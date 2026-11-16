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

/// <summary>A request to evaluate one feature on a labelled dataset build, as one trial (ADR-036).</summary>
/// <param name="DatasetId">The labelled dataset.</param>
/// <param name="Version">The build, or <see langword="null"/> for the latest held when queued.</param>
/// <param name="Feature">A feature the dataset's request names.</param>
/// <param name="TransformVersion">The cross-sectional transform: <c>raw</c> or <c>wz-1</c> (ADR-035).</param>
/// <param name="Quantiles">Buckets for the spread and the top quantile.</param>
/// <param name="DecayHorizons">Sessions ahead at which the mean rank IC is measured.</param>
/// <param name="Experiment">The experiment the trial is counted under; the job kind when <see langword="null"/>.</param>
/// <param name="Hypothesis">What a new experiment sets out to test.</param>
public sealed record FactorEvaluationRequest(
    string DatasetId,
    int? Version = null,
    string Feature = "",
    string TransformVersion = "",
    int Quantiles = 5,
    IReadOnlyList<int>? DecayHorizons = null,
    string? Experiment = null,
    string? Hypothesis = null)
{
    /// <inheritdoc />
    public bool Equals(FactorEvaluationRequest? other) =>
        other is not null
        && DatasetId == other.DatasetId
        && Version == other.Version
        && Feature == other.Feature
        && TransformVersion == other.TransformVersion
        && Quantiles == other.Quantiles
        && (DecayHorizons ?? []).SequenceEqual(other.DecayHorizons ?? [])
        && Experiment == other.Experiment
        && Hypothesis == other.Hypothesis;

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(DatasetId, Version, Feature, TransformVersion, Quantiles, Experiment, Hypothesis);
}

/// <summary>A request to average the pairwise feature correlations of a labelled dataset build (ADR-036 §4).</summary>
/// <param name="DatasetId">The labelled dataset.</param>
/// <param name="Version">The build, or <see langword="null"/> for the latest held when queued.</param>
/// <param name="TransformVersion">The cross-sectional transform applied to each feature first.</param>
/// <param name="Experiment">
/// The evaluation experiment; the worker records the run under <c>&lt;experiment&gt;-correlation</c>.
/// The job kind when <see langword="null"/>.
/// </param>
/// <param name="Hypothesis">What a new experiment sets out to test.</param>
public sealed record FactorCorrelationRequest(
    string DatasetId,
    int? Version = null,
    string TransformVersion = "",
    string? Experiment = null,
    string? Hypothesis = null);

