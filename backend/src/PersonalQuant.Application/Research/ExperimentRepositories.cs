using PersonalQuant.Domain.Research;

namespace PersonalQuant.Application.Research;

/// <summary>Reads and stores experiments.</summary>
public interface IExperimentRepository
{
    /// <summary>Finds an experiment by its name.</summary>
    /// <param name="name">The name.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The experiment, or <see langword="null"/>.</returns>
    Task<Experiment?> FindByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Stages a new experiment.</summary>
    /// <param name="experiment">The experiment.</param>
    void Add(Experiment experiment);
}

/// <summary>A run as listed, with its experiment's name and headline metric.</summary>
/// <param name="Run">The run, with its metrics.</param>
/// <param name="ExperimentName">The experiment it belongs to.</param>
public sealed record ResearchRunView(ResearchRun Run, string ExperimentName);

/// <summary>Reads the runs the quant worker recorded.</summary>
/// <remarks>Read-only: runs are written by the worker, never by the backend.</remarks>
public interface IResearchRunRepository
{
    /// <summary>Finds a run with its metrics and artifacts.</summary>
    /// <param name="id">The run's identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The run, or <see langword="null"/>.</returns>
    Task<ResearchRunView?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Lists the most recent runs, newest first.</summary>
    /// <param name="experimentName">Only this experiment's runs, or all when <see langword="null"/>.</param>
    /// <param name="limit">How many.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The runs.</returns>
    Task<IReadOnlyList<ResearchRunView>> ListRecentAsync(
        string? experimentName,
        int limit,
        CancellationToken cancellationToken = default);
}
