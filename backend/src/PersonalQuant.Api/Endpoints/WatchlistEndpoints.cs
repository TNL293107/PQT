using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using PersonalQuant.Api.Contracts;
using PersonalQuant.Application.Terminal;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.Api.Endpoints;

/// <summary>
/// The operator's watchlists: the first endpoints that write (ADR-026, ADR-027).
/// </summary>
/// <remarks>
/// Reads are open like every other read. Every write sits in a group that
/// requires the operator key, so a write endpoint added here later inherits the
/// guard rather than having to remember it.
/// </remarks>
internal static class WatchlistEndpoints
{
    private const string RouteGroup = "/watchlists";
    private const string OpenApiTag = "Watchlists";

    /// <summary>Maps the watchlist endpoints.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The same builder, to allow chaining.</returns>
    public static IEndpointRouteBuilder MapWatchlistEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var reads = endpoints.MapGroup(RouteGroup).WithTags(OpenApiTag);

        reads.MapGet("/", ListAsync)
            .WithName("ListWatchlists")
            .WithSummary("Lists every watchlist.");

        reads.MapGet("/{name}", GetAsync)
            .WithName("GetWatchlist")
            .WithSummary("Reads one watchlist and its instruments.");

        var writes = endpoints.MapGroup(RouteGroup).WithTags(OpenApiTag).RequireOperatorKey();

        writes.MapPost("/", CreateAsync)
            .WithName("CreateWatchlist")
            .WithSummary("Creates an empty watchlist. Requires the operator key.");

        writes.MapPatch("/{name}", RenameAsync)
            .WithName("RenameWatchlist")
            .WithSummary("Renames a watchlist. Requires the operator key.");

        writes.MapDelete("/{name}", DeleteAsync)
            .WithName("DeleteWatchlist")
            .WithSummary("Deletes a watchlist. Requires the operator key.");

        writes.MapPut("/{name}/instruments/{instrumentId:guid}", AddAsync)
            .WithName("AddWatchlistInstrument")
            .WithSummary("Appends an instrument to a watchlist. Requires the operator key.");

        writes.MapDelete("/{name}/instruments/{instrumentId:guid}", RemoveAsync)
            .WithName("RemoveWatchlistInstrument")
            .WithSummary("Removes an instrument from a watchlist. Requires the operator key.");

        return endpoints;
    }

    private static async Task<Ok<WatchlistListResponse>> ListAsync(
        IWatchlistService watchlists,
        CancellationToken cancellationToken)
    {
        var lists = await watchlists.ListAsync(cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(new WatchlistListResponse([.. lists.Select(WatchlistSummaryResponse.From)]));
    }

    private static async Task<Results<Ok<WatchlistResponse>, ProblemHttpResult>> GetAsync(
        string name,
        IWatchlistService watchlists,
        CancellationToken cancellationToken)
    {
        var view = await watchlists.FindAsync(name, cancellationToken).ConfigureAwait(false);

        return view is null
            ? TypedResults.Problem(
                detail: $"There is no watchlist named '{name}'.",
                statusCode: StatusCodes.Status404NotFound,
                title: "Watchlist not found.")
            : TypedResults.Ok(WatchlistResponse.From(view));
    }

    /// <remarks>
    /// <c>PUT</c> for adding an instrument and <c>POST</c> for creating a list:
    /// adding the same instrument twice leaves the list as one add would, which
    /// is what <c>PUT</c> promises, while creating a list that exists is a
    /// conflict the caller should hear about.
    /// </remarks>
    private static async Task<Results<Created<WatchlistResponse>, ProblemHttpResult>> CreateAsync(
        CreateWatchlistRequest request,
        IWatchlistService watchlists,
        CancellationToken cancellationToken)
    {
        var change = await watchlists.CreateAsync(request?.Name ?? string.Empty, cancellationToken)
            .ConfigureAwait(false);

        return change is { Succeeded: true, Watchlist: { } created }
            ? TypedResults.Created($"{RouteGroup}/{created.Name}", WatchlistResponse.From(created))
            : Problem(change);
    }

    private static async Task<Results<Ok<WatchlistResponse>, ProblemHttpResult>> RenameAsync(
        string name,
        RenameWatchlistRequest request,
        IWatchlistService watchlists,
        CancellationToken cancellationToken)
    {
        var change = await watchlists
            .RenameAsync(name, request?.Name ?? string.Empty, cancellationToken)
            .ConfigureAwait(false);

        return Answer(change);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        string name,
        IWatchlistService watchlists,
        CancellationToken cancellationToken)
    {
        var change = await watchlists.DeleteAsync(name, cancellationToken).ConfigureAwait(false);

        return change.Succeeded ? TypedResults.NoContent() : Problem(change);
    }

    private static async Task<Results<Ok<WatchlistResponse>, ProblemHttpResult>> AddAsync(
        string name,
        Guid instrumentId,
        IWatchlistService watchlists,
        CancellationToken cancellationToken)
    {
        var change = await watchlists
            .AddAsync(name, new InstrumentId(instrumentId), cancellationToken)
            .ConfigureAwait(false);

        return Answer(change);
    }

    private static async Task<Results<Ok<WatchlistResponse>, ProblemHttpResult>> RemoveAsync(
        string name,
        Guid instrumentId,
        IWatchlistService watchlists,
        CancellationToken cancellationToken)
    {
        var change = await watchlists
            .RemoveAsync(name, new InstrumentId(instrumentId), cancellationToken)
            .ConfigureAwait(false);

        return Answer(change);
    }

    private static Results<Ok<WatchlistResponse>, ProblemHttpResult> Answer(WatchlistChange change) =>
        change is { Succeeded: true, Watchlist: { } view }
            ? TypedResults.Ok(WatchlistResponse.From(view))
            : Problem(change);

    private static ProblemHttpResult Problem(WatchlistChange change)
    {
        var (status, title) = change.Outcome switch
        {
            WatchlistOutcome.NotFound => (StatusCodes.Status404NotFound, "Watchlist not found."),
            WatchlistOutcome.InstrumentNotFound => (StatusCodes.Status404NotFound, "Instrument not found."),
            WatchlistOutcome.NameTaken => (StatusCodes.Status409Conflict, "The name is taken."),
            WatchlistOutcome.Invalid => (StatusCodes.Status400BadRequest, "The watchlist request is not valid."),

            // A successful change always carries the list it changed. Reaching
            // here means it did not, which is a defect, not a caller's mistake.
            _ => (StatusCodes.Status500InternalServerError, "The watchlist change did not complete."),
        };

        return TypedResults.Problem(detail: change.Message, statusCode: status, title: title);
    }
}
