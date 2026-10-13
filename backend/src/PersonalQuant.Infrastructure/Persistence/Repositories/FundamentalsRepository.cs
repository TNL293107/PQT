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
