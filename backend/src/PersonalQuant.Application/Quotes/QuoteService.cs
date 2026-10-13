using System.Globalization;
using PersonalQuant.Application.Abstractions;
using PersonalQuant.Application.Exchanges;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Domain.CorporateActions;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Application.Quotes;

/// <summary>
/// Quotes a set of instruments on a session: close, reference, change, volume.
/// </summary>
/// <remarks>
/// The one implementation of "how did it move that day" in the system. Market
/// breadth counts these quotes and a watchlist shows them, so the two cannot
/// disagree about a name on the same session (ADR-026).
/// </remarks>
public interface IQuoteService
{
    /// <summary>
    /// Quotes every instrument named on one session.
    /// </summary>
    /// <param name="instruments">The instruments; every one must exist.</param>
    /// <param name="session">
    /// The session, or null for the latest session up to today on which any of
    /// them has a daily bar.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The quotes, or the reason there are none.</returns>
    Task<QuoteBoard> QuoteAsync(
        IReadOnlyCollection<InstrumentId> instruments,
        DateOnly? session,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="IQuoteService"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The previous session</strong> is each instrument's own venue's
/// previous trading day: instruments on different venues are supported, and the
/// session must be a trading day on every venue any of them trades on, because
/// a board that mixed one venue's session with another's holiday would compare
/// closes from different days.
/// </para>
/// <para>
/// <strong>The reference</strong> is the exchange's reference-price rule. The
/// previous close is multiplied by the factor of every action going ex after the
/// previous session and on or before this one, and when that restated it, an
/// equity's result is rounded to the venue's tick (<see cref="TickSchedule"/>),
/// because the exchange publishes a quotable reference and a close exactly at it
/// is not a move. Later factors are not applied: they rescale both closes alike
/// and cancel out of the ratio, and leaving them out means the close shown is
/// the close the market printed that day. The factors are the ones
/// <see cref="CorporateActions.IPriceAdjustmentService"/> stored and the bars
/// endpoint applies, read for the current series — every factor this system
/// currently knows, which is what ADR-022 says a current read applies.
/// </para>
/// </remarks>
/// <param name="calendar">Venue trading calendars.</param>
/// <param name="reader">Bulk reads of instruments, closes and factors.</param>
/// <param name="history">Says which venue each instrument traded on that session.</param>
/// <param name="providers">Says which sources adjust at source.</param>
/// <param name="clock">Today, when no session is named.</param>
internal sealed class QuoteService(
    ITradingCalendar calendar,
    IQuoteReader reader,
    IVenueHistoryRepository history,
    IMarketDataProviderRegistry providers,
    IClock clock) : IQuoteService
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
    /// Instruments whose newest bar is older than a month are not being
    /// ingested, and "the latest session" would quietly answer about a market
    /// weeks out of date. Refusing says so; naming the session still reaches it.
    /// </remarks>
    private const int DefaultSessionLookbackDays = 31;

    /// <summary>Decimal places a change is reported to.</summary>
    private const int ChangeScale = 6;

    /// <inheritdoc />
    public async Task<QuoteBoard> QuoteAsync(
        IReadOnlyCollection<InstrumentId> instruments,
        DateOnly? session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instruments);

        var ids = instruments.Distinct().ToList();

        if (ids.Count == 0)
        {
            return QuoteBoard.Quoted(session, defaulted: false, [], []);
        }

        var defaulted = session is null;

        if (session is not { } day)
        {
            var latest = await LatestSessionAsync(ids, cancellationToken).ConfigureAwait(false);

            if (latest.Session is not { } chosen)
            {
                return QuoteBoard.Refused(
                    QuoteOutcome.NoData,
                    null,
                    $"None of them has a daily bar between {Day(latest.From)} and {Day(latest.To)}. "
                        + "Name a session to quote an older one.");
            }

            day = chosen;
        }

        var listed = await reader.ListInstrumentsAsync(ids, cancellationToken).ConfigureAwait(false);
        var byId = listed.ToDictionary(instrument => instrument.InstrumentId);

        var current = ids
            .Select(id => byId.TryGetValue(id, out var instrument)
                ? instrument
                : throw new InvalidOperationException(
                    $"Instrument {id} was asked to be quoted, and the instrument master does not hold it."))
            .ToList();

        // Each instrument on the venue it traded on that session, by its
        // recorded venue history: its previous session, its calendar and its
        // tick are that venue's.
        var timelines = await VenueTimelines
            .LoadAsync(history, current.ToDictionary(item => item.InstrumentId, item => item.ExchangeId), cancellationToken)
            .ConfigureAwait(false);

        var named = current
            .Select(item => item with { ExchangeId = timelines[item.InstrumentId].On(day) })
            .ToList();

        var venues = await ResolveVenuesAsync(
                day, [.. named.Select(item => item.ExchangeId).Distinct()], cancellationToken)
            .ConfigureAwait(false);

        if (venues.Refusal is { } refusal)
        {
            return refusal;
        }

        var quotes = await MeasureAsync(named, venues.Venues, day, cancellationToken).ConfigureAwait(false);

        return QuoteBoard.Quoted(
            day,
            defaulted,
            [.. venues.Venues.Values.OrderBy(venue => venue.Code.Value, StringComparer.Ordinal)],
            quotes);
    }

