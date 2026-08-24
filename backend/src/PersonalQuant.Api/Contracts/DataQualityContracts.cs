using PersonalQuant.Application.MarketData;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Api.Contracts;

/// <summary>
/// The four components of a quality assessment, and their weighted summary.
/// </summary>
/// <remarks>
/// Fractions between zero and one, not percentages. Formatting is the client's
/// business, and a field that is sometimes 0.99 and sometimes 99 is how a
/// threshold ends up a hundred times wrong.
/// </remarks>
/// <param name="Completeness">Bars stored, over trading days the calendar expects.</param>
/// <param name="Consistency">Sessions with no open cross-session finding, over sessions stored.</param>
/// <param name="Validity">Rows the normaliser accepted, over rows providers returned.</param>
/// <param name="SourceReliability">Ingestion runs that succeeded, over runs attempted.</param>
/// <param name="Overall">The weighted summary.</param>
public sealed record DataQualityScoreResponse(
    decimal Completeness,
    decimal Consistency,
    decimal Validity,
    decimal SourceReliability,
    decimal Overall);

/// <summary>
/// One finding the quality rules recorded.
/// </summary>
/// <param name="IssueId">The finding's identifier.</param>
/// <param name="SessionAtUtc">The session it concerns.</param>
/// <param name="Kind">PriceLimitBreach, MissingSession or UnexpectedSession.</param>
/// <param name="Status">Open, Explained or Dismissed.</param>
/// <param name="Detail">The specifics, including the numbers that triggered it.</param>
/// <param name="DetectedAtUtc">When it was found.</param>
/// <param name="ResolvedAtUtc">When it stopped being open, if it has.</param>
/// <param name="Resolution">What accounted for it, once something did.</param>
public sealed record DataQualityIssueResponse(
    Guid IssueId,
    DateTimeOffset SessionAtUtc,
    string Kind,
    string Status,
    string Detail,
    DateTimeOffset DetectedAtUtc,
    DateTimeOffset? ResolvedAtUtc,
    string? Resolution)
{
    /// <summary>Projects a finding onto the wire contract.</summary>
    /// <param name="issue">The finding to project.</param>
    /// <returns>The response representation.</returns>
    public static DataQualityIssueResponse From(DataQualityIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);

        return new DataQualityIssueResponse(
            issue.Id.Value,
            issue.SessionAtUtc,
            issue.Kind.ToString(),
            issue.Status.ToString(),
            issue.Detail,
            issue.DetectedAtUtc,
            issue.ResolvedAtUtc,
            issue.Resolution);
    }
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
/// <param name="Interval">The resolution, by name.</param>
/// <param name="FromDate">The first date covered.</param>
/// <param name="ToDate">The last date covered.</param>
/// <param name="SessionsExpected">Trading days the calendar records in the window.</param>
/// <param name="BarsStored">Bars actually held.</param>
/// <param name="UnvalidatedBars">Bars not yet checked by the current rules.</param>
/// <param name="OpenIssues">Open findings by kind.</param>
/// <param name="Ingestion">What the ingestion runs over the window did.</param>
/// <param name="Score">The assessment.</param>
/// <param name="CalendarIsComplete">
/// Whether the venue's calendar covers the window. When it does not,
/// completeness is not measured and the figure reported for it means nothing —
/// import a calendar before reading it.
/// </param>
public sealed record DataQualityReportResponse(
    Guid InstrumentId,
    string Ticker,
    string Interval,
    DateOnly FromDate,
    DateOnly ToDate,
    int SessionsExpected,
    int BarsStored,
    int UnvalidatedBars,
    IReadOnlyDictionary<string, int> OpenIssues,
    IngestionSummaryResponse Ingestion,
    DataQualityScoreResponse Score,
    bool CalendarIsComplete)
{
    /// <summary>Projects a report onto the wire contract.</summary>
    /// <param name="report">The report to project.</param>
    /// <returns>The response representation.</returns>
    public static DataQualityReportResponse From(DataQualityReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        return new DataQualityReportResponse(
            report.InstrumentId.Value,
            report.Ticker.Value,
            report.Interval.ToString(),
            report.From,
            report.To,
            report.SessionsExpected,
            report.BarsStored,
            report.UnvalidatedBars,
            report.OpenIssues.ToDictionary(
                entry => entry.Key.ToString(),
                entry => entry.Value,
                StringComparer.Ordinal),
            new IngestionSummaryResponse(
                report.Ingestion.Runs,
                report.Ingestion.Succeeded,
                report.Ingestion.Failed,
                report.Ingestion.Skipped,
                report.Ingestion.BarsFetched,
                report.Ingestion.BarsAccepted,
                report.Ingestion.BarsRejected),
            new DataQualityScoreResponse(
                report.Score.Completeness,
                report.Score.Consistency,
                report.Score.Validity,
                report.Score.SourceReliability,
                report.Score.Overall),
            report.CalendarIsComplete);
    }
}

/// <summary>
/// What the ingestion runs over a window did, in aggregate.
/// </summary>
/// <param name="Runs">Runs recorded.</param>
/// <param name="Succeeded">Runs that completed.</param>
/// <param name="Failed">Runs that could not read the source.</param>
/// <param name="Skipped">Runs that had nothing to ask for.</param>
/// <param name="BarsFetched">Rows the sources returned.</param>
/// <param name="BarsAccepted">Rows that passed validation.</param>
/// <param name="BarsRejected">Rows validation refused.</param>
public sealed record IngestionSummaryResponse(
    int Runs,
    int Succeeded,
    int Failed,
    int Skipped,
    int BarsFetched,
    int BarsAccepted,
    int BarsRejected);

/// <summary>
/// The open findings for one series.
/// </summary>
/// <param name="InstrumentId">The instrument.</param>
/// <param name="Interval">The resolution, by name.</param>
/// <param name="Count">How many findings are in this response.</param>
/// <param name="Results">The findings, newest session first.</param>
public sealed record DataQualityIssuesResponse(
    Guid InstrumentId,
    string Interval,
    int Count,
    IReadOnlyList<DataQualityIssueResponse> Results);
