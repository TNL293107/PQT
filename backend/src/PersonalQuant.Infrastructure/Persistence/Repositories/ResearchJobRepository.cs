using Microsoft.EntityFrameworkCore;
using PersonalQuant.Application.Research;
using PersonalQuant.Domain.Research;

namespace PersonalQuant.Infrastructure.Persistence.Repositories;

/// <summary>
/// Reads and stages research jobs through EF Core.
/// </summary>
/// <remarks>
/// Reads are untracked. Every transition after submission is the worker's,
/// written in its own SQL, so a tracked instance here would only ever be a
/// stale copy of a row someone else is changing.
/// </remarks>
/// <param name="dbContext">The context.</param>
internal sealed class ResearchJobRepository(PersonalQuantDbContext dbContext) : IResearchJobRepository
{
    /// <inheritdoc />
    public void Add(ResearchJob job) => dbContext.ResearchJobs.Add(job);

    /// <inheritdoc />
    public Task<ResearchJob?> FindAsync(ResearchJobId id, CancellationToken cancellationToken = default) =>
        dbContext.ResearchJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(job => job.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ResearchJob>> ListRecentAsync(
        int limit,
        CancellationToken cancellationToken = default) =>
        await dbContext.ResearchJobs
            .AsNoTracking()
            .OrderByDescending(job => job.CreatedAtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}
