using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using PersonalQuant.Api.Contracts;
using PersonalQuant.Application.Coverage;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.Api.Endpoints;

/// <summary>
/// The coverage registry: what data PQT holds for an instrument, and to what
/// extent.
/// </summary>
/// <remarks>
/// <para>
/// Parses, calls and projects, and nothing else. Every state decision is made
/// by <see cref="IInstrumentCoverageService"/>, which the CLI and the console
/// reach too, so the three cannot disagree about what "not held" means
/// (ADR-026).
/// </para>
/// <para>
/// Read-only, like every endpoint here. Coverage is introspection over facts
/// already recorded; nothing on this route configures or triggers ingestion.
/// </para>
/// </remarks>
internal static class CoverageEndpoints
{
    private const string Route = "/instruments/{instrumentId:guid}/coverage";
    private const string OpenApiTag = "Coverage";

    /// <summary>
    /// Maps the coverage endpoint.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The same builder, to allow chaining.</returns>
    public static IEndpointRouteBuilder MapCoverageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(Route, GetAsync)
            .WithTags(OpenApiTag)
            .WithName("GetInstrumentCoverage")
            .WithSummary("States which datasets PQT holds for an instrument, and to what extent.");

        return endpoints;
    }

    /// <summary>
    /// <c>GET /instruments/{instrumentId}/coverage</c>.
    /// </summary>
    /// <remarks>
    /// An instrument with no data at all is a 200 with every dataset not held,
    /// not a 404. The instrument exists; that PQT holds nothing for it is the
    /// answer to the question asked.
    /// </remarks>
    private static async Task<Results<Ok<InstrumentCoverageResponse>, ProblemHttpResult>> GetAsync(
        Guid instrumentId,
        IInstrumentCoverageService coverage,
        CancellationToken cancellationToken)
    {
        var described = await coverage
            .DescribeAsync(new InstrumentId(instrumentId), cancellationToken)
            .ConfigureAwait(false);

        if (described is null)
        {
            return TypedResults.Problem(
                detail: "No instrument exists with that identifier.",
                statusCode: StatusCodes.Status404NotFound,
                title: "Instrument not found.");
        }

        return TypedResults.Ok(InstrumentCoverageResponse.From(described));
    }
}
