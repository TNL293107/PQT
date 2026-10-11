using Microsoft.EntityFrameworkCore;
using PersonalQuant.Application.Research;
using PersonalQuant.Domain.Research;

namespace PersonalQuant.Infrastructure.Persistence.Repositories;

/// <summary>Reads and stages experiments through EF Core.</summary>
/// <param name="dbContext">The context.</param>
internal sealed class ExperimentRepository(PersonalQuantDbContext dbContext) : IExperimentRepository
{
    /// <inheritdoc />
    public Task<Experiment?> FindByNameAsync(string name, CancellationToken cancellationToken = default) =>
        dbContext.Experiments.FirstOrDefaultAsync(experiment => experiment.Name == name, cancellationToken);

    /// <inheritdoc />
    public void Add(Experiment experiment) => dbContext.Experiments.Add(experiment);
}

/// <summary>Reads recorded runs through EF Core.</summary>
/// <remarks>Untracked throughout: the worker writes runs, and nothing here may change one.</remarks>
/// <param name="dbContext">The context.</param>
internal sealed class ResearchRunRepository(PersonalQuantDbContext dbContext) : IResearchRunRepository
{
    /// <inheritdoc />
    public async Task<ResearchRunView?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var found = await Runs()
            .Where(run => run.Id == id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var named = await NameAsync(found, cancellationToken).ConfigureAwait(false);

        return named.Count == 0 ? null : named[0];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ResearchRunView>> ListRecentAsync(
        string? experimentName,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var query = Runs();

        if (experimentName is not null)
        {
            var experiment = await dbContext.Experiments
                .AsNoTracking()
                .Where(candidate => candidate.Name == experimentName)
                .Select(candidate => (ExperimentId?)candidate.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (experiment is not { } id)
            {
                return [];
            }

            query = query.Where(run => run.ExperimentId == id);
        }

        var found = await query
            .OrderByDescending(run => run.FinishedAtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return await NameAsync(found, cancellationToken).ConfigureAwait(false);
    }

    private IQueryable<ResearchRun> Runs() =>
        dbContext.ResearchRuns
            .AsNoTracking()
            .AsSplitQuery()
            .Include(run => run.Metrics)
            .Include(run => run.Artifacts);

    private async Task<IReadOnlyList<ResearchRunView>> NameAsync(
        IReadOnlyList<ResearchRun> found,
        CancellationToken cancellationToken)
    {
        var ids = found.Select(run => run.ExperimentId).Distinct().ToList();

        var names = await dbContext.Experiments
            .AsNoTracking()
            .Where(experiment => ids.Contains(experiment.Id))
            .ToDictionaryAsync(experiment => experiment.Id, experiment => experiment.Name, cancellationToken)
            .ConfigureAwait(false);

        return [.. found.Select(run => new ResearchRunView(run, names[run.ExperimentId]))];
    }
}