    /// <summary>
    /// Finds the latest session up to today on which any of the instruments has
    /// a daily bar.
    /// </summary>
    private async Task<(DateOnly? Session, DateOnly From, DateOnly To)> LatestSessionAsync(
        IReadOnlyCollection<InstrumentId> ids,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var from = today.AddDays(-DefaultSessionLookbackDays);

        var closes = await reader.ListDailyClosesAsync(ids, from, today, cancellationToken).ConfigureAwait(false);

        return (closes.Select(close => (DateOnly?)close.Session).Max(), from, today);
    }

    /// <summary>
    /// Checks the session against every venue's calendar and finds the session
    /// before it on each.
    /// </summary>
    private async Task<VenueResolution> ResolveVenuesAsync(
        DateOnly session,
        IReadOnlyList<ExchangeId> venueIds,
        CancellationToken cancellationToken)
    {
        var coverage = (await calendar.ListCoverageAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(entry => entry.ExchangeId);

        var resolved = new Dictionary<ExchangeId, QuoteVenue>();

        foreach (var venueId in venueIds)
        {
            if (!coverage.TryGetValue(venueId, out var venue) || !venue.Covers(session))
            {
                return VenueResolution.Refused(QuoteBoard.Refused(
                    QuoteOutcome.CalendarNotCovered,
                    session,
                    $"The {Name(venue)} trading calendar is not transcribed for {Day(session)}, so whether "
                        + "it was a session cannot be said. It is refused rather than guessed."));
            }

            var window = await calendar
                .LoadAsync(venueId, session.AddDays(-PreviousSessionLookbackDays), session, cancellationToken)
                .ConfigureAwait(false);

            if (!window.IsTradingDay(session))
            {
                return VenueResolution.Refused(QuoteBoard.Refused(
                    QuoteOutcome.NotATradingDay,
                    session,
                    $"{Day(session)} is not a trading day on {venue.Code} — a weekend or a recorded closure."));
            }

            var previous = window.TradingDays().Where(day => day < session).Select(day => (DateOnly?)day).LastOrDefault();

            if (previous is not { } before || !venue.Covers(before))
            {
                return VenueResolution.Refused(QuoteBoard.Refused(
                    QuoteOutcome.CalendarNotCovered,
                    session,
                    $"The {venue.Code} trading calendar is not transcribed for the session before "
                        + $"{Day(session)}, so there is no previous close to measure against."));
            }

            resolved[venueId] = new QuoteVenue(venue.Code, before);
        }

        return new VenueResolution(resolved, Refusal: null);
    }

    /// <summary>
    /// Reads both sessions' closes and the factors between them for every
    /// instrument, in two queries, and quotes each.
    /// </summary>
    private async Task<IReadOnlyList<Quote>> MeasureAsync(
        IReadOnlyList<QuoteInstrument> instruments,
        IReadOnlyDictionary<ExchangeId, QuoteVenue> venues,
        DateOnly session,
        CancellationToken cancellationToken)
    {
        var ids = instruments.Select(instrument => instrument.InstrumentId).ToList();
        var earliest = venues.Values.Min(venue => venue.PreviousSession);

        var closes = (await reader
                .ListDailyClosesAsync(ids, earliest, session, cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(close => (close.InstrumentId, close.Session));

        var factors = (await reader
                .ListFactorsGoingExAsync(ids, earliest.AddDays(1), session, cancellationToken)
                .ConfigureAwait(false))
            .ToLookup(factor => factor.InstrumentId);

        return
        [
            .. instruments
                .Select(instrument => Measure(
                    instrument, venues[instrument.ExchangeId], session, closes, factors[instrument.InstrumentId]))
                .OrderBy(quote => quote.Ticker.Value, StringComparer.Ordinal),
        ];
    }

    private Quote Measure(
        QuoteInstrument instrument,
        QuoteVenue venue,
        DateOnly session,
        Dictionary<(InstrumentId InstrumentId, DateOnly Session), SessionClose> closes,
        IEnumerable<ExDateFactor> factors)
    {
        closes.TryGetValue((instrument.InstrumentId, session), out var today);
        closes.TryGetValue((instrument.InstrumentId, venue.PreviousSession), out var before);

        if (today is null || before is null)
        {
            return Row(instrument, venue, before, reference: null, today, PriceMove.NoData);
        }

        var factor = FactorBetween(before, today, factors);

        if (!factor.TryApply(before.Close, out var restated))
        {
            // Only reachable with a factor that rescales a price out of range,
            // which the engine refuses to store. Quoted as no data rather than
            // as a move nobody can vouch for.
            return Row(instrument, venue, before, reference: null, today, PriceMove.NoData);
        }

        var reference = factor.IsIdentity
            ? before.Close.Value
            : OnTick(instrument, venue, restated.Value);

        var move = today.Close.Value.CompareTo(reference) switch
        {
            > 0 => PriceMove.Advancing,
            < 0 => PriceMove.Declining,
            _ => PriceMove.Unchanged,
        };

        return Row(instrument, venue, before, reference, today, move);
    }

    /// <summary>
    /// Rounds a restated reference to the tick the exchange would publish it on.
    /// </summary>
    /// <remarks>
    /// Only for an equity on a venue whose schedule is known. An ETF, an index
    /// or an unknown venue keeps the unrounded restatement: an exact figure the
    /// exchange might round is a smaller error than a rounding it would not do.
    /// </remarks>
    private static decimal OnTick(QuoteInstrument instrument, QuoteVenue venue, decimal restated) =>
        instrument.AssetType == AssetType.Equity && TickSchedule.ForEquitiesOn(venue.Code) is { } ticks
            ? ticks.RoundToTick(restated)
            : restated;

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

    private static Quote Row(
        QuoteInstrument instrument,
        QuoteVenue venue,
        SessionClose? before,
        decimal? reference,
        SessionClose? today,
        PriceMove move) =>
        new(
            instrument.InstrumentId,
            instrument.Ticker,
            venue.Code,
            venue.PreviousSession,
            before?.Close.Value,
            reference,
            today?.Close.Value,
            reference is { } basis && today is not null
                ? decimal.Round((today.Close.Value / basis) - 1m, ChangeScale, MidpointRounding.ToEven)
                : null,
            today?.Volume,
            move);

    private static string Name(VenueCalendarCoverage? venue) =>
        venue?.Code.Value ?? "venue's";

    private static string Day(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>The venues resolved, or why they could not be.</summary>
    private sealed record VenueResolution(
        IReadOnlyDictionary<ExchangeId, QuoteVenue> Venues,
        QuoteBoard? Refusal)
    {
        public static VenueResolution Refused(QuoteBoard refusal) =>
            new(new Dictionary<ExchangeId, QuoteVenue>(), refusal);
    }
}
