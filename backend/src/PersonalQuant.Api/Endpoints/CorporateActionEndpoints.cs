using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using PersonalQuant.Api.Contracts;
using PersonalQuant.Application.CorporateActions;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.Api.Endpoints;

/// <summary>
/// Read-only corporate action endpoints: what an issuer did, and what it did to
/// the series.
/// </summary>
/// <remarks>
/// <para>
/// The explanation behind an adjusted price. A chart showing a security halving
/// overnight is answered here — a split, a dividend, a rights issue — and a
/// series that has been rescaled says by how much and from which action.
/// </para>
/// <para>
/// No write surface, and no endpoint that triggers a recompute. Actions arrive
/// through the import pipeline, factors follow it automatically, and both are
/// driven by the host.
/// </para>
/// </remarks>
internal static class CorporateActionEndpoints
{
    private const string RouteGroup = "/instruments/{instrumentId:guid}/corporate-actions";
    private const string OpenApiTag = "Corporate actions";

    /// <summary>
    /// Maps the corporate action endpoints.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The same builder, to allow chaining.</returns>
    public static IEndpointRouteBuilder MapCorporateActionEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(RouteGroup, ListAsync)
            .WithTags(OpenApiTag)
            .WithName("ListInstrumentCorporateActions")
            .WithSummary("Lists what an issuer did and what it did to the series.");

        return endpoints;
    }

    /// <summary>
    /// <c>GET /instruments/{instrumentId}/corporate-actions</c>.
    /// </summary>
    /// <remarks>
    /// Cancelled actions are included. An action that was announced and called
    /// off is a fact about the issuer, and a list that hid it would leave a
    /// factor's disappearance unexplained.
    /// </remarks>
    private static async Task<Results<Ok<CorporateActionsResponse>, ProblemHttpResult>> ListAsync(
        Guid instrumentId,
        IInstrumentCatalog catalog,
        ICorporateActionRepository actions,
        CancellationToken cancellationToken)
    {
        var id = new InstrumentId(instrumentId);
        var instrument = await catalog.FindDetailAsync(id, cancellationToken).ConfigureAwait(false);

        if (instrument is null)
        {
            return TypedResults.Problem(
                detail: "No instrument exists with that identifier.",
                statusCode: StatusCodes.Status404NotFound,
                title: "Instrument not found.");
        }

        var recorded = await actions.ListAsync(id, cancellationToken).ConfigureAwait(false);

        var factors = (await actions
            .ListAdjustmentsAsync(id, cancellationToken)
            .ConfigureAwait(false))
            .ToDictionary(adjustment => adjustment.CorporateActionId);

        return TypedResults.Ok(new CorporateActionsResponse(
            instrumentId,
            recorded.Count,
            [.. recorded.Select(action =>
                CorporateActionResponse.From(action, factors.GetValueOrDefault(action.Id)))]));
    }
}
