using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using PersonalQuant.Api.Contracts;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.Api.Endpoints;

/// <summary>
/// Read-only market data endpoints: the stored series, and the record of how
/// it got there.
/// </summary>
/// <remarks>
/// <para>
/// There is no endpoint that triggers ingestion. A request that causes
/// outbound calls to a rate-limited third party is not something to expose
/// before there is authentication to put in front of it — anyone who could
/// reach it could exhaust the day's provider quota. Ingestion is driven by the
/// host; the trigger arrives with the authentication in Phase 18.
/// </para>
/// <para>
/// Both endpoints hang off an instrument rather than living at the root. A
/// series without an instrument is not a thing anyone asks for, and the nested
/// route means the identifier is validated once, in one place.
/// </para>
/// </remarks>
internal static class MarketDataEndpoints
{
    private const string RouteGroup = "/instruments/{instrumentId:guid}";
    private const string OpenApiTag = "Market data";

    /// <summary>Most ingestion runs a caller may ask for.</summary>
    private const int MaxRuns = 50;

    /// <summary>Runs returned when the caller does not ask for a number.</summary>
    private const int DefaultRuns = 10;

    /// <summary>
    /// Maps the market data endpoints.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The same builder, to allow chaining.</returns>
    public static IEndpointRouteBuilder MapMarketDataEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup(RouteGroup).WithTags(OpenApiTag);

        group.MapGet("/bars", GetBarsAsync)
            .WithName("GetInstrumentBars")
            .WithSummary("Reads a bounded window of an instrument's OHLCV series.");

        group.MapGet("/ingestion", GetIngestionHistoryAsync)
            .WithName("GetInstrumentIngestionHistory")
            .WithSummary("Reads the recent ingestion attempts for an instrument's series.");

        return endpoints;
    }

    /// <summary>
    /// <c>GET /instruments/{instrumentId}/bars</c>.
    /// </summary>
    /// <remarks>
    /// An unknown instrument is a 404 and a known instrument with no data is a
    /// 200 with an empty list. They are different situations — "there is no
    /// such security" and "nothing has been ingested for it yet" — and a chart
    /// that cannot tell them apart shows the wrong message for both.
    /// </remarks>
    private static async Task<Results<Ok<BarSeriesResponse>, ProblemHttpResult>> GetBarsAsync(
        Guid instrumentId,
        string? interval,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int? limit,
        IInstrumentCatalog catalog,
        IMarketDataQueryService marketData,
        CancellationToken cancellationToken)
    {
        if (!BarIntervalParser.TryParse(interval, out var resolution))
        {
            return TypedResults.Problem(
                detail: $"The interval is not one this system records. Accepted: {BarIntervalParser.DescribeAccepted()}.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "The bar request is not valid.");
        }

        var id = new InstrumentId(instrumentId);

        if (!BarQuery.TryCreate(id, resolution, from, to, limit, out var query, out var problem))
        {
            return TypedResults.Problem(
                detail: problem,
                statusCode: StatusCodes.Status400BadRequest,
                title: "The bar request is not valid.");
        }

        // The instrument is confirmed before the series is read. Returning an
        // empty series for an identifier that does not exist would let a typo
        // look like a security that has never traded.
        var instrument = await catalog.FindDetailAsync(id, cancellationToken).ConfigureAwait(false);

        if (instrument is null)
        {
            return TypedResults.Problem(
                detail: "No instrument exists with that identifier.",
                statusCode: StatusCodes.Status404NotFound,
                title: "Instrument not found.");
        }

        var series = await marketData.GetSeriesAsync(query, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(BarSeriesResponse.From(series, query.Limit));
    }

    /// <summary>
    /// <c>GET /instruments/{instrumentId}/ingestion</c>.
    /// </summary>
    /// <remarks>
    /// The endpoint that explains a gap. A series that stops on a Tuesday is
    /// either a market holiday or a failed run, and only the audit trail says
    /// which.
    /// </remarks>
    private static async Task<Results<Ok<IngestionHistoryResponse>, ProblemHttpResult>>
        GetIngestionHistoryAsync(
        Guid instrumentId,
        string? interval,
        int? limit,
        IInstrumentCatalog catalog,
        IMarketDataQueryService marketData,
        CancellationToken cancellationToken)
    {
        if (!BarIntervalParser.TryParse(interval, out var resolution))
        {
            return TypedResults.Problem(
                detail: $"The interval is not one this system records. Accepted: {BarIntervalParser.DescribeAccepted()}.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "The ingestion history request is not valid.");
        }

        if (limit is < 1 or > MaxRuns)
        {
            return TypedResults.Problem(
                detail: $"The run limit must be between 1 and {MaxRuns}.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "The ingestion history request is not valid.");
        }

        var id = new InstrumentId(instrumentId);
        var instrument = await catalog.FindDetailAsync(id, cancellationToken).ConfigureAwait(false);

        if (instrument is null)
        {
            return TypedResults.Problem(
                detail: "No instrument exists with that identifier.",
                statusCode: StatusCodes.Status404NotFound,
                title: "Instrument not found.");
        }

        var runs = await marketData
            .ListRecentRunsAsync(id, resolution, limit ?? DefaultRuns, cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new IngestionHistoryResponse(
            instrumentId,
            resolution.ToString(),
            runs.Count,
            [.. runs.Select(IngestionRunResponse.From)]));
    }
}
