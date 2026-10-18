using Microsoft.EntityFrameworkCore;
using PersonalQuant.Application.Fundamentals;
using PersonalQuant.Domain.Fundamentals;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IFundamentalsRepository"/>.
/// </summary>
/// <param name="dbContext">The unit of work to read and stage through.</param>
internal sealed class FundamentalsRepository(PersonalQuantDbContext dbContext) : IFundamentalsRepository
{
    /// <inheritdoc />
    public async Task<FinancialReport?> FindReportAsync(
        SourceCode source,
        string sourceReference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceReference);

        var reference = sourceReference.Trim();

        return await dbContext.FinancialReports
            .FirstOrDefaultAsync(
                report => report.Source == source && report.SourceReference == reference,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FinancialReport>> ListReportsAsync(
        IReadOnlyCollection<InstrumentId> instrumentIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instrumentIds);

        var ids = instrumentIds.Distinct().ToList();

        if (ids.Count == 0)
        {
            return [];
        }

        return await dbContext.FinancialReports
            .AsNoTracking()
            .Where(report => ids.Contains(report.InstrumentId))
            .OrderBy(report => report.KnownFromUtc)
            .ThenBy(report => report.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StatementLine>> ListCurrentLinesForUpdateAsync(
        FinancialReportId reportId,
        CancellationToken cancellationToken = default) =>
        await dbContext.StatementLines
            .Where(line => line.ReportId == reportId && line.ObservedToUtc == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<StatementLine>> ListLinesAsync(
        IReadOnlyCollection<FinancialReportId> reportIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reportIds);

        var ids = reportIds.Distinct().ToList();

        if (ids.Count == 0)
        {
            return [];
        }

        return await dbContext.StatementLines
            .AsNoTracking()
            .Where(line => ids.Contains(line.ReportId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<FundamentalsSummary> SummariseAsync(InstrumentId instrumentId, CancellationToken cancellationToken = default)
    {
        // A few hundred rows at most per issuer; read the columns and fold here
        // rather than teach EF to group over a complex property.
        var reports = await dbContext.FinancialReports
            .AsNoTracking()
            .Where(report => report.InstrumentId == instrumentId)
            .Select(report => new { report.Period, report.ValuesFiled, report.DisclosedOn, report.Source })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (reports.Count == 0)
        {
            return new FundamentalsSummary(0, 0, null, null, null, []);
        }

        return new FundamentalsSummary(
            reports.Count,
            reports.Count(report => !report.ValuesFiled),
            reports.Min(report => report.Period),
            reports.Max(report => report.Period),
            reports.Max(report => report.DisclosedOn),
            [.. reports.Select(report => report.Source).Distinct().OrderBy(source => source.Value, StringComparer.Ordinal)]);
    }

    /// <inheritdoc />
    public Task<bool> HoldsPayloadAsync(
        SourceCode source,
        string sourceReference,
        string checksum,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceReference);

        var reference = sourceReference.Trim();

        return dbContext.RawFundamentalsPayloads.AnyAsync(
            payload => payload.Source == source && payload.SourceReference == reference && payload.Checksum == checksum,
            cancellationToken);
    }

    /// <inheritdoc />
    public void Add(RawFundamentalsPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        dbContext.RawFundamentalsPayloads.Add(payload);
    }

    /// <inheritdoc />
    public void Add(FinancialReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        dbContext.FinancialReports.Add(report);
    }

    /// <inheritdoc />
    public void Add(StatementLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        dbContext.StatementLines.Add(line);
    }
}
