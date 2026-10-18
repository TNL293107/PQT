using PersonalQuant.Domain.Fundamentals;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Application.Fundamentals;

/// <summary>
/// Stores and reads disclosed financial reports and the values they state.
/// </summary>
/// <remarks>
/// Append-only: reports and lines are added, and a line's only change is the
/// closing of its observation window by <see cref="StatementLine.Restate"/>.
/// Nothing is ever removed.
/// </remarks>
public interface IFundamentalsRepository
{
    /// <summary>Finds a report by its source's key.</summary>
    /// <param name="source">The disclosure system.</param>
    /// <param name="sourceReference">The source's key for the filing.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The report, or null when it has not been recorded.</returns>
    Task<FinancialReport?> FindReportAsync(
        SourceCode source,
        string sourceReference,
        CancellationToken cancellationToken = default);

    /// <summary>Lists every report recorded for a set of instruments.</summary>
    /// <param name="instrumentIds">The instruments.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The reports, in the order they became known.</returns>
    Task<IReadOnlyList<FinancialReport>> ListReportsAsync(
        IReadOnlyCollection<InstrumentId> instrumentIds,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the current reading of every line of a report, tracked for restatement.</summary>
    /// <param name="reportId">The report.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The current lines.</returns>
    Task<IReadOnlyList<StatementLine>> ListCurrentLinesForUpdateAsync(
        FinancialReportId reportId,
        CancellationToken cancellationToken = default);

    /// <summary>Lists every reading of every line of a set of reports, superseded ones included.</summary>
    /// <param name="reportIds">The reports.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The lines.</returns>
    Task<IReadOnlyList<StatementLine>> ListLinesAsync(
        IReadOnlyCollection<FinancialReportId> reportIds,
        CancellationToken cancellationToken = default);

    /// <summary>Summarises the reports recorded for one instrument, for the coverage registry.</summary>
    /// <param name="instrumentId">The instrument.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The summary; zero reports when none is recorded.</returns>
    Task<FundamentalsSummary> SummariseAsync(InstrumentId instrumentId, CancellationToken cancellationToken = default);

    /// <summary>Gets whether a filing's payload with this checksum is already retained.</summary>
    /// <param name="source">The source.</param>
    /// <param name="sourceReference">The source's key for the filing.</param>
    /// <param name="checksum">The payload's checksum.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    Task<bool> HoldsPayloadAsync(
        SourceCode source,
        string sourceReference,
        string checksum,
        CancellationToken cancellationToken = default);

    /// <summary>Stages a raw payload for saving.</summary>
    /// <param name="payload">The payload.</param>
    void Add(RawFundamentalsPayload payload);

    /// <summary>Stages a report for saving.</summary>
    /// <param name="report">The report.</param>
    void Add(FinancialReport report);

    /// <summary>Stages a line for saving.</summary>
    /// <param name="line">The line.</param>
    void Add(StatementLine line);
}

/// <summary>What is recorded for one instrument's fundamentals.</summary>
/// <param name="Reports">Reports recorded.</param>
/// <param name="WithoutValues">Of those, reports disclosed with an empty form.</param>
/// <param name="FirstPeriod">The earliest quarter a report closes on, when any.</param>
/// <param name="LastPeriod">The latest quarter a report closes on, when any.</param>
/// <param name="LastDisclosedOn">The latest disclosure date, when any.</param>
/// <param name="Sources">The sources reports came from, ordered.</param>
public sealed record FundamentalsSummary(
    int Reports,
    int WithoutValues,
    FiscalQuarter? FirstPeriod,
    FiscalQuarter? LastPeriod,
    DateOnly? LastDisclosedOn,
    IReadOnlyList<SourceCode> Sources);
