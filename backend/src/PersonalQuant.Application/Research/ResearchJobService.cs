using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using PersonalQuant.Application.Abstractions;
using PersonalQuant.Application.Datasets;
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
public sealed record MomentumSliceRequest(
    string DatasetId,
    int? Version = null,
    int Quantiles = 10,
    int RebalanceEvery = 21);

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

    /// <summary>Finds a job.</summary>
    /// <param name="id">The job's identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The job, or <see langword="null"/>.</returns>
    Task<ResearchJob?> FindAsync(ResearchJobId id, CancellationToken cancellationToken = default);

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
    IDatasetStore datasets,
    IUnitOfWork unitOfWork,
    IClock clock) : IResearchJobService
{
    /// <summary>The kind the worker runs the momentum slice under.</summary>
    public const string MomentumSliceKind = "momentum_slice";

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

        var payload = JsonSerializer.Serialize(request, PayloadJson);
        var job = ResearchJob.Submit(MomentumSliceKind, payload, clock.UtcNow);

        jobs.Add(job);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new ResearchJobSubmission(job, null);
    }

    /// <inheritdoc />
    public Task<ResearchJob?> FindAsync(ResearchJobId id, CancellationToken cancellationToken = default) =>
        jobs.FindAsync(id, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<ResearchJob>> ListRecentAsync(
        int limit,
        CancellationToken cancellationToken = default) =>
        jobs.ListRecentAsync(Math.Clamp(limit, 1, 500), cancellationToken);

    private static ResearchJobSubmission Refuse(string reason) => new(null, reason);

    // What the exporter issues: the first sixteen hex digits of a SHA-256.
    [GeneratedRegex("^[0-9a-f]{16}$", RegexOptions.CultureInvariant)]
    private static partial Regex DatasetIdPattern();
}
