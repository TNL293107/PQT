using PersonalQuant.Application.Fundamentals;
using PersonalQuant.Domain.Fundamentals;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.UnitTests.Fundamentals;

/// <summary>
/// Holds reports, lines and payloads in memory. Staged rows are kept at once,
/// as a saved unit of work would keep them, and lines are returned by
/// reference so a restatement closes the stored reading as EF tracking would.
/// </summary>
internal sealed class InMemoryFundamentals : IFundamentalsRepository
{
    public List<FinancialReport> Reports { get; } = [];

    public List<StatementLine> Lines { get; } = [];

    public List<RawFundamentalsPayload> Payloads { get; } = [];

    public Task<FinancialReport?> FindReportAsync(
        SourceCode source,
        string sourceReference,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Reports.Find(report => report.Source == source && report.SourceReference == sourceReference.Trim()));

    public Task<IReadOnlyList<FinancialReport>> ListReportsAsync(
        IReadOnlyCollection<InstrumentId> instrumentIds,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<FinancialReport>>(
            [.. Reports.Where(report => instrumentIds.Contains(report.InstrumentId)).OrderBy(report => report.KnownFromUtc)]);

    public Task<IReadOnlyList<StatementLine>> ListCurrentLinesForUpdateAsync(
        FinancialReportId reportId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<StatementLine>>([.. Lines.Where(line => line.ReportId == reportId && line.IsCurrent)]);

    public Task<IReadOnlyList<StatementLine>> ListLinesAsync(
        IReadOnlyCollection<FinancialReportId> reportIds,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<StatementLine>>([.. Lines.Where(line => reportIds.Contains(line.ReportId))]);

    public Task<bool> HoldsPayloadAsync(
        SourceCode source,
        string sourceReference,
        string checksum,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Payloads.Exists(payload =>
            payload.Source == source && payload.SourceReference == sourceReference && payload.Checksum == checksum));

    public void Add(RawFundamentalsPayload payload) => Payloads.Add(payload);

    public void Add(FinancialReport report) => Reports.Add(report);

    public void Add(StatementLine line) => Lines.Add(line);
}
