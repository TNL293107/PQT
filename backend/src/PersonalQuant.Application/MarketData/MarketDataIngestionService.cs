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
        var provider = ResolveProvider(instruction);

        // The audit record is opened before anything can go wrong, and its
        // source is the one that was actually going to be read. When no
        // provider resolves there is nothing to attribute the attempt to, so
        // the refusal is reported to the caller instead of being written
        // against a source that does not exist.
        if (provider is null)
        {
            return await RecordUnattemptableAsync(
                instruction,
                startedAtUtc,
                instruction.Source is null
                    ? "No market data source is registered, or several are and none was named."
                    : $"No market data source is registered under the code '{instruction.Source}'.",
                cancellationToken).ConfigureAwait(false);
        }

        var instrument = await instruments
            .FindResultByIdAsync(instruction.InstrumentId, cancellationToken)
            .ConfigureAwait(false);

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

        if (!provider.SupportedIntervals.Contains(instruction.Interval))
        {
            return await CloseAsync(
                IngestionRun.Start(
                    provider.Code,
                    instruction.InstrumentId,
                    instruction.Interval,
                    startedAtUtc,
                    startedAtUtc.AddTicks(instruction.Interval.ToDuration().Ticks),
                    startedAtUtc),
                run => run.Skip(
                    $"'{provider.Code}' does not serve {instruction.Interval} bars.", clock.UtcNow),
                cancellationToken).ConfigureAwait(false);
        }

        var checkpoint = await journal
            .FindCheckpointAsync(
                instruction.InstrumentId, instruction.Interval, provider.Code, cancellationToken)
            .ConfigureAwait(false);

        var range = ResolveRange(instruction, checkpoint, startedAtUtc);

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

        AdvanceCheckpoint(request, provider.Code, checkpoint, normalized, ingestedAtUtc);

        run.Succeed(
            new IngestionCounts(
                fetched.Bars.Count,
                normalized.Accepted.Count,
                normalized.Rejected.Count,
                merge.Stored,
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
            merge.Stored,
            merge.Revised,
            normalized.Rejected.Count);

        LogRejections(instrument.Ticker.Value, provider.Code.Value, normalized);

        return run;
    }

    private IMarketDataProvider? ResolveProvider(IngestionInstruction instruction)
    {
        if (instruction.Source is null)
        {
            return registry.TryResolveDefault(out var single) ? single : null;
        }

        return registry.TryResolve(instruction.Source, out var named) ? named : null;
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
    /// </remarks>
    private (DateTimeOffset FromUtc, DateTimeOffset ToUtc)? ResolveRange(
        IngestionInstruction instruction,
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

        var maxSpan = interval.ToDuration() * MarketDataRequest.MaxPeriods;

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
    private async Task<(int Stored, int Revised)> MergeAsync(
        MarketDataRequest request,
        NormalizationResult normalized,
        SourceCode source,
        DateTimeOffset ingestedAtUtc,
        CancellationToken cancellationToken)
    {
        if (normalized.Accepted.Count == 0)
        {
            return (0, 0);
        }

        var existing = await bars
            .ListForUpdateAsync(
                request.InstrumentId,
                request.Interval,
                request.FromUtc,
                request.ToUtc,
                cancellationToken)
            .ConfigureAwait(false);

        var byPeriod = existing.ToDictionary(bar => bar.OpenedAtUtc);
        var toAdd = new List<OhlcvBar>(normalized.Accepted.Count);
        var revised = 0;

        foreach (var bar in normalized.Accepted)
        {
            if (!byPeriod.TryGetValue(bar.OpenedAtUtc, out var held))
            {
                toAdd.Add(bar);
                continue;
            }

            if (held.Revise(
                    bar.Open,
                    bar.High,
                    bar.Low,
                    bar.Close,
                    bar.Volume,
                    bar.Turnover,
                    source,
                    ingestedAtUtc))
            {
                revised++;
            }
        }

        if (toAdd.Count > 0)
        {
            bars.AddRange(toAdd);
        }

        return (toAdd.Count, revised);
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
