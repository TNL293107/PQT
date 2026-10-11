using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using PersonalQuant.Application.Abstractions;
using PersonalQuant.Application.Datasets;
using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.Research;

namespace PersonalQuant.Application.Research;

/// <summary>Reads and stores research jobs.</summary>
public interface IResearchJobRepository
{
    /// <summary>Stages a new job.</summary>
    /// <param name="job">The job.</param>
    void Add(ResearchJob job);

    /// <summary>Finds a job.</summary>
    /// <param name="id">The job's identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The job, or <see langword="null"/>.</returns>
    Task<ResearchJob?> FindAsync(ResearchJobId id, CancellationToken cancellationToken = default);

    /// <summary>Lists the most recently submitted jobs, newest first.</summary>
    /// <param name="limit">How many.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The jobs.</returns>
    Task<IReadOnlyList<ResearchJob>> ListRecentAsync(
        int limit,
        CancellationToken cancellationToken = default);
}

/// <summary>A request to run the momentum vertical slice over a dataset build.</summary>
/// <param name="DatasetId">The dataset.</param>
/// <param name="Version">The build, or <see langword="null"/> for the latest when the job runs.</param>
/// <param name="Quantiles">Buckets per cross-section.</param>
/// <param name="RebalanceEvery">Sessions between rebalances.</param>
/// <param name="Experiment">
/// The experiment the run is counted under; the job kind when <see langword="null"/>.
/// </param>
/// <param name="Hypothesis">What a new experiment sets out to test.</param>
public sealed record MomentumSliceRequest(
    string DatasetId,
    int? Version = null,
    int Quantiles = 10,
    int RebalanceEvery = 21,
    string? Experiment = null,
    string? Hypothesis = null);

/// <summary>What became of a submission.</summary>
/// <param name="Job">The queued job, or <see langword="null"/> when refused.</param>
/// <param name="Refusal">Why it was refused, or <see langword="null"/>.</param>
public sealed record ResearchJobSubmission(ResearchJob? Job, string? Refusal)
{
    /// <summary>Gets a value indicating whether a job was queued.</summary>
    public bool Queued => Job is not null;
}

