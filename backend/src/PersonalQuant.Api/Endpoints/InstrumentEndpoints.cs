using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using PersonalQuant.Api.Contracts;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Domain.Classification;
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
/// a way to put unsourced records into the system of record. Nor is there an
/// endpoint that triggers an import: it reads an external source and writes to
/// the master, and neither belongs behind an unauthenticated route.
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

        group.MapGet("/", ListAsync)
            .WithName("ListInstruments")
            .WithSummary("Pages through the instrument master, filtered and deterministically ordered.");

        group.MapGet("/search", SearchAsync)
            .WithName("SearchInstruments")
            .WithSummary("Finds instruments by ticker or name, strongest match first.");

        group.MapGet("/resolve", ResolveAsync)
            .WithName("ResolveInstrument")
            .WithSummary("Resolves a symbol to the one instrument trading under it.");

        group.MapGet("/{instrumentId:guid}", GetByIdAsync)
            .WithName("GetInstrument")
            .WithSummary("Reads one instrument in full by its canonical identifier.");

        group.MapGet("/{instrumentId:guid}/related", GetRelatedAsync)
            .WithName("GetRelatedInstruments")
            .WithSummary("Lists the instruments connected to one by identity.");

        return endpoints;
    }

    /// <summary>
    /// <c>GET /instruments</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The universe read, as distinct from search. It is filtered rather than
    /// ranked and ordered rather than scored, because a caller paging through
    /// it has to see every row exactly once.
    /// </para>
    /// <para>
    /// Delisted instruments are included unless a status is given — the
    /// opposite of search's default, and deliberately so. This is the read
    /// historical work uses, and silently omitting delisted rows from a
    /// universe is how survivorship bias gets into a backtest.
    /// </para>
    /// </remarks>
    private static async Task<Results<Ok<InstrumentListResponse>, ProblemHttpResult>> ListAsync(
        string? exchange,
        string? assetType,
        string? status,
        string? sector,
        int? limit,
        int? offset,
        IInstrumentCatalog catalog,
        CancellationToken cancellationToken)
    {
        ExchangeCode? exchangeCode = null;

        if (!string.IsNullOrWhiteSpace(exchange)
            && !ExchangeCode.TryCreate(exchange, out exchangeCode))
        {
            return Invalid("The exchange code is not valid.");
        }

        if (!TryParseFilter<AssetType>(assetType, out var parsedAssetType))
        {
            return Invalid("The asset type is not one this system records.");
        }

        if (!TryParseFilter<InstrumentStatus>(status, out var parsedStatus))
        {
            return Invalid("The status is not one this system records.");
        }

        ClassificationCode? sectorCode = null;

        if (!string.IsNullOrWhiteSpace(sector)
            && !ClassificationCode.TryCreate(sector, out sectorCode))
        {
            return Invalid("The sector code is not valid.");
        }

        if (!InstrumentListCriteria.TryCreate(
                exchangeCode,
                parsedAssetType,
                parsedStatus,
                sectorCode,
                limit,
                offset,
                out var criteria,
                out var problem))
        {
            return Invalid(problem);
        }

        var page = await catalog.ListAsync(criteria, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(InstrumentListResponse.From(page));

        static ProblemHttpResult Invalid(string detail) =>
            TypedResults.Problem(
                detail: detail,
                statusCode: StatusCodes.Status400BadRequest,
                title: "The instrument list request is not valid.");
    }

    /// <summary>
    /// Parses an optional enumeration filter, rejecting a value that is not a
    /// declared member.
    /// </summary>
    /// <remarks>
    /// <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/> accepts any
    /// numeric string, so <c>?status=99</c> would otherwise pass as a valid
    /// filter and quietly match nothing.
    /// </remarks>
    private static bool TryParseFilter<TEnum>(string? value, out TEnum? parsed)
        where TEnum : struct, Enum
    {
        parsed = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!Enum.TryParse<TEnum>(value, ignoreCase: true, out var candidate)
            || !Enum.IsDefined(candidate))
        {
            return false;
        }

        parsed = candidate;
        return true;
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
    /// rather than trusting the ticker and name it happens to be holding, and
    /// a reference page renders from the same response.
    /// </remarks>
    private static async Task<Results<Ok<InstrumentDetailResponse>, ProblemHttpResult>> GetByIdAsync(
        Guid instrumentId,
        IInstrumentCatalog catalog,
        CancellationToken cancellationToken)
    {
        var instrument = await catalog
            .FindDetailAsync(new InstrumentId(instrumentId), cancellationToken)
            .ConfigureAwait(false);

        return instrument is null
            ? TypedResults.Problem(
                detail: "No instrument exists with that identifier.",
                statusCode: StatusCodes.Status404NotFound,
                title: "Instrument not found.")
            : TypedResults.Ok(InstrumentDetailResponse.From(instrument));
    }

    /// <summary>
    /// <c>GET /instruments/{instrumentId}/related</c>.
    /// </summary>
    /// <remarks>
    /// An unknown instrument is a 404 and a known one with no relations is a
    /// 200 with an empty list. "No such security" and "this security stands
    /// alone" are different answers, and a client that cannot tell them apart
    /// shows the wrong message for both.
    /// </remarks>
    private static async Task<Results<Ok<InstrumentRelationsResponse>, ProblemHttpResult>>
        GetRelatedAsync(
        Guid instrumentId,
        IInstrumentCatalog catalog,
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

        var related = await catalog.ListRelatedAsync(id, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(new InstrumentRelationsResponse(
            instrumentId,
            related.Count,
            [.. related.Select(RelatedInstrumentResponse.From)]));
    }
}
