using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Application.MarketData;

/// <summary>
/// How much a series can be trusted, split into the four things that can go
/// wrong with it.
/// </summary>
/// <remarks>
/// <para>
/// Each component answers a different question and is measured against a
/// different denominator. Read them separately. <see cref="Overall"/> is a
/// summary for a dashboard, not a number to make a decision on — a series that
/// is 99% complete and 99% consistent is not interchangeable with one that is
/// 100% complete and 98% consistent, and the aggregate cannot tell them apart.
/// </para>
/// <para>
/// Every component is a fraction between zero and one. They are not
/// percentages: formatting is the caller's business, and a value that is
/// sometimes 0.99 and sometimes 99 is how a threshold ends up a hundred times
/// wrong.
/// </para>
/// </remarks>
/// <param name="Completeness">Bars stored, over trading days the calendar expects.</param>
/// <param name="Consistency">
/// Sessions with no open cross-session finding, over sessions stored.
/// </param>
/// <param name="Validity">
/// Rows the normaliser accepted, over rows providers returned.
/// </param>
/// <param name="SourceReliability">Ingestion runs that succeeded, over runs attempted.</param>
/// <param name="Overall">The weighted summary.</param>
public sealed record DataQualityScore(
    decimal Completeness,
    decimal Consistency,
    decimal Validity,
    decimal SourceReliability,
    decimal Overall)
{
    /// <summary>
    /// Weight given to completeness.
    /// </summary>
    /// <remarks>
    /// The largest, because a missing session is the failure research cannot
    /// work around. A backtest over a series with a week absent produces a
    /// number, and nothing about the number says the week is gone.
    /// </remarks>
    public const decimal CompletenessWeight = 0.4m;

    /// <summary>
    /// Weight given to consistency.
    /// </summary>
    /// <remarks>
    /// Second, because an unexplained discontinuity is usually a corporate
    /// action — real, correctable, and catastrophic if computed on unadjusted.
    /// </remarks>
    public const decimal ConsistencyWeight = 0.3m;

    /// <summary>
    /// Weight given to validity.
    /// </summary>
    /// <remarks>
    /// Lower, because a rejected row never entered the series. It measures the
    /// provider's output rather than what is stored.
    /// </remarks>
    public const decimal ValidityWeight = 0.2m;

    /// <summary>
    /// Weight given to source reliability.
    /// </summary>
    /// <remarks>
    /// Lowest, because a failed run that was later retried successfully leaves
    /// a complete series. It is a leading indicator of trouble, not a
    /// description of the data.
    /// </remarks>
    public const decimal SourceReliabilityWeight = 0.1m;

    /// <summary>A score for a window in which nothing was expected and nothing found.</summary>
    /// <remarks>
    /// One rather than zero. A window containing no trading days — a fortnight
    /// of Tet, or a range before the security listed — is not a defective
    /// series, and scoring it zero would drag every aggregate that averaged
    /// over it.
    /// </remarks>
    public static DataQualityScore Perfect { get; } = new(1m, 1m, 1m, 1m, 1m);

    /// <summary>
    /// Combines the four components into a weighted summary.
    /// </summary>
    /// <param name="completeness">Bars stored over sessions expected.</param>
    /// <param name="consistency">Clean sessions over sessions stored.</param>
    /// <param name="validity">Accepted rows over fetched rows.</param>
    /// <param name="sourceReliability">Successful runs over runs attempted.</param>
    /// <returns>The score.</returns>
    public static DataQualityScore From(
        decimal completeness,
        decimal consistency,
        decimal validity,
        decimal sourceReliability)
    {
        var bounded = (
            Completeness: Clamp(completeness),
            Consistency: Clamp(consistency),
            Validity: Clamp(validity),
            Reliability: Clamp(sourceReliability));

        var overall =
            (bounded.Completeness * CompletenessWeight)
            + (bounded.Consistency * ConsistencyWeight)
            + (bounded.Validity * ValidityWeight)
            + (bounded.Reliability * SourceReliabilityWeight);

        return new DataQualityScore(
            bounded.Completeness,
            bounded.Consistency,
            bounded.Validity,
            bounded.Reliability,
            Clamp(overall));
    }

    /// <summary>
    /// Divides, treating an empty denominator as nothing having gone wrong.
    /// </summary>
    /// <remarks>
    /// A ratio with no denominator is undefined, and the two ways of forcing a
    /// number out of it say opposite things. Zero would report a series nobody
    /// has ingested yet as maximally broken; one reports that nothing is known
    /// to be wrong, which is true. The count that produced the denominator
    /// travels beside the score so the difference is visible.
    /// </remarks>
    /// <param name="good">The numerator.</param>
    /// <param name="total">The denominator.</param>
    /// <returns>The ratio, or one when the denominator is zero.</returns>
    public static decimal Ratio(int good, int total) =>
        total <= 0 ? 1m : Clamp((decimal)good / total);

    private static decimal Clamp(decimal value) => Math.Clamp(value, 0m, 1m);
}

/// <summary>
/// A quality assessment of one series over one window.
/// </summary>
/// <remarks>
/// The counts travel with the score because a ratio alone cannot be acted on.
/// "Completeness 0.98" is a number; "245 of 250 sessions" is a number somebody
/// can go and look at.
/// </remarks>
/// <param name="InstrumentId">The instrument assessed.</param>
/// <param name="Ticker">Its ticker, for display.</param>
/// <param name="Interval">The resolution assessed.</param>
/// <param name="From">The first date covered.</param>
/// <param name="To">The last date covered.</param>
/// <param name="SessionsExpected">Trading days the calendar records in the window.</param>
/// <param name="BarsStored">Bars actually held.</param>
/// <param name="UnvalidatedBars">Bars not yet checked by the current rules.</param>
/// <param name="OpenIssues">Open findings by kind.</param>
/// <param name="Ingestion">What the ingestion runs over the window did.</param>
/// <param name="Score">The assessment.</param>
/// <param name="CalendarIsComplete">
/// Whether the venue's calendar actually covers the window. When it does not,
/// completeness is not measured and is reported as unknown rather than as a
/// number computed against a calendar that is not there.
/// </param>
public sealed record DataQualityReport(
    InstrumentId InstrumentId,
    Ticker Ticker,
    BarInterval Interval,
    DateOnly From,
    DateOnly To,
    int SessionsExpected,
    int BarsStored,
    int UnvalidatedBars,
    IReadOnlyDictionary<DataQualityIssueKind, int> OpenIssues,
    IngestionSummary Ingestion,
    DataQualityScore Score,
    bool CalendarIsComplete);
