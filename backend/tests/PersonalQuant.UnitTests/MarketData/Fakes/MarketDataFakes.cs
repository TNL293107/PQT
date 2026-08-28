using PersonalQuant.Application.Abstractions;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Domain.Currencies;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.UnitTests.MarketData.Fakes;

/// <summary>
/// In-memory stand-ins for the ports the ingestion pipeline writes through.
/// </summary>
/// <remarks>
/// <para>
/// The pipeline's rules — what is skipped, what is retried, where the
/// checkpoint lands, what a run records — are decisions made above the
/// database, and none of them need one. The SQL behind these ports is proved
/// separately, against real PostgreSQL.
/// </para>
/// <para>
/// The fakes deliberately do not commit on write. Whether the pipeline saves,
/// and how many times, is part of what the tests assert.
/// </para>
/// </remarks>
internal sealed class FakeClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;
}

/// <summary>Returns immediately and records nothing.</summary>
internal sealed class NoDelayScheduler : IDelayScheduler
{
    public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

/// <summary>Counts commits without performing one.</summary>
internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveCount { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCount++;

        return Task.FromResult(0);
    }
}

/// <summary>An instrument master holding a single security.</summary>
internal sealed class SingleInstrumentRepository : IInstrumentRepository
{
    private readonly InstrumentSearchResult? _instrument;

    public SingleInstrumentRepository(InstrumentSearchResult? instrument) =>
        _instrument = instrument;

    public static InstrumentSearchResult Known(InstrumentId id) =>
        new(
            id,
            Ticker.Create("FPT"),
            "FPT Corporation",
            AssetType.Equity,
            ExchangeCode.Create("HOSE"),
            CurrencyCode.Vnd,
            InstrumentStatus.Listed,
            MatchKind: null);

    public Task<InstrumentSearchResult?> FindResultByIdAsync(
        InstrumentId id,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_instrument is not null && _instrument.InstrumentId == id
            ? _instrument
            : null);

