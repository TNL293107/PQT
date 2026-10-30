using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using PersonalQuant.Api.Contracts;
using PersonalQuant.Application.Research;

namespace PersonalQuant.Api.Endpoints;

/// <summary>
/// Recorded research runs, for the console's RUNS view (sprint 11.2).
/// </summary>
/// <remarks>
/// <para>
/// Read-only: runs are written by the quant worker, and nothing on this route
/// queues, changes or deletes one. It reads the same <see cref="IResearchJobService"/>
/// the CLI's <c>pqt research runs</c> does, so the two cannot disagree.
/// </para>
/// <para>
/// Every run carries its experiment's trial count beside it: a metric is one of
/// that many configurations tried, and reading it without the count overstates
/// it. Runs come newest first, never ranked by a metric.
/// </para>
/// </remarks>
internal static class ResearchEndpoints
{
    private const string Route = "/research/runs";
    private const string OpenApiTag = "Research";
    private const int DefaultLimit = 50;
    private const int MaxLimit = 500;

    /// <summary>Maps the research endpoints.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The same builder, to allow chaining.</returns>
    public static IEndpointRouteBuilder MapResearchEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(Route, ListRunsAsync)
            .WithTags(OpenApiTag)
            .WithName("ListResearchRuns")
            .WithSummary("Lists recorded research runs, newest first, each with its experiment's trial count.");

        return endpoints;
    }

    /// <summary><c>GET /research/runs?experiment=&amp;limit=</c>.</summary>
    /// <remarks>An experiment with no runs is a 200 with none, not a 404.</remarks>
    private static async Task<Results<Ok<ResearchRunsResponse>, ProblemHttpResult>> ListRunsAsync(
        string? experiment,
        int? limit,
        IResearchJobService research,
        CancellationToken cancellationToken)
    {
        if (limit is < 1 or > MaxLimit)
        {
            return TypedResults.Problem(
                detail: $"The limit must be between 1 and {MaxLimit}.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid limit.");
        }

        var runs = await research
            .ListRunsAsync(string.IsNullOrWhiteSpace(experiment) ? null : experiment.Trim(), limit ?? DefaultLimit, cancellationToken)
            .ConfigureAwait(false);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var name in runs.Select(view => view.ExperimentName).Distinct(StringComparer.Ordinal))
        {
            counts[name] = await TrialCountAsync(research, name, cancellationToken).ConfigureAwait(false);
        }

        return TypedResults.Ok(ResearchRunsResponse.From(runs, counts));
    }

    /// <summary>The experiment's highest trial index: how many configurations it has tried.</summary>
    /// <remarks>
    /// Read from the experiment's own runs rather than the run's recorded
    /// <c>trial_count</c>, which was the count when that run finished. Indices
    /// are dense from 1 (the worker assigns them under a lock), so the newest
    /// <see cref="MaxLimit"/> runs hold the highest; an experiment with more
    /// runs than that still reads its true count.
    /// </remarks>
    private static async Task<int> TrialCountAsync(
        IResearchJobService research,
        string experiment,
        CancellationToken cancellationToken)
    {
        var own = await research.ListRunsAsync(experiment, MaxLimit, cancellationToken).ConfigureAwait(false);

        return own.Select(view => view.Run.TrialIndex ?? 0).DefaultIfEmpty(0).Max();
    }
}
