using System.Globalization;
using Microsoft.Extensions.Logging;
using PersonalQuant.Application.Abstractions;
using PersonalQuant.Application.Diagnostics;
using PersonalQuant.Application.Exchanges;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Application.Universes;
using PersonalQuant.Domain.CorporateActions;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;
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
/// counted over an empty set (ADR-020). <strong>The previous session</strong>
/// is each member's own venue's previous trading day: members on different
/// venues are supported, and the session must be a trading day on every venue
/// any member trades on, because a count that mixed one venue's session with
/// another's holiday would compare closes from different days.
/// </para>
/// <para>
/// <strong>The change</strong> is measured on the session's basis: the
/// previous close is multiplied by the factor of every action going ex after
/// the previous session and on or before this one, which is the exchange's own
/// reference-price rule. Later factors are not applied, because they rescale
/// both closes alike and cancel out of the ratio; leaving them out means the
/// close shown is the close the market printed that day. The factors are the
/// ones <see cref="CorporateActions.IPriceAdjustmentService"/> stored and the
/// bars endpoint applies, read for the current series — every factor this
/// system currently knows, which is what ADR-022 says a current read applies.
/// </para>
/// </remarks>
/// <param name="universes">The universe store, for the coverage claim.</param>
/// <param name="catalog">Answers who belonged, refusing what is not known.</param>
/// <param name="calendar">Venue trading calendars.</param>
/// <param name="reader">Bulk reads of instruments, closes and factors.</param>
/// <param name="providers">Says which sources adjust at source.</param>
/// <param name="clock">Today, when no session is named.</param>
/// <param name="logger">The log.</param>
internal sealed class MarketBreadthService(
    IUniverseRepository universes,
    IUniverseCatalog catalog,
    ITradingCalendar calendar,
    IBreadthReader reader,
    IMarketDataProviderRegistry providers,
    IClock clock,
    ILogger<MarketBreadthService> logger) : IMarketBreadthService
{
    /// <summary>
    /// How far back the previous trading day is looked for.
    /// </summary>
    /// <remarks>
    /// Tet closes Vietnamese venues for up to nine weekdays; a month is
    /// comfortably more than any closure on record, and a gap longer than that
    /// is a calendar problem the refusal should surface.
    /// </remarks>
    private const int PreviousSessionLookbackDays = 31;

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

    /// <summary>Decimal places a change is reported to.</summary>
    private const int ChangeScale = 6;

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

        var instruments = await reader
            .ListInstrumentsAsync(members, cancellationToken)
            .ConfigureAwait(false);

        var venues = await ResolveVenuesAsync(
                code, session, [.. instruments.Select(item => item.ExchangeId).Distinct()], cancellationToken)
            .ConfigureAwait(false);

        if (venues.Refusal is { } refusal)
        {
            return refusal;
        }

        var rows = await MeasureAsync(members, instruments, venues.Venues, session, cancellationToken)
            .ConfigureAwait(false);

        return BreadthResult.Computed(new MarketBreadth(
            code,
            session,
            defaulted,
            [.. venues.Venues.Values.OrderBy(venue => venue.Code.Value, StringComparer.Ordinal)],
            rows));
    }

    /// <summary>
    /// Checks the session against every member venue's calendar and finds the
    /// session before it on each.
    /// </summary>
    private async Task<VenueResolution> ResolveVenuesAsync(
        UniverseCode code,
        DateOnly session,
        IReadOnlyList<ExchangeId> venueIds,
        CancellationToken cancellationToken)
    {
        var coverage = (await calendar.ListCoverageAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(entry => entry.ExchangeId);

        var resolved = new Dictionary<ExchangeId, BreadthVenue>();

        foreach (var venueId in venueIds)
        {
            if (!coverage.TryGetValue(venueId, out var venue) || !venue.Covers(session))
            {
                return VenueResolution.Refused(BreadthResult.Refused(
                    BreadthOutcome.CalendarNotCovered,
                    code,
                    session,
                    $"The {Name(venue)} trading calendar is not transcribed for {Day(session)}, so whether "
                        + "it was a session cannot be said. Breadth is refused rather than guessed."));
            }

            var window = await calendar
                .LoadAsync(venueId, session.AddDays(-PreviousSessionLookbackDays), session, cancellationToken)
                .ConfigureAwait(false);

            if (!window.IsTradingDay(session))
            {
                return VenueResolution.Refused(BreadthResult.Refused(
                    BreadthOutcome.NotATradingDay,
                    code,
                    session,
                    $"{Day(session)} is not a trading day on {venue.Code} — a weekend or a recorded closure."));
            }

            var previous = window.TradingDays().Where(day => day < session).Select(day => (DateOnly?)day).LastOrDefault();

            if (previous is not { } before || !venue.Covers(before))
            {
                return VenueResolution.Refused(BreadthResult.Refused(
                    BreadthOutcome.CalendarNotCovered,
                    code,
                    session,
                    $"The {venue.Code} trading calendar is not transcribed for the session before "
                        + $"{Day(session)}, so there is no previous close to measure against."));
            }

            resolved[venueId] = new BreadthVenue(venue.Code, before);
        }

        return new VenueResolution(resolved, Refusal: null);
    }

    /// <summary>
    /// Reads both sessions' closes and the factors between them for every
    /// member, in two queries, and classifies each member.
    /// </summary>
    private async Task<IReadOnlyList<BreadthMember>> MeasureAsync(
        IReadOnlyList<InstrumentId> members,
        IReadOnlyList<BreadthInstrument> instruments,
        IReadOnlyDictionary<ExchangeId, BreadthVenue> venues,
        DateOnly session,
        CancellationToken cancellationToken)
    {
        var earliest = venues.Values.Min(venue => venue.PreviousSession);

        var closes = (await reader
                .ListDailyClosesAsync(members, earliest, session, cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(close => (close.InstrumentId, close.Session));

        var factors = (await reader
                .ListFactorsGoingExAsync(members, earliest.AddDays(1), session, cancellationToken)
                .ConfigureAwait(false))
            .ToLookup(factor => factor.InstrumentId);

        var byId = instruments.ToDictionary(instrument => instrument.InstrumentId);

        return
        [
            .. members
                .Select(id => byId.TryGetValue(id, out var instrument)
                    ? instrument
                    : throw new InvalidOperationException(
                        $"A membership names instrument {id}, which the instrument master does not hold."))
                .Select(instrument => Measure(
                    instrument, venues[instrument.ExchangeId], session, closes, factors[instrument.InstrumentId]))
                .OrderBy(member => member.Ticker.Value, StringComparer.Ordinal),
        ];
    }

    private BreadthMember Measure(
        BreadthInstrument instrument,
        BreadthVenue venue,
        DateOnly session,
        Dictionary<(InstrumentId InstrumentId, DateOnly Session), SessionClose> closes,
        IEnumerable<ExDateFactor> factors)
    {
        closes.TryGetValue((instrument.InstrumentId, session), out var today);
        closes.TryGetValue((instrument.InstrumentId, venue.PreviousSession), out var before);

        if (today is null || before is null)
        {
            return Row(instrument, venue, before, restated: null, today, BreadthCategory.NoData);
        }

        var factor = FactorBetween(before, today, factors);

        if (!factor.TryApply(before.Close, out var restated))
        {
            // Only reachable with a factor that rescales a price out of range,
            // which the engine refuses to store. Counted as no data rather than
            // as a move nobody can vouch for.
            return Row(instrument, venue, before, restated: null, today, BreadthCategory.NoData);
        }

        var category = today.Close.Value.CompareTo(restated.Value) switch
        {
            > 0 => BreadthCategory.Advancing,
            < 0 => BreadthCategory.Declining,
            _ => BreadthCategory.Unchanged,
        };

        return Row(instrument, venue, before, restated, today, category);
    }

    /// <summary>
    /// Combines the factors of every action going ex after the previous session
    /// and on or before this one.
    /// </summary>
    /// <remarks>
    /// The interval is <see cref="PriceAdjustment.AppliesTo"/>'s rule read from
    /// both ends: a factor applies to a bar that opened strictly before its
    /// ex-date, so it applies to the previous session's bar and not to this
    /// one's exactly when <c>previous &lt; exDate &lt;= session</c>. A bar from
    /// a source that adjusts at source is already on today's basis, and
    /// adjusting it again would be wrong by the factor — the same rule the bars
    /// endpoint follows.
    /// </remarks>
    private AdjustmentFactor FactorBetween(
        SessionClose before,
        SessionClose today,
        IEnumerable<ExDateFactor> factors)
    {
        if (AdjustsAtSource(before.Source) || AdjustsAtSource(today.Source))
        {
            return AdjustmentFactor.Identity;
        }

        return factors
            .Where(factor => factor.ExDate > before.Session && factor.ExDate <= today.Session)
            .Aggregate(AdjustmentFactor.Identity, (combined, factor) => combined.Combine(factor.Factor));
    }

    private bool AdjustsAtSource(SourceCode source) =>
        providers.TryResolve(source, out var provider)
        && provider.Capability.Limitations.AdjustsPricesAtSource;

    private static BreadthMember Row(
        BreadthInstrument instrument,
        BreadthVenue venue,
        SessionClose? before,
        Price? restated,
        SessionClose? today,
        BreadthCategory category) =>
        new(
            instrument.InstrumentId,
            instrument.Ticker,
            venue.Code,
            venue.PreviousSession,
            before?.Close.Value,
            restated?.Value,
            today?.Close.Value,
            restated is not null && today is not null
                ? decimal.Round(
                    (today.Close.Value / restated.Value) - 1m, ChangeScale, MidpointRounding.ToEven)
                : null,
            today?.Volume,
            category);

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

    private static string Name(VenueCalendarCoverage? venue) =>
        venue?.Code.Value ?? "venue's";

    private static string Day(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>The venues resolved, or why they could not be.</summary>
    private sealed record VenueResolution(
        IReadOnlyDictionary<ExchangeId, BreadthVenue> Venues,
        BreadthResult? Refusal)
    {
        public static VenueResolution Refused(BreadthResult refusal) =>
            new(new Dictionary<ExchangeId, BreadthVenue>(), refusal);
    }

    /// <summary>The session chosen by default, or why none could be.</summary>
    private sealed record SessionChoice(DateOnly Session, BreadthResult? Refusal)
    {
        public static SessionChoice Refused(BreadthResult refusal) => new(default, refusal);
    }
}
