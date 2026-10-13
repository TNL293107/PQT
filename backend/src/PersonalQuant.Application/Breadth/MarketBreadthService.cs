using System.Globalization;
using Microsoft.Extensions.Logging;
using PersonalQuant.Application.Abstractions;
using PersonalQuant.Application.Diagnostics;
using PersonalQuant.Application.Exchanges;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Application.Quotes;
using PersonalQuant.Application.Universes;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
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

    /// <summary>
    /// Counts every equity listed on a venue that session, by the same quotes.
    /// </summary>
    /// <remarks>
    /// The members are the equities the instrument master holds on the venue
    /// now and whose record says they were listed that day; the answer states
    /// how many had no recorded listing date and which have never had a bar
    /// ingested (<see cref="VenueListing"/>).
    /// </remarks>
    /// <param name="venue">The venue.</param>
    /// <param name="session">
    /// The session to count, or null for the latest session up to today on
    /// which any equity listed that day has a daily bar.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The count, or the reason it was refused.</returns>
    Task<BreadthResult> ComputeForVenueAsync(
        ExchangeCode venue,
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
/// <param name="exchanges">Resolves a venue code.</param>
/// <param name="history">Says which venue each equity traded on that session.</param>
/// <param name="quotes">Quotes the members.</param>
/// <param name="reader">Bulk reads of closes, listings and holdings.</param>
/// <param name="clock">Today, when no session is named.</param>
/// <param name="logger">The log.</param>
internal sealed class MarketBreadthService(
    IUniverseRepository universes,
    IUniverseCatalog catalog,
    IExchangeRepository exchanges,
    IVenueHistoryRepository history,
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
            return BreadthResult.Computed(new MarketBreadth(BreadthScope.Of(code), session, defaulted, [], []));
        }

        return await CountAsync(BreadthScope.Of(code), members, session, defaulted, listing: null, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<BreadthResult> ComputeForVenueAsync(
        ExchangeCode venue,
        DateOnly? session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(venue);

        var result = await ComputeForVenueCoreAsync(venue, session, cancellationToken).ConfigureAwait(false);

        Log(result);
        return result;
    }

    private async Task<BreadthResult> ComputeForVenueCoreAsync(
        ExchangeCode venue,
        DateOnly? session,
        CancellationToken cancellationToken)
    {
        var scope = BreadthScope.Of(venue);
        var exchange = await exchanges.FindByCodeAsync(venue, cancellationToken).ConfigureAwait(false);

        if (exchange is null)
        {
            return BreadthResult.Refused(
                BreadthOutcome.UnknownVenue, scope, session, $"No venue is registered under the code {venue}.");
        }

        var equities = await reader.ListEquitiesAsync(cancellationToken).ConfigureAwait(false);

        var timelines = await VenueTimelines
            .LoadAsync(
                history,
                equities.ToDictionary(equity => equity.Instrument.InstrumentId, equity => equity.Instrument.ExchangeId),
                cancellationToken)
            .ConfigureAwait(false);

        // On this venue that session, by its venue history, and listed then.
        bool Member(ListedEquity equity, DateOnly day) =>
            timelines[equity.Instrument.InstrumentId].On(day) == exchange.Id && equity.ListedOnSession(day);

        DateOnly day;

        if (session is { } named)
        {
            day = named;
        }
        else
        {
            var chosen = await LatestVenueSessionAsync(venue, equities, Member, cancellationToken).ConfigureAwait(false);

            if (chosen.Refusal is { } refusal)
            {
                return refusal;
            }

            day = chosen.Session;
        }

        var members = equities.Where(equity => Member(equity, day)).ToList();
        var ids = members.Select(member => member.Instrument.InstrumentId).ToList();

        IReadOnlySet<InstrumentId> held = ids.Count == 0
            ? new HashSet<InstrumentId>()
            : await reader.ListWithDailyBarsAsync(ids, cancellationToken).ConfigureAwait(false);

        var listing = new VenueListing(
            members.Count(member => member.ListedOn is null),
            [
                .. members
                    .Where(member => !held.Contains(member.Instrument.InstrumentId))
                    .Select(member => member.Instrument.Ticker)
                    .OrderBy(ticker => ticker.Value, StringComparer.Ordinal),
            ]);

        if (ids.Count == 0)
        {
            // As for a universe: a venue that held no equity that day is a fact
            // about the master, answered as an empty count.
            return BreadthResult.Computed(new MarketBreadth(scope, day, session is null, [], [], listing));
        }

        return await CountAsync(scope, ids, day, defaulted: session is null, listing, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Finds the latest session up to today on which an equity listed on the
    /// venue that day has a daily bar.
    /// </summary>
    private async Task<SessionChoice> LatestVenueSessionAsync(
        ExchangeCode venue,
        IReadOnlyList<ListedEquity> equities,
        Func<ListedEquity, DateOnly, bool> member,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var from = today.AddDays(-DefaultSessionLookbackDays);
        var byId = equities.ToDictionary(equity => equity.Instrument.InstrumentId);

        var closes = byId.Count == 0
            ? []
            : await reader.ListDailyClosesAsync([.. byId.Keys], from, today, cancellationToken).ConfigureAwait(false);

        var latest = closes
            .Where(close => member(byId[close.InstrumentId], close.Session))
            .Select(close => (DateOnly?)close.Session)
            .Max();

        return latest is { } session
            ? new SessionChoice(session, Refusal: null)
            : SessionChoice.Refused(BreadthResult.Refused(
                BreadthOutcome.NoData,
                BreadthScope.Of(venue),
                null,
                $"No equity listed on {venue} has a daily bar between {Day(from)} and {Day(today)}. "
                    + "Name a session to count an older one."));
    }

    /// <summary>Quotes a member set on a session and counts the quotes.</summary>
    private async Task<BreadthResult> CountAsync(
        BreadthScope scope,
        IReadOnlyCollection<InstrumentId> members,
        DateOnly session,
        bool defaulted,
        VenueListing? listing,
        CancellationToken cancellationToken)
    {
        var board = await quotes.QuoteAsync(members, session, cancellationToken).ConfigureAwait(false);

        return board.Outcome switch
        {
            QuoteOutcome.Quoted => BreadthResult.Computed(new MarketBreadth(
                scope,
                session,
                defaulted,
                [.. board.Venues.Select(venue => new BreadthVenue(venue.Code, venue.PreviousSession))],
                [.. board.Quotes.Select(Member)],
                listing)),
            QuoteOutcome.CalendarNotCovered => BreadthResult.Refused(
                BreadthOutcome.CalendarNotCovered, scope, session, board.Detail),
            QuoteOutcome.NotATradingDay => BreadthResult.Refused(
                BreadthOutcome.NotATradingDay, scope, session, board.Detail),
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
                BreadthScope.Of(code),
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
                BreadthOutcome.UnknownUniverse, BreadthScope.Of(code), session, $"No universe is defined under the code {code}."),
            UniverseUnknownReason.NoCoverageDeclared => BreadthResult.Refused(
                BreadthOutcome.MembershipUnknown,
                BreadthScope.Of(code),
                session,
                $"{code} has no declared membership coverage, so who belonged to it{when} is not known. "
                    + "An unknown membership is not an empty one."),
            _ => BreadthResult.Refused(
                BreadthOutcome.MembershipUnknown,
                BreadthScope.Of(code),
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
                breadth.Scope.Value,
                session,
                breadth.Advancing,
                breadth.Declining,
                breadth.Unchanged,
                breadth.NoData);

            return;
        }

        ApplicationLog.BreadthRefused(logger, result.Scope.Value, result.Outcome);
    }

    private static string Day(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>The session chosen by default, or why none could be.</summary>
    private sealed record SessionChoice(DateOnly Session, BreadthResult? Refusal)
    {
        public static SessionChoice Refused(BreadthResult refusal) => new(default, refusal);
    }
}
