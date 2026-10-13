using System.Globalization;
using Microsoft.Extensions.Logging;
using PersonalQuant.Application.Abstractions;
using PersonalQuant.Application.Diagnostics;
using PersonalQuant.Application.Quotes;
using PersonalQuant.Application.Universes;
using PersonalQuant.Domain.Universes;

namespace PersonalQuant.Application.Breadth;

/// <summary>
/// Counts how a universe's members moved on a session.
/// </summary>
/// <remarks>
/// The one implementation of "advancing" in the system (ADR-026). The API, the
/// operator CLI and the console all call this; none of them counts anything.
/// </remarks>
public interface IMarketBreadthService
{
    /// <summary>
    /// Counts advancers, decliners, unchanged members and members without data.
    /// </summary>
    /// <param name="code">The universe.</param>
    /// <param name="session">
    /// The session to count, or null for the latest session up to today on
    /// which any member has a daily bar.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The count, or the reason it was refused.</returns>
    Task<BreadthResult> ComputeAsync(
        UniverseCode code,
        DateOnly? session,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="IMarketBreadthService"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Members</strong> are read through <see cref="IUniverseCatalog"/>,
/// so a session the universe does not claim to know is refused rather than
/// counted over an empty set (ADR-020).
/// </para>
/// <para>
/// <strong>The move</strong> of each member is its quote from
/// <see cref="IQuoteService"/> — close against the previous close restated for
/// actions going ex and rounded to the tick, on each member's own venue's
/// previous trading day. Breadth only counts; a watchlist showing the same name
/// on the same session shows the same move.
/// </para>
/// </remarks>
/// <param name="universes">The universe store, for the coverage claim.</param>
/// <param name="catalog">Answers who belonged, refusing what is not known.</param>
/// <param name="quotes">Quotes the members.</param>
/// <param name="reader">Bulk reads of closes, for choosing a default session.</param>
/// <param name="clock">Today, when no session is named.</param>
/// <param name="logger">The log.</param>
internal sealed class MarketBreadthService(
    IUniverseRepository universes,
    IUniverseCatalog catalog,
    IQuoteService quotes,
    IQuoteReader reader,
    IClock clock,
    ILogger<MarketBreadthService> logger) : IMarketBreadthService
{
    /// <summary>
    /// How far back a default session is looked for.
    /// </summary>
    /// <remarks>
    /// A deployment whose newest member bar is older than a month has stopped
    /// ingesting, and "breadth for the latest session" would quietly answer
    /// about a market weeks out of date. Refusing says so; <c>--on</c> still
    /// reaches the older day on purpose.
    /// </remarks>
    private const int DefaultSessionLookbackDays = 31;

    /// <inheritdoc />
    public async Task<BreadthResult> ComputeAsync(
        UniverseCode code,
        DateOnly? session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(code);

        BreadthResult result;

        if (session is { } named)
        {
            result = await ComputeOnAsync(code, named, defaulted: false, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            var chosen = await ChooseSessionAsync(code, cancellationToken).ConfigureAwait(false);

            result = chosen.Refusal ?? await ComputeOnAsync(
                    code, chosen.Session, defaulted: true, cancellationToken)
                .ConfigureAwait(false);
        }

        Log(result);
        return result;
    }

    private async Task<BreadthResult> ComputeOnAsync(
        UniverseCode code,
        DateOnly session,
        bool defaulted,
        CancellationToken cancellationToken)
    {
        var constituents = await catalog
            .ConstituentsAsOfAsync(code, session, cancellationToken)
            .ConfigureAwait(false);

        if (!constituents.IsKnown)
        {
            return MembershipRefusal(code, session, constituents.UnknownReason);
        }

        var members = constituents.Members;

        if (members.Count == 0)
        {
            // A covered day on which the universe held nothing is a fact, and
            // it is answered as one: no venues, no rows, every count zero.
            return BreadthResult.Computed(new MarketBreadth(code, session, defaulted, [], []));
        }

        var board = await quotes.QuoteAsync(members, session, cancellationToken).ConfigureAwait(false);

        return board.Outcome switch
        {
            QuoteOutcome.Quoted => BreadthResult.Computed(new MarketBreadth(
                code,
                session,
                defaulted,
                [.. board.Venues.Select(venue => new BreadthVenue(venue.Code, venue.PreviousSession))],
                [.. board.Quotes.Select(Member)])),
            QuoteOutcome.CalendarNotCovered => BreadthResult.Refused(
                BreadthOutcome.CalendarNotCovered, code, session, board.Detail),
            QuoteOutcome.NotATradingDay => BreadthResult.Refused(
                BreadthOutcome.NotATradingDay, code, session, board.Detail),
            _ => throw new InvalidOperationException(
                $"A named session was answered with {board.Outcome}, which only a defaulted one can be."),
        };
    }

    private static BreadthMember Member(Quote quote) =>
        new(
            quote.InstrumentId,
            quote.Ticker,
            quote.Venue,
            quote.PreviousSession,
            quote.RawPreviousClose,
            quote.Reference,
            quote.Close,
            quote.Change,
            quote.Volume,
            quote.Move switch
            {
                PriceMove.Advancing => BreadthCategory.Advancing,
                PriceMove.Declining => BreadthCategory.Declining,
                PriceMove.Unchanged => BreadthCategory.Unchanged,
                _ => BreadthCategory.NoData,
            });

    /// <summary>
    /// Finds the latest session up to today on which some member — a member
    /// <em>that day</em> — has a daily bar.
    /// </summary>
    /// <remarks>
    /// Membership is checked per day rather than against today's set, so a
    /// name admitted yesterday does not pull the default forward to a session
    /// on which it was not yet a member.
    /// </remarks>
    private async Task<SessionChoice> ChooseSessionAsync(UniverseCode code, CancellationToken cancellationToken)
    {
        var universe = await universes.FindByCodeAsync(code, cancellationToken).ConfigureAwait(false);

        if (universe is null)
        {
            return SessionChoice.Refused(MembershipRefusal(code, null, UniverseUnknownReason.NoSuchUniverse));
        }

        if (universe.Coverage is not { } coverage)
        {
            return SessionChoice.Refused(MembershipRefusal(code, null, UniverseUnknownReason.NoCoverageDeclared));
        }

        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var cutoff = coverage.Until is { } until && until <= today ? until.AddDays(-1) : today;

        if (cutoff < coverage.From)
        {
            return SessionChoice.Refused(MembershipRefusal(code, null, UniverseUnknownReason.OutsideCoverage));
        }

        var lookback = cutoff.AddDays(-DefaultSessionLookbackDays);
        var from = lookback < coverage.From ? coverage.From : lookback;

        var history = await catalog.MembershipOverAsync(code, from, cutoff, cancellationToken).ConfigureAwait(false);

        if (!history.IsKnown)
        {
            return SessionChoice.Refused(MembershipRefusal(code, null, history.UnknownReason));
        }

        var spells = history.Spells.ToLookup(spell => spell.InstrumentId);
        var closes = spells.Count == 0
            ? []
            : await reader
                .ListDailyClosesAsync([.. spells.Select(group => group.Key)], from, cutoff, cancellationToken)
                .ConfigureAwait(false);

        var latest = closes
            .Where(close => spells[close.InstrumentId].Any(spell => spell.Includes(close.Session)))
            .Select(close => (DateOnly?)close.Session)
            .Max();

        return latest is { } session
            ? new SessionChoice(session, Refusal: null)
            : SessionChoice.Refused(BreadthResult.Refused(
                BreadthOutcome.NoData,
                code,
                null,
                $"No member of {code} has a daily bar between {Day(from)} and {Day(cutoff)}. "
                    + "Name a session to count an older one."));
    }

    private static BreadthResult MembershipRefusal(
        UniverseCode code,
        DateOnly? session,
        UniverseUnknownReason? reason)
    {
        var when = session is { } day ? $" on {Day(day)}" : string.Empty;

        return reason switch
        {
            UniverseUnknownReason.NoSuchUniverse => BreadthResult.Refused(
                BreadthOutcome.UnknownUniverse, code, session, $"No universe is defined under the code {code}."),
            UniverseUnknownReason.NoCoverageDeclared => BreadthResult.Refused(
                BreadthOutcome.MembershipUnknown,
                code,
                session,
                $"{code} has no declared membership coverage, so who belonged to it{when} is not known. "
                    + "An unknown membership is not an empty one."),
            _ => BreadthResult.Refused(
                BreadthOutcome.MembershipUnknown,
                code,
                session,
                $"{code} does not claim to know its membership{when}; the date is outside its declared "
                    + "coverage. An unknown membership is not an empty one."),
        };
    }

    private void Log(BreadthResult result)
    {
        // The counts walk the member rows; not worth doing for a disabled level.
        if (!logger.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        if (result.Breadth is { } breadth)
        {
            var session = Day(breadth.Session);

            ApplicationLog.BreadthComputed(
                logger,
                breadth.Code.Value,
                session,
                breadth.Advancing,
                breadth.Declining,
                breadth.Unchanged,
                breadth.NoData);

            return;
        }

        ApplicationLog.BreadthRefused(logger, result.Code.Value, result.Outcome);
    }

    private static string Day(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>The session chosen by default, or why none could be.</summary>
    private sealed record SessionChoice(DateOnly Session, BreadthResult? Refusal)
    {
        public static SessionChoice Refused(BreadthResult refusal) => new(default, refusal);
    }
}
