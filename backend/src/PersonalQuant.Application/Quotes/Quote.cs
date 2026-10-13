using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.Application.Quotes;

/// <summary>
/// Which way an instrument moved on a session.
/// </summary>
/// <remarks>
/// Values are explicit because they cross the wire by name and are asserted
/// on by tests; reordering the declaration must not renumber them.
/// </remarks>
public enum PriceMove
{
    /// <summary>Closed above its reference.</summary>
    Advancing = 1,

    /// <summary>Closed below its reference.</summary>
    Declining = 2,

    /// <summary>Closed exactly at its reference.</summary>
    Unchanged = 3,

    /// <summary>
    /// Has no daily bar on the session, or none on the previous one.
    /// </summary>
    /// <remarks>
    /// Never folded into <see cref="Unchanged"/>. A suspended name and a name
    /// nobody ingested both lack a bar, and calling either "did not move" is a
    /// claim about the market that the data does not make.
    /// </remarks>
    NoData = 4,
}

/// <summary>
/// One instrument on a session: what it closed at, against what, and which way
/// that moved.
/// </summary>
/// <param name="InstrumentId">The canonical identifier.</param>
/// <param name="Ticker">The ticker it trades under now.</param>
/// <param name="Venue">The venue whose calendar chose its previous session.</param>
/// <param name="PreviousSession">The venue's trading day before the session.</param>
/// <param name="RawPreviousClose">
/// The previous session's close as printed, or null when there is no bar.
/// </param>
/// <param name="Reference">
/// The price the move is measured from: the previous close restated for every
/// action going ex on the session and, for an equity, rounded to the venue's
/// tick as the exchange rounds its reference. Null when either bar is missing.
/// Equal to <paramref name="RawPreviousClose"/> on an ordinary day.
/// </param>
/// <param name="Close">The session's close, or null when there is no bar.</param>
/// <param name="Change">
/// <c>Close / Reference − 1</c>, or null when either bar is missing.
/// </param>
/// <param name="Volume">The session's volume, or null when there is no bar.</param>
/// <param name="Move">Which way it moved.</param>
public sealed record Quote(
    InstrumentId InstrumentId,
    Ticker Ticker,
    ExchangeCode Venue,
    DateOnly PreviousSession,
    decimal? RawPreviousClose,
    decimal? Reference,
    decimal? Close,
    decimal? Change,
    long? Volume,
    PriceMove Move)
{
    /// <summary>
    /// Gets a value indicating whether an action going ex on the session
    /// restated the previous close.
    /// </summary>
    public bool AdjustedForAction =>
        RawPreviousClose is { } raw && Reference is { } reference && raw != reference;
}

/// <summary>
/// A venue the quoted instruments trade on, and the session before this one there.
/// </summary>
/// <param name="Code">The venue.</param>
/// <param name="PreviousSession">Its trading day before the session.</param>
public sealed record QuoteVenue(ExchangeCode Code, DateOnly PreviousSession);

/// <summary>
/// How a request for quotes ended.
/// </summary>
public enum QuoteOutcome
{
    /// <summary>The instruments were quoted.</summary>
    Quoted = 1,

    /// <summary>
    /// A venue's calendar was not transcribed for the session or the session
    /// before it, so neither "it was a trading day" nor "this was the previous
    /// one" can be said.
    /// </summary>
    CalendarNotCovered = 2,

    /// <summary>The session is not a trading day on an instrument's venue.</summary>
    NotATradingDay = 3,

    /// <summary>
    /// No session was named, and no instrument has a recent bar to default to.
    /// </summary>
    NoData = 4,
}

/// <summary>
/// Quotes for a set of instruments on one session, or the stated reason there
/// are none.
/// </summary>
/// <remarks>
/// One session for the whole set, never a session per instrument. A board that
/// put a suspended name's close from last month beside today's closes would
/// read as a market nobody saw; here that name is <see cref="PriceMove.NoData"/>
/// on the day everyone else traded.
/// </remarks>
public sealed record QuoteBoard
{
    private QuoteBoard(
        QuoteOutcome outcome,
        DateOnly? session,
        bool sessionWasDefaulted,
        IReadOnlyList<QuoteVenue> venues,
        IReadOnlyList<Quote> quotes,
        string detail)
    {
        Outcome = outcome;
        Session = session;
        SessionWasDefaulted = sessionWasDefaulted;
        Venues = venues;
        Quotes = quotes;
        Detail = detail;
    }

    /// <summary>Gets how the request ended.</summary>
    public QuoteOutcome Outcome { get; }

    /// <summary>
    /// Gets the session quoted, asked about or chosen; null only when nothing
    /// was named and there was nothing to quote.
    /// </summary>
    public DateOnly? Session { get; }

    /// <summary>
    /// Gets a value indicating whether the caller named no session and this is
    /// the latest one any of the instruments has a bar for.
    /// </summary>
    public bool SessionWasDefaulted { get; }

    /// <summary>Gets the venues the instruments trade on, by code.</summary>
    public IReadOnlyList<QuoteVenue> Venues { get; }

    /// <summary>Gets one quote per instrument, by ticker; empty unless quoted.</summary>
    public IReadOnlyList<Quote> Quotes { get; }

    /// <summary>Gets a sentence fit to show an operator, saying what happened.</summary>
    public string Detail { get; }

    /// <summary>Records quotes.</summary>
    /// <param name="session">The session, or null for an empty set with nothing named.</param>
    /// <param name="defaulted">Whether the session was chosen rather than named.</param>
    /// <param name="venues">The venues.</param>
    /// <param name="quotes">The quotes.</param>
    /// <returns>The board.</returns>
    public static QuoteBoard Quoted(
        DateOnly? session,
        bool defaulted,
        IReadOnlyList<QuoteVenue> venues,
        IReadOnlyList<Quote> quotes)
    {
        ArgumentNullException.ThrowIfNull(venues);
        ArgumentNullException.ThrowIfNull(quotes);

        var detail = session is { } day ? $"Quoted on {day:yyyy-MM-dd}." : "Nothing to quote.";

        return new QuoteBoard(QuoteOutcome.Quoted, session, defaulted, venues, quotes, detail);
    }

    /// <summary>Records a refusal.</summary>
    /// <param name="outcome">Why. Must not be <see cref="QuoteOutcome.Quoted"/>.</param>
    /// <param name="session">The session, when one was named or chosen.</param>
    /// <param name="detail">What to tell the operator.</param>
    /// <returns>The board.</returns>
    public static QuoteBoard Refused(QuoteOutcome outcome, DateOnly? session, string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);

        if (outcome == QuoteOutcome.Quoted)
        {
            throw new ArgumentOutOfRangeException(
                nameof(outcome), "A refusal needs a reason other than success.");
        }

        return new QuoteBoard(outcome, session, sessionWasDefaulted: false, [], [], detail);
    }
}