/// <summary>A request to calibrate one feature's quantiles into probabilities and expected value (ADR-037).</summary>
/// <remarks>
/// The five costs have no default: a fee left out is refused at compile time,
/// never read as zero (ADR-037 §1, "a declared parameter of the run").
/// </remarks>
/// <param name="DatasetId">The labelled dataset.</param>
/// <param name="BuyFee">The buy fee, a fraction of notional.</param>
/// <param name="SellFee">The sell fee, a fraction of notional.</param>
/// <param name="SellTax">The sell tax, a fraction of notional.</param>
/// <param name="Spread">The spread, a fraction of notional.</param>
/// <param name="Slippage">Slippage on each leg, a fraction of notional.</param>
/// <param name="Version">The build, or <see langword="null"/> for the latest held when queued.</param>
/// <param name="Feature">The catalogue name of the feature.</param>
/// <param name="TransformVersion">The cross-sectional transform applied first.</param>
/// <param name="Quantiles">Buckets the feature is calibrated in.</param>
/// <param name="CalibrationVersion">The calibration rules; ADR-037 is <c>cal-1</c>.</param>
/// <param name="CostModelVersion">The name the declared costs are recorded under.</param>
/// <param name="Experiment">
/// The evaluation experiment; the worker records the run under <c>&lt;experiment&gt;-calibration</c>,
/// which is where a hypothesis is staged. The job kind when <see langword="null"/>.
/// </param>
/// <param name="Hypothesis">What a new calibration experiment sets out to test.</param>
public sealed record CalibrationRequest(
    string DatasetId,
    decimal BuyFee,
    decimal SellFee,
    decimal SellTax,
    decimal Spread,
    decimal Slippage,
    int? Version = null,
    string Feature = "",
    string TransformVersion = "",
    int Quantiles = 5,
    string CalibrationVersion = "cal-1",
    string CostModelVersion = "",
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

    /// <summary>Queues the evaluation of one feature on a labelled dataset, as one trial.</summary>
    /// <param name="request">What to evaluate.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The queued job, or why it was refused.</returns>
    Task<ResearchJobSubmission> SubmitFactorEvaluationAsync(
        FactorEvaluationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Queues the pairwise feature correlation of a labelled dataset.</summary>
    /// <param name="request">What to correlate.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The queued job, or why it was refused.</returns>
    Task<ResearchJobSubmission> SubmitFactorCorrelationAsync(
        FactorCorrelationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Queues the calibration of one feature on a labelled dataset (ADR-037).</summary>
    /// <param name="request">What to calibrate, and the declared costs.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The queued job, or why it was refused.</returns>
    Task<ResearchJobSubmission> SubmitCalibrationAsync(
        CalibrationRequest request,
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

    /// <summary>The kind the worker evaluates one feature under (ADR-036).</summary>
    public const string FactorEvaluationKind = "factor_evaluation";

    /// <summary>The kind the worker correlates every pair of features under (ADR-036 §4).</summary>
    public const string FactorCorrelationKind = "factor_correlation";

    /// <summary>The kind the worker calibrates one feature under (ADR-037).</summary>
    public const string CalibrationKind = "calibration";

    /// <summary>
    /// What the worker appends to a calibration job's experiment to name the one it
    /// records the run under, unless the name already ends with it (ADR-037 §5,
    /// <c>handlers.py</c> <c>recorded_under</c>).
    /// </summary>
    public const string CalibrationExperimentSuffix = "-calibration";

    /// <summary>The calibration rules the worker computes.</summary>
    public static readonly IReadOnlyList<string> CalibrationVersions = ["cal-1"];

    /// <summary>The transforms the worker knows, by the version a run records (ADR-035).</summary>
    public static readonly IReadOnlyList<string> TransformVersions = ["raw", "wz-1"];

    private const int MaxDecayHorizon = 252;

    private const int MaxFeatureNameLength = 64;

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
    public async Task<ResearchJobSubmission> SubmitFactorEvaluationAsync(
        FactorEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Feature)
            || request.Feature.Length > MaxFeatureNameLength
            || !FeatureNamePattern().IsMatch(request.Feature))
        {
            return Refuse($"'{request.Feature}' is not a feature name.");
        }

        if (request.Quantiles is < 1 or > MaxQuantiles)
        {
            return Refuse($"Quantiles must be between 1 and {MaxQuantiles}, not {request.Quantiles}.");
        }

        if (request.DecayHorizons is not { Count: > 0 } horizons || horizons.Any(horizon => horizon is < 1 or > MaxDecayHorizon))
        {
            return Refuse($"Decay horizons must be one or more sessions, each between 1 and {MaxDecayHorizon}.");
        }

        var prepared = await PrepareLabelledAsync(
                request.DatasetId, request.Version, request.TransformVersion, request.Experiment, request.Hypothesis,
                FactorEvaluationKind, recordedSuffix: null, cancellationToken)
            .ConfigureAwait(false);

        if (prepared.Refusal is { } refusal)
        {
            return Refuse(refusal);
        }

        var payload = JsonSerializer.Serialize(
            new FactorEvaluationPayload(
                request.DatasetId,
                prepared.Version,
                request.Feature,
                request.TransformVersion,
                request.Quantiles,
                [.. horizons],
                prepared.Experiment),
            PayloadJson);

        return await QueueAsync(FactorEvaluationKind, payload, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ResearchJobSubmission> SubmitFactorCorrelationAsync(
        FactorCorrelationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var prepared = await PrepareLabelledAsync(
                request.DatasetId, request.Version, request.TransformVersion, request.Experiment, request.Hypothesis,
                FactorCorrelationKind, recordedSuffix: null, cancellationToken)
            .ConfigureAwait(false);

        if (prepared.Refusal is { } refusal)
        {
            return Refuse(refusal);
        }

        var payload = JsonSerializer.Serialize(
            new FactorCorrelationPayload(request.DatasetId, prepared.Version, request.TransformVersion, prepared.Experiment),
            PayloadJson);

        return await QueueAsync(FactorCorrelationKind, payload, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ResearchJobSubmission> SubmitCalibrationAsync(
        CalibrationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Feature)
            || request.Feature.Length > MaxFeatureNameLength
            || !FeatureNamePattern().IsMatch(request.Feature))
        {
            return Refuse($"'{request.Feature}' is not a feature name.");
        }

        if (request.Quantiles is < 1 or > MaxQuantiles)
        {
            return Refuse($"Quantiles must be between 1 and {MaxQuantiles}, not {request.Quantiles}.");
        }

        if (!CalibrationVersions.Contains(request.CalibrationVersion))
        {
            return Refuse(
                $"Calibration version '{request.CalibrationVersion}' is not one the worker computes "
                    + $"({string.Join(", ", CalibrationVersions)}).");
        }

        if (string.IsNullOrWhiteSpace(request.CostModelVersion))
        {
            return Refuse("The cost model must be named.");
        }

        // The worker's own bounds: a fraction of notional, from 0 to under 1.
        foreach (var (field, fee) in new[]
            {
                ("buy_fee", request.BuyFee), ("sell_fee", request.SellFee), ("sell_tax", request.SellTax),
                ("spread", request.Spread), ("slippage", request.Slippage),
            })
        {
            if (fee is < 0m or >= 1m)
            {
                return Refuse($"{field} must be a fraction of notional from 0 to under 1, not {fee.ToString(CultureInfo.InvariantCulture)}.");
            }
        }

        var prepared = await PrepareLabelledAsync(
                request.DatasetId, request.Version, request.TransformVersion, request.Experiment, request.Hypothesis,
                CalibrationKind, recordedSuffix: CalibrationExperimentSuffix, cancellationToken)
            .ConfigureAwait(false);

        if (prepared.Refusal is { } refusal)
        {
            return Refuse(refusal);
        }

        var payload = JsonSerializer.Serialize(
            new CalibrationPayload(
                request.DatasetId,
                prepared.Version,
                request.Feature,
                request.TransformVersion,
                request.Quantiles,
                request.CalibrationVersion,
                request.CostModelVersion,
                request.BuyFee,
                request.SellFee,
                request.SellTax,
                request.Spread,
                request.Slippage,
                prepared.Experiment),
            PayloadJson);

        return await QueueAsync(CalibrationKind, payload, cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// Checks what a labelled-dataset job shares: the dataset and its build,
    /// the transform and the experiment. The worker needs the build named, so
    /// "latest" is resolved here, to the newest build held when queued.
    /// Nothing is saved: a new experiment is only staged, and only saved with
    /// the job. With <paramref name="recordedSuffix"/>, the experiment staged and
    /// judged is the one the worker records under; the name returned for the
    /// payload is still the one typed.
    /// </summary>
    private async Task<(int Version, string Experiment, string? Refusal)> PrepareLabelledAsync(
        string datasetId,
        int? version,
        string transformVersion,
        string? experimentName,
        string? hypothesis,
        string kind,
        string? recordedSuffix,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(datasetId) || !DatasetIdPattern().IsMatch(datasetId))
        {
            return (0, string.Empty, $"'{datasetId}' is not a dataset id.");
        }

        if (!TransformVersions.Contains(transformVersion, StringComparer.Ordinal))
        {
            return (0, string.Empty,
                $"'{transformVersion}' is not a transform version ({string.Join(", ", TransformVersions)}).");
        }

        var versions = await datasets.ListVersionsAsync(datasetId, cancellationToken).ConfigureAwait(false);

        if (versions.Count == 0)
        {
            return (0, string.Empty, $"Dataset {datasetId} has no build. Export it first.");
        }

        if (version is { } named && !versions.Contains(named))
        {
            return (0, string.Empty,
                $"Dataset {datasetId} has no build v{named.ToString(CultureInfo.InvariantCulture)}; "
                    + $"it holds {string.Join(", ", versions.Select(held => "v" + held.ToString(CultureInfo.InvariantCulture)))}.");
        }

        var name = experimentName ?? kind;

        // The payload carries the name typed; the run is recorded under the
        // worker's name for it, so that is the experiment staged and judged.
        var recordedUnder = recordedSuffix is null || name.EndsWith(recordedSuffix, StringComparison.Ordinal)
            ? name
            : name + recordedSuffix;
        var experiment = await experiments.FindByNameAsync(recordedUnder, cancellationToken).ConfigureAwait(false);

        if (experiment is null)
        {
            try
            {
                experiment = Experiment.Define(recordedUnder, hypothesis, clock.UtcNow);
            }
            catch (DomainValidationException exception)
            {
                return (0, string.Empty, exception.Message);
            }

            experiments.Add(experiment);
        }
        else if (hypothesis is { } stated
            && !string.Equals(stated.Trim(), experiment.Hypothesis, StringComparison.Ordinal))
        {
            return (0, string.Empty,
                $"Experiment '{recordedUnder}' already states its hypothesis; a different one is a new "
                    + "experiment. Name one with --experiment.");
        }

        return (version ?? versions.Max(), name, null);
    }

    private async Task<ResearchJobSubmission> QueueAsync(string kind, string payload, CancellationToken cancellationToken)
    {
        var job = ResearchJob.Submit(kind, payload, clock.UtcNow);

        jobs.Add(job);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new ResearchJobSubmission(job, null);
    }

    // What the Python handlers read, in snake_case; the request type carries
    // the hypothesis too, which belongs to the experiment, not the job.
    private sealed record MomentumSlicePayload(
        string DatasetId,
        int? Version,
        int Quantiles,
        int RebalanceEvery,
        string Experiment);

    private sealed record ReproducePayload(Guid RunId);

    // ADR-036 §1, exactly the fields the worker's handler accepts; it refuses any other.
    private sealed record FactorEvaluationPayload(
        string DatasetId,
        int DatasetVersion,
        string Feature,
        string TransformVersion,
        int Quantiles,
        IReadOnlyList<int> DecayHorizons,
        string Experiment);

    // Fees as decimals, so the JSON number is the one typed, never a double's neighbour.
    private sealed record CalibrationPayload(
        string DatasetId,
        int DatasetVersion,
        string Feature,
        string TransformVersion,
        int Quantiles,
        string CalibrationVersion,
        string CostModelVersion,
        decimal BuyFee,
        decimal SellFee,
        decimal SellTax,
        decimal Spread,
        decimal Slippage,
        string Experiment);

    private sealed record FactorCorrelationPayload(
        string DatasetId,
        int DatasetVersion,
        string TransformVersion,
        string Experiment);

    // A catalogue name: lower case, digits and underscores, as personal_quant.factors names them.
    [GeneratedRegex("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex FeatureNamePattern();

    // What the exporter issues: the first sixteen hex digits of a SHA-256.
    [GeneratedRegex("^[0-9a-f]{16}$", RegexOptions.CultureInvariant)]
    private static partial Regex DatasetIdPattern();
}
