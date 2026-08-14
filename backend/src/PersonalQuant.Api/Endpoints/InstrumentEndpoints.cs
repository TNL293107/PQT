using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using PersonalQuant.Api.Contracts;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.Api.Endpoints;

/// <summary>
/// Read-only instrument master endpoints: search, symbol resolution, and
/// lookup by canonical identifier.
/// </summary>
/// <remarks>
/// <para>
/// There is no write surface. Instruments arrive through the provider import
/// pipeline, not through HTTP, so exposing create or update here would only be
/// a way to put unsourced records into the system of record.
/// </para>
/// <para>
/// Every response is a contract type from <see cref="Contracts"/>. Neither the
/// aggregate nor the application projection is serialised directly.
/// </para>
/// </remarks>
internal static class InstrumentEndpoints
{
    private const string RouteGroup = "/instruments";
    private const string OpenApiTag = "Instruments";

    /// <summary>
    /// Maps the instrument endpoints.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The same builder, to allow chaining.</returns>
    public static IEndpointRouteBuilder MapInstrumentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup(RouteGroup).WithTags(OpenApiTag);

        group.MapGet("/search", SearchAsync)
            .WithName("SearchInstruments")
            .WithSummary("Finds instruments by ticker or name, strongest match first.");

        group.MapGet("/resolve", ResolveAsync)
            .WithName("ResolveInstrument")
            .WithSummary("Resolves a symbol to the one instrument trading under it.");

        group.MapGet("/{instrumentId:guid}", GetByIdAsync)
            .WithName("GetInstrument")
            .WithSummary("Reads one instrument by its canonical identifier.");

        return endpoints;
    }

    /// <summary>
    /// <c>GET /instruments/search</c>.
    /// </summary>
    /// <remarks>
    /// A blank query is a client error rather than an empty result set. The
    /// two are different situations — "you did not ask me anything" and
    /// "nothing matches what you asked" — and a caller that cannot tell them
    /// apart will eventually show the user the wrong message.
    /// </remarks>
    private static async Task<Results<Ok<InstrumentSearchResponse>, ProblemHttpResult>> SearchAsync(
        string? q,
        int? limit,
        bool? includeInactive,
        IInstrumentSearchService search,
        CancellationToken cancellationToken)
    {
        if (!InstrumentSearchCriteria.TryCreate(
                q, limit, includeInactive ?? false, out var criteria, out var problem))
        {
            return TypedResults.Problem(
                detail: problem,
                statusCode: StatusCodes.Status400BadRequest,
                title: "The search request is not valid.");
        }

        var results = await search.SearchAsync(criteria, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(new InstrumentSearchResponse(
            criteria.Text,
            results.Count,
            criteria.Limit,
            [.. results.Select(InstrumentResponse.From)]));
    }

    /// <summary>
    /// <c>GET /instruments/resolve</c>.
    /// </summary>
    /// <remarks>
    /// The three outcomes share one body shape and differ by status: 200 when
    /// the symbol identifies exactly one instrument, 404 when none does, and
    /// 409 when several do. The candidates travel with the 409 so the caller
    /// can disambiguate rather than being told only that it failed.
    /// </remarks>
    private static async Task<Results<
        Ok<InstrumentResolutionResponse>,
        NotFound<InstrumentResolutionResponse>,
        Conflict<InstrumentResolutionResponse>,
        ProblemHttpResult>> ResolveAsync(
        string? symbol,
        string? exchange,
        IInstrumentResolver resolver,
        CancellationToken cancellationToken)
    {
        ExchangeCode? exchangeCode = null;

        if (!string.IsNullOrWhiteSpace(exchange)
            && !ExchangeCode.TryCreate(exchange, out exchangeCode))
        {
            return TypedResults.Problem(
                detail: "The exchange code is not valid.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "The resolve request is not valid.");
        }

        var resolution = await resolver
            .ResolveAsync(symbol, exchangeCode, cancellationToken)
            .ConfigureAwait(false);

        var response = InstrumentResolutionResponse.From(resolution);

        return resolution.Outcome switch
        {
            InstrumentResolutionOutcome.Resolved => TypedResults.Ok(response),
            InstrumentResolutionOutcome.Ambiguous => TypedResults.Conflict(response),
            _ => TypedResults.NotFound(response),
        };
    }

    /// <summary>
    /// <c>GET /instruments/{instrumentId}</c>.
    /// </summary>
    /// <remarks>
    /// The trusted read behind a client-side selection. A terminal that has
    /// stored an identifier calls this to re-establish what it points at,
    /// rather than trusting the ticker and name it happens to be holding.
    /// </remarks>
    private static async Task<Results<Ok<InstrumentResponse>, ProblemHttpResult>> GetByIdAsync(
        Guid instrumentId,
        IInstrumentResolver resolver,
        CancellationToken cancellationToken)
    {
        var instrument = await resolver
            .FindByIdAsync(new InstrumentId(instrumentId), cancellationToken)
            .ConfigureAwait(false);

        return instrument is null
            ? TypedResults.Problem(
                detail: "No instrument exists with that identifier.",
                statusCode: StatusCodes.Status404NotFound,
                title: "Instrument not found.")
            : TypedResults.Ok(InstrumentResponse.From(instrument));
    }
}
