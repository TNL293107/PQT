using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using PersonalQuant.Api.Contracts;
using PersonalQuant.Application.Breadth;
using PersonalQuant.Application.Universes;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Universes;

namespace PersonalQuant.Api.Endpoints;

/// <summary>
/// Read-only universe endpoints: what is defined, and how its members moved.
/// </summary>
/// <remarks>
/// <para>
/// Breadth is computed by <see cref="IMarketBreadthService"/> and only rendered
/// here (ADR-026). A count in the browser would be a second definition of
/// "advancing", and the first ex-date would make the two disagree.
/// </para>
/// <para>
/// Refusals are problem details whose <c>outcome</c> extension names the
/// reason, so a client can tell "not a trading day" from "the calendar does not
/// reach" without parsing a sentence. An unknown universe is a 404; everything
/// else the server declines to count is a 422, because the request was well
/// formed and the honest answer is that it cannot be answered.
/// </para>
/// </remarks>
internal static class UniverseEndpoints
{
    private const string RouteGroup = "/universes";
    private const string OpenApiTag = "Universes";
    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>
    /// Maps the universe endpoints.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The same builder, to allow chaining.</returns>
    public static IEndpointRouteBuilder MapUniverseEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup(RouteGroup).WithTags(OpenApiTag);

        group.MapGet("/", ListUniversesAsync)
            .WithName("ListUniverses")
            .WithSummary("Lists every universe, with its declared coverage and today's member count.");

        group.MapGet("/{code}/breadth", GetBreadthAsync)
            .WithName("GetUniverseBreadth")
            .WithSummary("Counts advancing, declining, unchanged and no-data members on a session.");

        // A venue is not a universe, but its breadth is the same count over a
        // different member set, refused and rendered the same way.
        endpoints.MapGroup("/venues").WithTags("Venues")
            .MapGet("/{code}/breadth", GetVenueBreadthAsync)
            .WithName("GetVenueBreadth")
            .WithSummary("Counts every equity listed on a venue on a session, and says which hold no data.");

        return endpoints;
    }

    /// <summary>
    /// <c>GET /venues/{code}/breadth?on=yyyy-MM-dd</c>.
    /// </summary>
    private static async Task<Results<Ok<BreadthResponse>, ProblemHttpResult>> GetVenueBreadthAsync(
        string code,
        string? on,
        IMarketBreadthService breadth,
        CancellationToken cancellationToken)
    {
        if (!ExchangeCode.TryCreate(code, out var venue))
        {
            return Invalid($"'{code}' is not a venue code, such as HOSE.");
        }

        if (!TryReadSession(on, out var session, out var problem))
        {
            return problem;
        }

        var result = await breadth.ComputeForVenueAsync(venue, session, cancellationToken).ConfigureAwait(false);

        return result.Breadth is { } counted
            ? TypedResults.Ok(BreadthResponse.From(counted))
            : Refused(result);
    }

    private static bool TryReadSession(string? on, out DateOnly? session, out ProblemHttpResult problem)
    {
        session = null;
        problem = null!;

        if (on is null)
        {
            return true;
        }

        if (!DateOnly.TryParseExact(on, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            problem = Invalid($"'{on}' is not a date. Write it as {DateFormat}, for example 2026-09-30.");
            return false;
        }

        session = day;
        return true;
    }

    /// <summary>
    /// <c>GET /universes</c>.
    /// </summary>
    /// <remarks>
    /// <c>memberCount</c> is null, not zero, for a universe whose coverage does
    /// not reach today: its membership today is unknown rather than empty.
    /// </remarks>
    private static async Task<Ok<UniverseListResponse>> ListUniversesAsync(
        IUniverseDirectory directory,
        CancellationToken cancellationToken)
    {
        var listing = await directory.ListAsync(cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(new UniverseListResponse(
            listing.AsOf,
            listing.Universes.Count,
            [.. listing.Universes.Select(UniverseResponse.From)]));
    }

    /// <summary>
    /// <c>GET /universes/{code}/breadth?on=yyyy-MM-dd</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Without <c>on</c>, the latest session up to today on which any member
    /// has a daily bar, and the response says it chose.
    /// </para>
    /// <para>
    /// The day is parsed exactly. A culture-dependent parse would read
    /// <c>09/10/2026</c> as September in one locale and October in another,
    /// and either answer would be a count for a session nobody asked about.
    /// </para>
    /// </remarks>
    private static async Task<Results<Ok<BreadthResponse>, ProblemHttpResult>> GetBreadthAsync(
        string code,
        string? on,
        IMarketBreadthService breadth,
        CancellationToken cancellationToken)
    {
        if (!UniverseCode.TryCreate(code, out var universe))
        {
            return Invalid(
                $"'{code}' is not a universe code: {UniverseCode.MinLength} to {UniverseCode.MaxLength} "
                    + "letters, digits or underscores.");
        }

        if (!TryReadSession(on, out var session, out var problem))
        {
            return problem;
        }

        var result = await breadth.ComputeAsync(universe, session, cancellationToken).ConfigureAwait(false);

        return result.Breadth is { } counted
            ? TypedResults.Ok(BreadthResponse.From(counted))
            : Refused(result);
    }

    private static ProblemHttpResult Refused(BreadthResult result)
    {
        var (status, title) = result.Outcome switch
        {
            BreadthOutcome.UnknownUniverse => (StatusCodes.Status404NotFound, "Universe not found."),
            BreadthOutcome.UnknownVenue => (StatusCodes.Status404NotFound, "Venue not found."),
            BreadthOutcome.MembershipUnknown => (
                StatusCodes.Status422UnprocessableEntity, "The universe's membership on that session is not known."),
            BreadthOutcome.CalendarNotCovered => (
                StatusCodes.Status422UnprocessableEntity, "The venue calendar does not cover that session."),
            BreadthOutcome.NotATradingDay => (
                StatusCodes.Status422UnprocessableEntity, "That session is not a trading day."),
            _ => (StatusCodes.Status422UnprocessableEntity, "There is no data to count."),
        };

        return TypedResults.Problem(
            detail: result.Detail,
            statusCode: status,
            title: title,
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["outcome"] = result.Outcome.ToString(),
                ["scope"] = result.Scope.Kind.ToString(),
                ["code"] = result.Scope.Value,
                ["session"] = result.Session?.ToString(DateFormat, CultureInfo.InvariantCulture),
            });
    }

    private static ProblemHttpResult Invalid(string detail) =>
        TypedResults.Problem(
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest,
            title: "The breadth request is not valid.");
}
