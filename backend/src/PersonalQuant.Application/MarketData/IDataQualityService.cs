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

    /// <summary>
    /// Records that something accounts for a finding, or that it was
    /// investigated and is not a problem.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The other half of "a finding stays open until something accounts for
    /// it". Without a way to close one, the open set only ever grows and the
    /// consistency score decays permanently — a series that was explained
    /// years ago would still be scored as though nobody had looked.
    /// </para>
    /// <para>
    /// Phase 4 is the caller: it matches an open price-limit finding to a
    /// corporate action and explains it, which is a recorded resolution rather
    /// than an edit. A human-facing surface waits for the authentication in
    /// Phase 18, because an anonymous caller able to dismiss findings could
    /// hide real corruption.
    /// </para>
    /// </remarks>
    /// <param name="issueId">The finding to close.</param>
    /// <param name="outcome">Whether it is explained or dismissed.</param>
    /// <param name="reason">What accounts for it, or why it is not a problem.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The closed finding, or <see langword="null"/> when no such finding
    /// exists.
    /// </returns>
    /// <exception cref="Domain.Common.DomainStateException">
    /// The finding has already been closed.
    /// </exception>
    Task<DataQualityIssue?> ResolveIssueAsync(
        DataQualityIssueId issueId,
        DataQualityResolution outcome,
        string reason,
        CancellationToken cancellationToken = default);
}

/// <summary>How a finding is being closed.</summary>
/// <remarks>
/// Two words, and they mean opposite things about the data. "Explained" says
/// the discontinuity was real and something accounts for it; "dismissed" says
/// there was nothing there. Collapsing them into one "resolved" would lose the
/// distinction that matters when the series is read years later.
/// </remarks>
public enum DataQualityResolution
{
    /// <summary>Something known accounts for it — a corporate action, a halt.</summary>
    Explained = 1,

    /// <summary>Investigated and found not to be a problem.</summary>
    Dismissed = 2,
}

/// <summary>
/// Default <see cref="IDataQualityService"/>.
/// </summary>
/// <param name="instruments">Resolves the instrument and its venue.</param>
/// <param name="calendar">Supplies the venue's trading days.</param>
/// <param name="bars">The canonical series.</param>
/// <param name="issues">What the rules found.</param>
/// <param name="journal">The ingestion audit trail.</param>
/// <param name="unitOfWork">Commits a resolution.</param>
/// <param name="clock">Supplies the resolution instant.</param>
internal sealed class DataQualityService(
    IInstrumentRepository instruments,
    ITradingCalendar calendar,
    IBarRepository bars,
    IDataQualityRepository issues,
    IIngestionJournal journal,
    Abstractions.IUnitOfWork unitOfWork,
    Abstractions.IClock clock) : IDataQualityService
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

    /// <inheritdoc />
    public async Task<DataQualityIssue?> ResolveIssueAsync(
        DataQualityIssueId issueId,
        DataQualityResolution outcome,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (issueId.IsEmpty)
        {
            return null;
        }

        var issue = await issues.FindAsync(issueId, cancellationToken).ConfigureAwait(false);

        if (issue is null)
        {
            return null;
        }

        // The aggregate decides whether the transition is legal — closing an
        // already-closed finding would erase the audit trail it exists to
        // leave — so the refusal propagates rather than being translated here.
        if (outcome == DataQualityResolution.Explained)
        {
            issue.Explain(reason, clock.UtcNow);
        }
        else
        {
            issue.Dismiss(reason, clock.UtcNow);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return issue;
    }

    private static int Count(
        IReadOnlyDictionary<DataQualityIssueKind, int> counts,
        DataQualityIssueKind kind) =>
        counts.TryGetValue(kind, out var count) ? count : 0;

    private static DateTimeOffset ToInstant(DateOnly date) =>
        new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
