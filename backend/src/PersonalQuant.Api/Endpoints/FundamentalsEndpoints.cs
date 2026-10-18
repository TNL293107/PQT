using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using PersonalQuant.Api.Contracts;
using PersonalQuant.Application.Fundamentals;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Domain.Fundamentals;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Api.Endpoints;

/// <summary>
/// Read-only endpoints over point-in-time fundamentals (sprint 7.2).
/// </summary>
/// <remarks>
/// Both reads are cut at a day, the way <c>dataset export --known-as-of</c> is:
/// 00:00 UTC, before that day's session opened. A report filed after the cut
/// is absent, and the response says how many were withheld.
/// </remarks>
internal static class FundamentalsEndpoints
{
    private const string DateFormat = "yyyy-MM-dd";
    private const string OpenApiTag = "Fundamentals";

    /// <summary>Maps the fundamentals routes.</summary>
    /// <param name="endpoints">The route builder.</param>
    /// <returns>The same builder.</returns>
    public static IEndpointRouteBuilder MapFundamentalsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/instruments/{instrumentId:guid}/fundamentals", GetStatementsAsync)
            .WithTags(OpenApiTag)
            .WithName("GetInstrumentFundamentals")
            .WithSummary("Reads an issuer's statement values as they were known before a day's session opened.");

        endpoints.MapGet("/instruments/{instrumentId:guid}/ratios", GetRatiosAsync)
            .WithTags(OpenApiTag)
            .WithName("GetInstrumentRatios")
            .WithSummary("Computes an issuer's point-in-time ratios for a session (ADR-030).");

        return endpoints;
    }

    private static async Task<Results<Ok<FundamentalsAsOfResponse>, ProblemHttpResult>> GetStatementsAsync(
        Guid instrumentId,
        string? knownAsOf,
        string? scope,
        IInstrumentResolver instruments,
        IFundamentalsReader reader,
        CancellationToken cancellationToken)
    {
        if (!TryDay(knownAsOf, out var day, out var problem))
        {
            return problem;
        }

        FinancialStatementScope? parsedScope = (scope ?? "consolidated").ToLowerInvariant() switch
        {
            "consolidated" => FinancialStatementScope.Consolidated,
            "separate" => FinancialStatementScope.Separate,
            _ => null,
        };

        if (parsedScope is null)
        {
            return Invalid($"'{scope}' is not a scope. Use consolidated or separate.");
        }

        if (await instruments.FindByIdAsync(new InstrumentId(instrumentId), cancellationToken).ConfigureAwait(false) is null)
        {
            return NotFound();
        }

        var instant = day is { } cut
            ? new DateTimeOffset(cut.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : DateTimeOffset.UtcNow;

        var read = await reader.ReadAsync(new InstrumentId(instrumentId), parsedScope.Value, instant, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(FundamentalsAsOfResponse.From(read));
    }

    private static async Task<Results<Ok<FundamentalRatiosResponse>, ProblemHttpResult>> GetRatiosAsync(
        Guid instrumentId,
        string? on,
        IInstrumentResolver instruments,
        IFundamentalRatioService ratios,
        IBarRepository bars,
        CancellationToken cancellationToken)
    {
        if (!TryDay(on, out var day, out var problem))
        {
            return problem;
        }

        var id = new InstrumentId(instrumentId);

        if (await instruments.FindByIdAsync(id, cancellationToken).ConfigureAwait(false) is null)
        {
            return NotFound();
        }

        // Without a day, the latest session PQT holds a close for: ratios
        // priced on a day with no close would be all absent.
        var session = day;

        if (session is null)
        {
            var last = await bars.FindLastBeforeAsync(id, BarInterval.OneDay, DateTimeOffset.UtcNow, cancellationToken)
                .ConfigureAwait(false);

            if (last is null)
            {
                return Invalid("PQT holds no daily close for this instrument; name a session with ?on=yyyy-MM-dd.");
            }

            session = DateOnly.FromDateTime(last.OpenedAtUtc.UtcDateTime);
        }

        var read = await ratios.ReadAsync(id, session.Value, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(FundamentalRatiosResponse.From(read));
    }

    private static bool TryDay(string? text, out DateOnly? day, out ProblemHttpResult problem)
    {
        day = null;
        problem = null!;

        if (text is null)
        {
            return true;
        }

        if (!DateOnly.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            problem = Invalid($"'{text}' is not a date. Write it as {DateFormat}, for example 2024-08-01.");
            return false;
        }

        day = parsed;
        return true;
    }

    private static ProblemHttpResult Invalid(string detail) =>
        TypedResults.Problem(detail: detail, statusCode: StatusCodes.Status400BadRequest, title: "The request could not be read.");

    private static ProblemHttpResult NotFound() =>
        TypedResults.Problem(
            detail: "No instrument exists with that identifier.",
            statusCode: StatusCodes.Status404NotFound,
            title: "Instrument not found.");
}
