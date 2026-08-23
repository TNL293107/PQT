using PersonalQuant.Application.Exchanges;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Application.MarketData;

/// <summary>
/// Reports how much a series can be trusted, and resolves what the rules found.
/// </summary>
/// <remarks>
/// The read side of the quality phase, kept apart from
/// <see cref="IBarQualityInspector"/> for the same reason the market data read
/// is kept apart from ingestion: reading a score happens on a dashboard and
/// must not be able to trigger a re-check.
/// </remarks>
public interface IDataQualityService
{
    /// <summary>
    /// Scores a series over a window.
    /// </summary>
    /// <param name="instrumentId">The instrument.</param>
    /// <param name="interval">The resolution.</param>
    /// <param name="fromDate">The first date to cover.</param>
    /// <param name="toDate">The last date to cover.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The report, or <see langword="null"/> when the instrument is unknown.</returns>
    Task<DataQualityReport?> ScoreAsync(
        InstrumentId instrumentId,
        BarInterval interval,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the open findings for a series, newest session first.
    /// </summary>
    /// <param name="instrumentId">The instrument.</param>
    /// <param name="interval">The resolution.</param>
    /// <param name="limit">How many to return.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The open findings.</returns>
    Task<IReadOnlyList<DataQualityIssue>> ListOpenIssuesAsync(
        InstrumentId instrumentId,
        BarInterval interval,
        int limit,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="IDataQualityService"/>.
/// </summary>
/// <param name="instruments">Resolves the instrument and its venue.</param>
/// <param name="calendar">Supplies the venue's trading days.</param>
/// <param name="bars">The canonical series.</param>
/// <param name="issues">What the rules found.</param>
/// <param name="journal">The ingestion audit trail.</param>
internal sealed class DataQualityService(
    IInstrumentRepository instruments,
    ITradingCalendar calendar,
    IBarRepository bars,
    IDataQualityRepository issues,
    IIngestionJournal journal) : IDataQualityService
{
    /// <summary>Most findings a caller may ask for in one read.</summary>
    private const int MaxIssues = 200;

    /// <inheritdoc />
    public async Task<DataQualityReport?> ScoreAsync(
        InstrumentId instrumentId,
        BarInterval interval,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default)
    {
        if (toDate < fromDate)
        {
            (fromDate, toDate) = (toDate, fromDate);
        }

        var instrument = await instruments
            .FindByIdAsync(instrumentId, cancellationToken)
            .ConfigureAwait(false);

        if (instrument is null)
        {
            return null;
        }

        var fromUtc = ToInstant(fromDate);

        // Exclusive at the end, and the window is inclusive of its last date,
        // so the boundary is the start of the day after.
        var toUtc = ToInstant(toDate.AddDays(1));

        var window = await calendar
            .LoadAsync(instrument.ExchangeId, fromDate, toDate, cancellationToken)
            .ConfigureAwait(false);

        var stored = await bars
            .ListForUpdateAsync(instrumentId, interval, fromUtc, toUtc, cancellationToken)
            .ConfigureAwait(false);

        var openByKind = await issues
            .CountOpenByKindAsync(instrumentId, interval, fromUtc, toUtc, cancellationToken)
            .ConfigureAwait(false);

        var ingestion = await journal
            .SummariseRunsAsync(instrumentId, interval, fromUtc, toUtc, cancellationToken)
            .ConfigureAwait(false);

        var expected = window.IsComplete ? window.TradingDays().Count() : 0;

        // Cross-session findings are the ones that describe a session that is
        // present but disagrees with its neighbour. A missing session is
        // already counted by completeness, and charging it twice would make
        // one absence lower two components.
        var inconsistent =
            Count(openByKind, DataQualityIssueKind.PriceLimitBreach)
            + Count(openByKind, DataQualityIssueKind.UnexpectedSession);

        var score = DataQualityScore.From(
            // Completeness is not measurable without a calendar that covers
            // the window, so it is reported as unknown rather than as a figure
            // computed against nothing. The flag beside it says which.
            window.IsComplete ? DataQualityScore.Ratio(stored.Count, expected) : 1m,
            DataQualityScore.Ratio(stored.Count - inconsistent, stored.Count),
            DataQualityScore.Ratio(ingestion.BarsAccepted, ingestion.BarsFetched),
            DataQualityScore.Ratio(ingestion.Succeeded, ingestion.Succeeded + ingestion.Failed));

        return new DataQualityReport(
            instrumentId,
            instrument.Ticker,
            interval,
            fromDate,
            toDate,
            expected,
            stored.Count,
            stored.Count(bar => bar.ValidationVersion < DataRules.ValidationVersion),
            openByKind,
            ingestion,
            score,
            window.IsComplete);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DataQualityIssue>> ListOpenIssuesAsync(
        InstrumentId instrumentId,
        BarInterval interval,
        int limit,
        CancellationToken cancellationToken = default) =>
        instrumentId.IsEmpty
            ? Task.FromResult<IReadOnlyList<DataQualityIssue>>([])
            : issues.ListOpenAsync(
                instrumentId, interval, Math.Clamp(limit, 1, MaxIssues), cancellationToken);

    private static int Count(
        IReadOnlyDictionary<DataQualityIssueKind, int> counts,
        DataQualityIssueKind kind) =>
        counts.TryGetValue(kind, out var count) ? count : 0;

    private static DateTimeOffset ToInstant(DateOnly date) =>
        new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