    public Task<Instrument?> FindByIdAsync(
        InstrumentId id,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not exercised by ingestion.");

    public Task<Instrument?> FindActiveByTickerAsync(
        ExchangeId exchangeId,
        Ticker ticker,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not exercised by ingestion.");

    public Task<bool> IsTickerTakenAsync(
        ExchangeId exchangeId,
        Ticker ticker,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not exercised by ingestion.");

    public Task<IReadOnlyList<InstrumentSearchResult>> SearchAsync(
        InstrumentSearchCriteria criteria,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not exercised by ingestion.");

    public Task<IReadOnlyList<InstrumentSearchResult>> ListActiveByTickerAsync(
        Ticker ticker,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not exercised by ingestion.");

    public Task<InstrumentDetail?> FindDetailByIdAsync(
        InstrumentId id,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not exercised by ingestion.");

    public Task<InstrumentPage> ListAsync(
        InstrumentListCriteria criteria,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not exercised by ingestion.");

    public Task<InstrumentIdentifier?> FindIdentifierAsync(
        IdentifierValue value,
        SourceCode? source,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not exercised by ingestion.");

    public Task<IReadOnlyList<InstrumentIdentifier>> ListIdentifiersAsync(
        InstrumentId id,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not exercised by ingestion.");

    public Task<IReadOnlyList<RelatedInstrument>> ListRelatedAsync(
        InstrumentId id,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not exercised by ingestion.");

    public void AddIdentifier(InstrumentIdentifier identifier) =>
        throw new NotSupportedException("Not exercised by ingestion.");

    public void Add(Instrument instrument) =>
        throw new NotSupportedException("Not exercised by ingestion.");
}

/// <summary>An in-memory series, keyed the way the real table is.</summary>
internal sealed class FakeBarRepository : IBarRepository
{
    private readonly Dictionary<(InstrumentId Instrument, BarInterval Interval, DateTimeOffset Period), OhlcvBar> _bars = [];

    public IReadOnlyCollection<OhlcvBar> All => _bars.Values;

    /// <summary>The observation history, in the order it was written.</summary>
    public List<BarRevision> Revisions { get; } = [];

    public Task<IReadOnlyList<OhlcvBar>> ListForUpdateAsync(
        InstrumentId instrumentId,
        BarInterval interval,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<OhlcvBar>>(
            [.. _bars.Values
                .Where(bar =>
                    bar.InstrumentId == instrumentId
                    && bar.Interval == interval
                    && bar.OpenedAtUtc >= fromUtc
                    && bar.OpenedAtUtc < toUtc)
                .OrderBy(bar => bar.OpenedAtUtc)]);

    public Task<IReadOnlyList<OhlcvBar>> QueryAsync(
        BarQuery query,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<OhlcvBar>>(
            [.. _bars.Values
                .Where(bar =>
                    bar.InstrumentId == query.InstrumentId && bar.Interval == query.Interval)
                .OrderBy(bar => bar.OpenedAtUtc)
                .TakeLast(query.Limit)]);

    public Task<OhlcvBar?> FindLastBeforeAsync(
        InstrumentId instrumentId,
        BarInterval interval,
        DateTimeOffset beforeUtc,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_bars.Values
            .Where(bar =>
                bar.InstrumentId == instrumentId
                && bar.Interval == interval
                && bar.OpenedAtUtc < beforeUtc)
            .MaxBy(bar => bar.OpenedAtUtc));

    public Task<IReadOnlyList<BarRevision>> QueryAsOfAsync(
        BarQuery query,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<BarRevision>>(
            [.. Revisions
                .Where(revision =>
                    revision.InstrumentId == query.InstrumentId
                    && revision.Interval == query.Interval
                    && query.KnownAsOfUtc is { } asOf
                    && revision.WasKnownAt(asOf))
                .OrderBy(revision => revision.OpenedAtUtc)
                .TakeLast(query.Limit)]);

    public Task<IReadOnlyList<BarRevision>> ListOpenRevisionsForUpdateAsync(
        InstrumentId instrumentId,
        BarInterval interval,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<BarRevision>>(
            [.. Revisions
                .Where(revision =>
                    revision.InstrumentId == instrumentId
                    && revision.Interval == interval
                    && revision.OpenedAtUtc >= fromUtc
                    && revision.OpenedAtUtc < toUtc
                    && revision.IsCurrent)
                .OrderBy(revision => revision.OpenedAtUtc)]);

    public void AddRange(IReadOnlyList<OhlcvBar> bars)
    {
        foreach (var bar in bars)
        {
            _bars[(bar.InstrumentId, bar.Interval, bar.OpenedAtUtc)] = bar;
        }
    }

    public void AddRevisions(IReadOnlyList<BarRevision> revisions) =>
        Revisions.AddRange(revisions);
}

/// <summary>An in-memory ingestion journal.</summary>
internal sealed class FakeIngestionJournal : IIngestionJournal
{
    public List<RawMarketDataBatch> RawBatches { get; } = [];

    public List<IngestionRun> Runs { get; } = [];

    public List<IngestionCheckpoint> Checkpoints { get; } = [];

    public void AddRawBatch(RawMarketDataBatch batch) => RawBatches.Add(batch);

    public void AddRun(IngestionRun run)
    {
        if (!Runs.Contains(run))
        {
            Runs.Add(run);
        }
    }

    public Task<IReadOnlyList<IngestionRun>> ListRecentRunsAsync(
        InstrumentId instrumentId,
        BarInterval interval,
        int limit,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<IngestionRun>>(
            [.. Runs
                .Where(run => run.InstrumentId == instrumentId && run.Interval == interval)
                .OrderByDescending(run => run.StartedAtUtc)
                .Take(limit)]);

    public Task<IngestionSummary> SummariseRunsAsync(
        InstrumentId instrumentId,
        BarInterval interval,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
    {
        var matching = Runs
            .Where(run =>
                run.InstrumentId == instrumentId
                && run.Interval == interval
                && run.StartedAtUtc >= fromUtc
                && run.StartedAtUtc < toUtc)
            .ToList();

        return Task.FromResult(new IngestionSummary(
            matching.Count,
            matching.Count(run => run.Outcome == IngestionOutcome.Succeeded),
            matching.Count(run => run.Outcome == IngestionOutcome.Failed),
            matching.Count(run => run.Outcome == IngestionOutcome.Skipped),
            matching.Sum(run => run.BarsFetched),
            matching.Sum(run => run.BarsAccepted),
            matching.Sum(run => run.BarsRejected)));
    }

    public Task<IngestionCheckpoint?> FindCheckpointAsync(
        InstrumentId instrumentId,
        BarInterval interval,
        SourceCode source,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Checkpoints.FirstOrDefault(checkpoint =>
            checkpoint.InstrumentId == instrumentId
            && checkpoint.Interval == interval
            && checkpoint.Source == source));

    public void AddCheckpoint(IngestionCheckpoint checkpoint) => Checkpoints.Add(checkpoint);
}

/// <summary>A provider returning whatever a test hands it.</summary>
internal sealed class ScriptedProvider(
    SourceCode code,
    Func<MarketDataRequest, Task<MarketDataFetchResult>> behaviour) : IMarketDataProvider
{
    public int CallCount { get; private set; }

    public SourceCode Code { get; } = code;

    public IReadOnlySet<BarInterval> SupportedIntervals { get; init; } =
        new HashSet<BarInterval>
        {
            BarInterval.OneMinute,
            BarInterval.FiveMinutes,
            BarInterval.FifteenMinutes,
            BarInterval.ThirtyMinutes,
            BarInterval.OneHour,
            BarInterval.OneDay,
        };

    public Task<MarketDataFetchResult> FetchBarsAsync(
        MarketDataRequest request,
        CancellationToken cancellationToken = default)
    {
        CallCount++;

        return behaviour(request);
    }
}

/// <summary>
/// A quality inspector that records what it was asked and finds nothing.
/// </summary>
/// <remarks>
/// The ingestion tests are about fetch, deduplicate, checkpoint and audit. What
/// the quality rules conclude is a separate question with its own tests, and
/// running the real inspector here would make every ingestion test depend on a
/// trading calendar it does not care about.
/// </remarks>
internal sealed class NoOpQualityInspector : IBarQualityInspector
{
    public int InspectionCount { get; private set; }

    public IReadOnlyList<OhlcvBar> LastPending { get; private set; } = [];

    public Task<QualityInspection> InspectAsync(
        InstrumentId instrumentId,
        BarInterval interval,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        IReadOnlyList<OhlcvBar> pending,
        CancellationToken cancellationToken = default)
    {
        InspectionCount++;
        LastPending = pending;

        return Task.FromResult(new QualityInspection(pending.Count, 0, [], null));
    }
}
