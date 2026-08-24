using Microsoft.EntityFrameworkCore;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IDataQualityRepository"/>.
/// </summary>
/// <param name="dbContext">The unit of work to read and stage through.</param>
internal sealed class DataQualityRepository(PersonalQuantDbContext dbContext)
    : IDataQualityRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<DataQualityIssue>> ListAsync(
        InstrumentId instrumentId,
        BarInterval interval,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default) =>
        await dbContext.DataQualityIssues
            .AsNoTracking()
            .Where(issue =>
                issue.InstrumentId == instrumentId
                && issue.Interval == interval
                && issue.SessionAtUtc >= fromUtc
                && issue.SessionAtUtc < toUtc)
            .OrderBy(issue => issue.SessionAtUtc)
            .ThenBy(issue => issue.Kind)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DataQualityIssue>> ListOpenAsync(
        InstrumentId instrumentId,
        BarInterval interval,
        int limit,
        CancellationToken cancellationToken = default) =>
        await dbContext.DataQualityIssues
            .AsNoTracking()
            .Where(issue =>
                issue.InstrumentId == instrumentId
                && issue.Interval == interval
                && issue.Status == DataQualityIssueStatus.Open)
            .OrderByDescending(issue => issue.SessionAtUtc)
            .ThenBy(issue => issue.Kind)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<DataQualityIssueKind, int>> CountOpenByKindAsync(
        InstrumentId instrumentId,
        BarInterval interval,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
    {
        // Grouped in the database. Scoring reads this on every dashboard
        // refresh, and materialising a year of findings to count them in memory
        // would make the read grow with the history rather than with the answer.
        var counts = await dbContext.DataQualityIssues
            .AsNoTracking()
            .Where(issue =>
                issue.InstrumentId == instrumentId
                && issue.Interval == interval
                && issue.Status == DataQualityIssueStatus.Open
                && issue.SessionAtUtc >= fromUtc
                && issue.SessionAtUtc < toUtc)
            .GroupBy(issue => issue.Kind)
            .Select(group => new { Kind = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return counts.ToDictionary(row => row.Kind, row => row.Count);
    }

    /// <inheritdoc />
    public Task<DataQualityIssue?> FindAsync(
        DataQualityIssueId id,
        CancellationToken cancellationToken = default) =>
        // Tracked: the caller resolves it and commits through the same unit of
        // work.
        dbContext.DataQualityIssues.FirstOrDefaultAsync(
            issue => issue.Id == id, cancellationToken);

    /// <inheritdoc />
    public void Add(DataQualityIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);

        dbContext.DataQualityIssues.Add(issue);
    }
}