/// <summary>Queues research work for the quant worker and reports on it.</summary>
public interface IResearchJobService
{
    /// <summary>Queues a run of the momentum vertical slice.</summary>
    /// <param name="request">What to run it over.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The queued job, or why it was refused.</returns>
    Task<ResearchJobSubmission> SubmitMomentumSliceAsync(
        MomentumSliceRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Queues the re-execution of a recorded run, to diff its metrics.</summary>
    /// <param name="runId">The run to reproduce.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The queued job, or why it was refused.</returns>
    Task<ResearchJobSubmission> SubmitReproductionAsync(
        Guid runId,
        CancellationToken cancellationToken = default);

    /// <summary>Finds a job.</summary>
    /// <param name="id">The job's identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The job, or <see langword="null"/>.</returns>
    Task<ResearchJob?> FindAsync(ResearchJobId id, CancellationToken cancellationToken = default);

    /// <summary>Finds a recorded run with its metrics.</summary>
    /// <param name="runId">The run's identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The run, or <see langword="null"/>.</returns>
    Task<ResearchRunView?> FindRunAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>Lists recorded runs, newest first.</summary>
    /// <param name="experimentName">Only this experiment's runs, or all.</param>
    /// <param name="limit">How many.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The runs.</returns>
    Task<IReadOnlyList<ResearchRunView>> ListRunsAsync(
        string? experimentName,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>Lists recent jobs, newest first.</summary>
    /// <param name="limit">How many.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The jobs.</returns>
    Task<IReadOnlyList<ResearchJob>> ListRecentAsync(
        int limit,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Queues research jobs, refusing the ones the worker could only fail.
/// </summary>
/// <remarks>
/// <para>
/// The worker validates every payload again before it runs one — the table is
/// writable by the research role, and a row is not trustworthy for having
/// passed through here. What this layer adds is an answer at the moment of
/// asking: a job over a dataset that was never exported is refused now, with
/// the reason, rather than queued to fail minutes later in another process.
/// </para>
/// <para>
/// The payload is written in snake_case, which is what the Python handler
/// reads; the job kinds are the handler names.
/// </para>
/// </remarks>
internal sealed partial class ResearchJobService(
    IResearchJobRepository jobs,
    IExperimentRepository experiments,
    IResearchRunRepository runs,
    IDatasetStore datasets,
    IUnitOfWork unitOfWork,
    IClock clock) : IResearchJobService
{
    /// <summary>The kind the worker runs the momentum slice under.</summary>
    public const string MomentumSliceKind = "momentum_slice";

    /// <summary>The kind the worker re-executes a recorded run under.</summary>
    public const string ReproduceRunKind = "reproduce_run";

    private const int MaxQuantiles = 100;

    private const int MaxRebalanceSessions = 252;

    private static readonly JsonSerializerOptions PayloadJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <inheritdoc />
    public async Task<ResearchJobSubmission> SubmitMomentumSliceAsync(
        MomentumSliceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrEmpty(request.DatasetId) || !DatasetIdPattern().IsMatch(request.DatasetId))
        {
            return Refuse($"'{request.DatasetId}' is not a dataset id.");
        }

        if (request.Quantiles is < 1 or > MaxQuantiles)
        {
            return Refuse($"Quantiles must be between 1 and {MaxQuantiles}, not {request.Quantiles}.");
        }

        if (request.RebalanceEvery is < 1 or > MaxRebalanceSessions)
        {
            return Refuse(
                $"The rebalance interval must be between 1 and {MaxRebalanceSessions} sessions, "
                    + $"not {request.RebalanceEvery}.");
        }

        var versions = await datasets.ListVersionsAsync(request.DatasetId, cancellationToken)
            .ConfigureAwait(false);

        if (versions.Count == 0)
        {
            return Refuse($"Dataset {request.DatasetId} has no build. Export it first.");
        }

        if (request.Version is { } version && !versions.Contains(version))
        {
            return Refuse(
                $"Dataset {request.DatasetId} has no build v{version.ToString(CultureInfo.InvariantCulture)}; "
                    + $"it holds {string.Join(", ", versions.Select(held => "v" + held.ToString(CultureInfo.InvariantCulture)))}.");
        }

        var name = request.Experiment ?? MomentumSliceKind;
        var experiment = await experiments.FindByNameAsync(name, cancellationToken).ConfigureAwait(false);

        if (experiment is null)
        {
            try
            {
                experiment = Experiment.Define(name, request.Hypothesis, clock.UtcNow);
            }
            catch (DomainValidationException exception)
            {
                return Refuse(exception.Message);
            }

            experiments.Add(experiment);
        }
        else if (request.Hypothesis is { } stated
            && !string.Equals(stated.Trim(), experiment.Hypothesis, StringComparison.Ordinal))
        {
            // A hypothesis is what the experiment's runs were counted against.
            // Changing it under them would make the trial count answer a
            // question nobody asked; a new question is a new experiment.
            return Refuse(
                $"Experiment '{name}' already states its hypothesis; a different one is a new "
                    + "experiment. Name one with --experiment.");
        }

        var payload = JsonSerializer.Serialize(
            new MomentumSlicePayload(
                request.DatasetId,
                request.Version,
                request.Quantiles,
                request.RebalanceEvery,
                experiment.Name),
            PayloadJson);
        var job = ResearchJob.Submit(MomentumSliceKind, payload, clock.UtcNow);

        jobs.Add(job);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new ResearchJobSubmission(job, null);
    }

    /// <inheritdoc />
    public async Task<ResearchJobSubmission> SubmitReproductionAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        var recorded = await runs.FindAsync(runId, cancellationToken).ConfigureAwait(false);

        if (recorded is null)
        {
            return Refuse($"No research run {runId} is recorded.");
        }

        if (recorded.Run.Status != "succeeded")
        {
            return Refuse($"Run {runId} failed; there are no metrics to reproduce.");
        }

        var versions = await datasets.ListVersionsAsync(recorded.Run.DatasetId, cancellationToken)
            .ConfigureAwait(false);

        if (!versions.Contains(recorded.Run.DatasetVersion))
        {
            // The reproduction would fail for want of its input; the input is
            // what makes it a reproduction at all.
            return Refuse(
                $"Run {runId} read {recorded.Run.DatasetId} v{recorded.Run.DatasetVersion}, which is no "
                    + "longer held. A run cannot be reproduced without the dataset version it read.");
        }

        var payload = JsonSerializer.Serialize(new ReproducePayload(runId), PayloadJson);
        var job = ResearchJob.Submit(ReproduceRunKind, payload, clock.UtcNow);

        jobs.Add(job);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new ResearchJobSubmission(job, null);
    }

    /// <inheritdoc />
    public Task<ResearchRunView?> FindRunAsync(Guid runId, CancellationToken cancellationToken = default) =>
        runs.FindAsync(runId, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<ResearchRunView>> ListRunsAsync(
        string? experimentName,
        int limit,
        CancellationToken cancellationToken = default) =>
        runs.ListRecentAsync(experimentName, Math.Clamp(limit, 1, 500), cancellationToken);

    /// <inheritdoc />
    public Task<ResearchJob?> FindAsync(ResearchJobId id, CancellationToken cancellationToken = default) =>
        jobs.FindAsync(id, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<ResearchJob>> ListRecentAsync(
        int limit,
        CancellationToken cancellationToken = default) =>
        jobs.ListRecentAsync(Math.Clamp(limit, 1, 500), cancellationToken);

    private static ResearchJobSubmission Refuse(string reason) => new(null, reason);

    // What the Python handlers read, in snake_case; the request type carries
    // the hypothesis too, which belongs to the experiment, not the job.
    private sealed record MomentumSlicePayload(
        string DatasetId,
        int? Version,
        int Quantiles,
        int RebalanceEvery,
        string Experiment);

    private sealed record ReproducePayload(Guid RunId);

    // What the exporter issues: the first sixteen hex digits of a SHA-256.
    [GeneratedRegex("^[0-9a-f]{16}$", RegexOptions.CultureInvariant)]
    private static partial Regex DatasetIdPattern();
}
