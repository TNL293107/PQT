using Microsoft.Extensions.Logging;
using PersonalQuant.Application.Abstractions;
using PersonalQuant.Application.Diagnostics;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Application.MarketData;

/// <summary>
/// Default <see cref="IMarketDataIngestionService"/>.
/// </summary>
/// <remarks>
/// <para>
/// The pipeline the roadmap describes, in order: fetch, validate, normalize,
/// deduplicate, persist, audit. Each step is a method below, and the order is
/// the contract — deduplicating before validating would compare a corrupt row
/// against a stored one, and auditing before persisting would record work that
/// a failed commit then rolls back.
/// </para>
/// <para>
/// Everything a run produces — the raw payload, the bars, the advanced
/// checkpoint and the audit record — is committed in a single unit of work.
/// A checkpoint that survives while the bars it covers do not is the one
/// failure mode that leaves a permanent, silent hole: the next run resumes
/// past data that was never stored, and nothing downstream can tell the gap
/// from a market holiday.
/// </para>
/// <para>
/// A failed run is committed too, on its own. That is deliberate: the audit
/// table's purpose is to explain gaps, and it can only do that if a failure
/// leaves a row.
/// </para>
/// </remarks>
/// <param name="instruments">Resolves the instrument to a ticker and venue.</param>
/// <param name="registry">The registered sources.</param>
/// <param name="fetcher">Calls the source under the retry and spacing policy.</param>
/// <param name="normalizer">Validates and converts what the source returned.</param>
/// <param name="inspector">Applies the quality rules that span sessions.</param>
/// <param name="bars">The canonical series.</param>
/// <param name="journal">Raw payloads, run history and checkpoints.</param>
/// <param name="unitOfWork">Commits the run.</param>
/// <param name="policy">Backfill and range settings.</param>
/// <param name="clock">Supplies the current instant.</param>
/// <param name="logger">Logger for ingestion telemetry.</param>
internal sealed class MarketDataIngestionService(
    IInstrumentRepository instruments,
    IMarketDataProviderRegistry registry,
    IMarketDataFetcher fetcher,
    IMarketDataNormalizer normalizer,
    IBarQualityInspector inspector,
    IBarRepository bars,
    IIngestionJournal journal,
    IUnitOfWork unitOfWork,
    IngestionPolicy policy,
    IClock clock,
    ILogger<MarketDataIngestionService> logger) : IMarketDataIngestionService
{
    /// <inheritdoc />
    public async Task<IngestionRun> IngestAsync(
        IngestionInstruction instruction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instruction);

        var startedAtUtc = clock.UtcNow;

        // Read before the source is chosen, and with no side effect either
        // way. The venue and the asset type are selection criteria — a source
        // that covers HOSE and not UPCOM has to be able to refuse the second —
        // and they are only knowable from the instrument.
        var instrument = await instruments
            .FindResultByIdAsync(instruction.InstrumentId, cancellationToken)
            .ConfigureAwait(false);

        var selection = registry.SelectProvider(new ProviderCriteria(
            instruction.Interval,
            instruction.Source,
            instrument?.ExchangeCode,
            instrument?.AssetType));

        // No source was going to be read, so there is nothing to attribute the
        // attempt to. The run is recorded against the reserved code and the
        // reason names what failed — which source, and on which dimension.
        // Nothing is tried after this: a second source is a second answer, and
        // falling through to one would assemble a series from two symbologies.
        if (selection.Provider is not { } provider)
        {
            return await RecordUnattemptableAsync(
                instruction,
                startedAtUtc,
                selection.Reason ?? "No market data source could serve the request.",
                cancellationToken).ConfigureAwait(false);
        }

        if (instrument is null)
        {
            return await CloseAsync(
                IngestionRun.Start(
                    provider.Code,
                    instruction.InstrumentId,
                    instruction.Interval,
                    startedAtUtc,
                    startedAtUtc.AddTicks(instruction.Interval.ToDuration().Ticks),
                    startedAtUtc),
                run => run.Skip("No instrument exists with that identifier.", clock.UtcNow),
                cancellationToken).ConfigureAwait(false);
        }

        // V9, and refused before anything is fetched. A source that adjusts
        // prices for corporate actions and one that does not are two different
        // datasets that happen to share a shape: merging them produces a series
        // wrong by the product of every factor since, and no quality rule can
        // see it because every individual bar is plausible. The refusal names
        // both sources, because the next question an operator asks is which two.
        if (await FindAdjustmentConflictAsync(provider, instruction, cancellationToken)
                .ConfigureAwait(false) is { } conflict)
        {
            return await CloseAsync(
                IngestionRun.Start(
                    provider.Code,
                    instruction.InstrumentId,
                    instruction.Interval,
                    startedAtUtc,
                    startedAtUtc.AddTicks(instruction.Interval.ToDuration().Ticks),
                    startedAtUtc),
                run => run.Skip(conflict, clock.UtcNow),
                cancellationToken).ConfigureAwait(false);
        }

        var checkpoint = await journal
            .FindCheckpointAsync(
                instruction.InstrumentId, instruction.Interval, provider.Code, cancellationToken)
            .ConfigureAwait(false);

        var range = ResolveRange(instruction, provider, checkpoint, startedAtUtc);

        if (range is null)
        {
            // Recorded rather than returned silently. A schedule that has
            // nothing to do every night is either correct or broken, and only
            // the written-down skips distinguish the two.
            return await CloseAsync(
                IngestionRun.Start(
                    provider.Code,
                    instruction.InstrumentId,
                    instruction.Interval,
                    startedAtUtc,
                    startedAtUtc.AddTicks(instruction.Interval.ToDuration().Ticks),
                    startedAtUtc),
                run => run.Skip("No period has finished since the last run.", clock.UtcNow),
                cancellationToken).ConfigureAwait(false);
        }

        var (fromUtc, toUtc) = range.Value;

        if (!MarketDataRequest.TryCreate(
                instruction.InstrumentId,
                instrument.Ticker,
                instrument.ExchangeCode,
                instruction.Interval,
                fromUtc,
                toUtc,
                out var request,
                out var problem))
        {
            return await CloseAsync(
                IngestionRun.Start(
                    provider.Code,
                    instruction.InstrumentId,
                    instruction.Interval,
                    startedAtUtc,
                    toUtc,
                    startedAtUtc),
                run => run.Skip(problem, clock.UtcNow),
                cancellationToken).ConfigureAwait(false);
        }

        var run = IngestionRun.Start(
            provider.Code,
            request.InstrumentId,
            request.Interval,
            request.FromUtc,
            request.ToUtc,
            startedAtUtc);

        var attempt = await fetcher
            .FetchAsync(provider, request, cancellationToken)
            .ConfigureAwait(false);

        if (!attempt.Succeeded)
        {
            ApplicationLog.MarketDataIngestionFailed(
                logger,
                provider.Code.Value,
                instrument.Ticker.Value,
                request.Interval,
                attempt.FailureReason ?? string.Empty);

            return await CloseAsync(
                run,
                closing => closing.Fail(
                    attempt.FailureReason ?? "The provider could not be read.",
                    attempt.Attempts,
                    clock.UtcNow),
                cancellationToken).ConfigureAwait(false);
        }

        var fetched = attempt.Result!;
        var ingestedAtUtc = clock.UtcNow;

        var batch = RawMarketDataBatch.Retain(
            provider.Code,
            request.InstrumentId,
            request.Interval,
            request.FromUtc,
            request.ToUtc,
            fetched.Payload,
            fetched.ContentType,
            ingestedAtUtc);

        journal.AddRawBatch(batch);

        var normalized = normalizer.Normalize(request, provider.Code, fetched.Bars, ingestedAtUtc);
        var merge = await MergeAsync(request, normalized, provider.Code, ingestedAtUtc, cancellationToken)
            .ConfigureAwait(false);

        // Inspected before the commit, and handed the bars this run has staged
        // but not yet written. A bar committed without the finding about it
        // would look clean, and nothing would know to re-check it.
        await inspector
            .InspectAsync(
                request.InstrumentId,
                request.Interval,
                request.FromUtc,
                request.ToUtc,
                merge.Added,
                cancellationToken)
            .ConfigureAwait(false);

        AdvanceCheckpoint(request, provider.Code, checkpoint, normalized, ingestedAtUtc);

        run.Succeed(
            new IngestionCounts(
                fetched.Bars.Count,
                normalized.Accepted.Count,
                normalized.Rejected.Count,
                merge.Added.Count,
                merge.Revised),
            attempt.Attempts,
            batch.Id,
            clock.UtcNow);

        journal.AddRun(run);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        ApplicationLog.MarketDataIngested(
            logger,
            provider.Code.Value,
            instrument.Ticker.Value,
            request.Interval,
            merge.Added.Count,
            merge.Revised,
            normalized.Rejected.Count);

        LogRejections(instrument.Ticker.Value, provider.Code.Value, normalized);

        return run;
    }

    /// <summary>
    /// Works out the half-open range to request.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The end is the start of the current period, never the current instant:
    /// the period in progress has not finished, and a bar for it would be
    /// provisional.
    /// </para>
    /// <para>
    /// A range longer than one request may carry is truncated rather than
    /// refused. The checkpoint then resumes where this run stopped, so a large
    /// backfill completes over several runs instead of failing on the first.
    /// </para>
    /// <para>
    /// V10. The bound is the smaller of what this system permits and what the
    /// source declares it can carry. A declared limit that nothing enforced
    /// would be the worst of both: rendered on the operator surface as a
    /// promise, and silently broken on every backfill — which against a source
    /// that truncates a long response rather than refusing it means bars
    /// quietly missing from a range the run records as covered.
    /// </para>
    /// </remarks>
    private (DateTimeOffset FromUtc, DateTimeOffset ToUtc)? ResolveRange(
        IngestionInstruction instruction,
        IMarketDataProvider provider,
        IngestionCheckpoint? checkpoint,
        DateTimeOffset nowUtc)
    {
        var interval = instruction.Interval;

        var fromUtc = instruction.FromUtc is { } explicitFrom
            ? IngestionPolicy.FloorTo(explicitFrom, interval)
            : checkpoint?.ResumeFromUtc ?? policy.InitialFrom(interval, nowUtc);

        var ceiling = IngestionPolicy.FloorTo(nowUtc, interval);

        var toUtc = instruction.ToUtc is { } explicitTo
            ? IngestionPolicy.FloorTo(explicitTo, interval)
            : ceiling;

        // An explicit end beyond the last finished period is clamped rather
        // than honoured, so asking for "everything up to now" never stores a
        // partial bar.
        if (toUtc > ceiling)
        {
            toUtc = ceiling;
        }

        if (toUtc <= fromUtc)
        {
            return null;
        }

        // Periods here are the interval's own length, so a source declaring 65
        // for a daily series gets 65 calendar days — around forty-five trading
        // sessions. That is deliberately under any cap the vendor expresses in
        // sessions: sessions per calendar day is not a constant, and a bound
        // that is occasionally exceeded is a bound that occasionally loses data.
        var maxPeriods = Math.Min(
            provider.Capability.Limitations.MaxPeriodsPerCall ?? MarketDataRequest.MaxPeriods,
            MarketDataRequest.MaxPeriods);

        var maxSpan = interval.ToDuration() * maxPeriods;

        if (toUtc - fromUtc > maxSpan)
        {
            toUtc = fromUtc + maxSpan;
        }

        return (fromUtc, toUtc);
    }

    /// <summary>
    /// Deduplicates the accepted bars against what is already stored.
    /// </summary>
    /// <remarks>
    /// The dedupe rule is the storage key — instrument, interval, opening
    /// instant — so a period already held is never inserted twice. What
    /// happens instead is a restatement, and only when the values actually
    /// differ; re-fetching an unchanged range is the normal case and counts as
    /// neither stored nor revised.
    /// </remarks>
    private async Task<MergeOutcome> MergeAsync(
        MarketDataRequest request,
        NormalizationResult normalized,
        SourceCode source,
        DateTimeOffset ingestedAtUtc,
        CancellationToken cancellationToken)
    {
        if (normalized.Accepted.Count == 0)
        {
            return MergeOutcome.Nothing;
        }

        var existing = await bars
            .ListForUpdateAsync(
                request.InstrumentId,
                request.Interval,
                request.FromUtc,
                request.ToUtc,
                cancellationToken)
            .ConfigureAwait(false);

        // The observation history of the same range. Loaded alongside the bars
        // rather than per restatement, because a run that restates forty
        // periods would otherwise issue forty queries for rows one query
        // already covers.
        var openRevisions = await bars
            .ListOpenRevisionsForUpdateAsync(
                request.InstrumentId,
                request.Interval,
                request.FromUtc,
                request.ToUtc,
                cancellationToken)
            .ConfigureAwait(false);

        var byPeriod = existing.ToDictionary(bar => bar.OpenedAtUtc);
        var openByPeriod = openRevisions.ToDictionary(revision => revision.OpenedAtUtc);
        var toAdd = new List<OhlcvBar>(normalized.Accepted.Count);
        var history = new List<BarRevision>(normalized.Accepted.Count);
        var revised = 0;

        foreach (var bar in normalized.Accepted)
        {
            if (!byPeriod.TryGetValue(bar.OpenedAtUtc, out var held))
            {
                toAdd.Add(bar);
                history.Add(BarRevision.Snapshot(bar, ingestedAtUtc));
                continue;
            }

            if (!held.Revise(
                    bar.Open,
                    bar.High,
                    bar.Low,
                    bar.Close,
                    bar.Volume,
                    bar.Turnover,
                    source,
                    ingestedAtUtc))
            {
                // Re-fetching an unchanged period. Nothing moved, so nothing is
                // observed anew and the history must not grow — a revision row
                // per fetch would make the record of what changed unreadable.
                continue;
            }

            revised++;

            // Both edges take the run's instant, never a second clock read.
            // The window a statement was held for ends exactly where its
            // successor's begins, so no as-of instant can fall between them and
            // find the period missing.
            if (openByPeriod.TryGetValue(bar.OpenedAtUtc, out var superseded))
            {
                superseded.Supersede(ingestedAtUtc);
            }

            history.Add(BarRevision.Snapshot(held, ingestedAtUtc));
        }

        if (toAdd.Count > 0)
        {
            bars.AddRange(toAdd);
        }

        bars.AddRevisions(history);

        return new MergeOutcome(toAdd, revised);
    }

    /// <summary>
    /// Moves the resume position to the newest bar this run actually stored.
    /// </summary>
    /// <remarks>
    /// Never to the end of the requested range. A request for a week that
    /// returned three days must resume on the fourth; resuming from the
    /// requested end would skip the rest of the week permanently, and the
    /// checkpoint would then assert that the missing days had been covered.
    /// </remarks>
    private void AdvanceCheckpoint(
        MarketDataRequest request,
        SourceCode source,
        IngestionCheckpoint? checkpoint,
        NormalizationResult normalized,
        DateTimeOffset succeededAtUtc)
    {
        var lastOpenedAtUtc = normalized.LastAcceptedOpenedAtUtc;

        if (checkpoint is null)
        {
            if (lastOpenedAtUtc is { } first)
            {
                journal.AddCheckpoint(IngestionCheckpoint.Start(
                    request.InstrumentId, request.Interval, source, first, succeededAtUtc));
            }

            // With no checkpoint and nothing returned there is nothing to
            // record a position from. Creating one at the requested start
            // would claim a range had been covered that produced no data.
            return;
        }

        if (lastOpenedAtUtc is { } advanced)
        {
            checkpoint.Advance(advanced, succeededAtUtc);
        }
        else
        {
            checkpoint.RecordSuccessWithoutProgress(succeededAtUtc);
        }
    }

    /// <summary>
    /// Reports why the chosen source may not write into this series, or
    /// <see langword="null"/> when it may.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rule is symmetric, although the specification states it one way
    /// round. Appending raw prices to a source-adjusted history and appending
    /// source-adjusted prices to a raw one produce the same mixture, and a rule
    /// that refused only one direction would be defeated by the order the two
    /// runs happened to execute in.
    /// </para>
    /// <para>
    /// The whole series is read, not the range being fetched. A raw range
    /// appended after a source-adjusted history is the same wrong dataset as
    /// one written into the middle of it.
    /// </para>
    /// <para>
    /// A source that is no longer registered cannot be asked what it did, and
    /// is read as raw — what every source was before one declared otherwise,
    /// and the same assumption the adjusted read makes, so the two can never
    /// disagree about one series.
    /// </para>
    /// </remarks>
    private async Task<string?> FindAdjustmentConflictAsync(
        IMarketDataProvider provider,
        IngestionInstruction instruction,
        CancellationToken cancellationToken)
    {
        var held = await bars
            .ListSourcesAsync(instruction.InstrumentId, instruction.Interval, cancellationToken)
            .ConfigureAwait(false);

        if (held.Count == 0)
        {
            return null;
        }

        var writerAdjusts = provider.Capability.Limitations.AdjustsPricesAtSource;

        foreach (var holder in held)
        {
            if (holder == provider.Code || AdjustsAtSource(holder) == writerAdjusts)
            {
                continue;
            }

            return $"'{provider.Code}' {Describe(writerAdjusts)}, and the {instruction.Interval} "
                + $"series already holds bars from '{holder}', which {Describe(!writerAdjusts)}. "
                + "They are different datasets and must not be merged.";
        }

        return null;
    }

    /// <summary>
    /// Reports whether the source that produced held bars had already adjusted
    /// them.
    /// </summary>
    private bool AdjustsAtSource(SourceCode source) =>
        registry.TryResolve(source, out var provider)
        && provider.Capability.Limitations.AdjustsPricesAtSource;

    private static string Describe(bool adjustsAtSource) =>
        adjustsAtSource
            ? "serves prices already adjusted for corporate actions"
            : "serves raw prices";

    private async Task<IngestionRun> RecordUnattemptableAsync(
        IngestionInstruction instruction,
        DateTimeOffset startedAtUtc,
        string reason,
        CancellationToken cancellationToken)
    {
        // There is no source to attribute the attempt to, so the run is
        // recorded against a reserved code rather than invented or omitted.
        var run = IngestionRun.Start(
            SourceCode.Create("UNRESOLVED"),
            instruction.InstrumentId,
            instruction.Interval,
            startedAtUtc,
            startedAtUtc.AddTicks(instruction.Interval.ToDuration().Ticks),
            startedAtUtc);

        return await CloseAsync(run, closing => closing.Skip(reason, clock.UtcNow), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<IngestionRun> CloseAsync(
        IngestionRun run,
        Action<IngestionRun> close,
        CancellationToken cancellationToken)
    {
        close(run);
        journal.AddRun(run);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return run;
    }

    /// <summary>
    /// What the deduplication step did, and the bars it staged.
    /// </summary>
    /// <remarks>
    /// The added bars travel with the counts because the quality check needs
    /// them: they are staged in this unit of work and a database query cannot
    /// see them yet.
    /// </remarks>
    /// <param name="Added">Periods not previously held, staged for insert.</param>
    /// <param name="Revised">Periods already held that the source restated.</param>
    private sealed record MergeOutcome(IReadOnlyList<OhlcvBar> Added, int Revised)
    {
        public static MergeOutcome Nothing { get; } = new([], 0);
    }

    private void LogRejections(string ticker, string source, NormalizationResult normalized)
    {
        if (normalized.Rejected.Count == 0 || !logger.IsEnabled(LogLevel.Warning))
        {
            return;
        }

        // Grouped by reason rather than logged per row. A response with a
        // swapped column pair rejects every row it contains, and one line
        // saying so is readable where a thousand are not.
        foreach (var group in normalized.Rejected.GroupBy(rejection => rejection.Reason))
        {
            ApplicationLog.MarketDataBarsRejected(
                logger,
                source,
                ticker,
                group.Key,
                group.Count(),
                group.First().Detail);
        }
    }
}
