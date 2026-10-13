using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.Universes;

namespace PersonalQuant.Application.Breadth;

/// <summary>
/// How one member moved on a session.
/// </summary>
/// <remarks>
/// Values are explicit because they cross the wire by name and are asserted
/// on by tests; reordering the declaration must not renumber them.
/// </remarks>
public enum BreadthCategory
{
    /// <summary>Closed above its previous close, on adjusted prices.</summary>
    Advancing = 1,

    /// <summary>Closed below its previous close, on adjusted prices.</summary>
    Declining = 2,

    /// <summary>Closed exactly at its previous close, on adjusted prices.</summary>
    Unchanged = 3,

    /// <summary>
    /// Has no daily bar on the session, or none on the previous one.
    /// </summary>
    /// <remarks>
    /// Never folded into <see cref="Unchanged"/>. A suspended name and a name
    /// nobody ingested both lack a bar, and counting either as "did not move"
    /// is a claim about the market that the data does not make.
    /// </remarks>
    NoData = 4,
}

/// <summary>
/// One member of a universe on a session: what it closed at, against what, and
/// which way that counts.
/// </summary>
/// <param name="InstrumentId">The canonical identifier.</param>
/// <param name="Ticker">The ticker it trades under now.</param>
/// <param name="Venue">The venue whose calendar chose its previous session.</param>
/// <param name="PreviousSession">The venue's trading day before the session.</param>
/// <param name="RawPreviousClose">
/// The previous session's close as printed, or null when there is no bar.
/// </param>
/// <param name="PreviousClose">
/// The previous close restated onto the session's basis — multiplied by the
/// factor of every action going ex on the session and, for an equity, rounded
/// to the venue's tick as the exchange's reference is — or null when either bar
/// is missing. Equal to <paramref name="RawPreviousClose"/> on an ordinary day.
/// The member's <see cref="Quotes.Quote.Reference"/>.
/// </param>
/// <param name="Close">The session's close, or null when there is no bar.</param>
/// <param name="Change">
/// <c>Close / PreviousClose − 1</c>, or null when either bar is missing.
/// </param>
/// <param name="Volume">The session's volume, or null when there is no bar.</param>
/// <param name="Category">Which way it counts.</param>
public sealed record BreadthMember(
    InstrumentId InstrumentId,
    Ticker Ticker,
    ExchangeCode Venue,
    DateOnly PreviousSession,
    decimal? RawPreviousClose,
    decimal? PreviousClose,
    decimal? Close,
    decimal? Change,
    long? Volume,
    BreadthCategory Category)
{
    /// <summary>
    /// Gets a value indicating whether an action going ex on the session
    /// restated the previous close.
    /// </summary>
    public bool AdjustedForAction =>
        RawPreviousClose is { } raw && PreviousClose is { } adjusted && raw != adjusted;
}

/// <summary>
/// A venue the members trade on, and the session before this one there.
/// </summary>
/// <param name="Code">The venue.</param>
/// <param name="PreviousSession">Its trading day before the session.</param>
public sealed record BreadthVenue(ExchangeCode Code, DateOnly PreviousSession);

/// <summary>What a breadth count is over.</summary>
public enum BreadthScopeKind
{
    /// <summary>A universe's point-in-time members.</summary>
    Universe = 1,

    /// <summary>Every equity listed on a venue that session.</summary>
    Venue = 2,
}

/// <summary>
/// The set a breadth count is over: a universe or a venue, by code.
/// </summary>
/// <param name="Kind">Universe or venue.</param>
/// <param name="Value">The universe's or the venue's code.</param>
public sealed record BreadthScope(BreadthScopeKind Kind, string Value)
{
    /// <summary>A universe's members.</summary>
    /// <param name="code">The universe.</param>
    /// <returns>The scope.</returns>
    public static BreadthScope Of(UniverseCode code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return new BreadthScope(BreadthScopeKind.Universe, code.Value);
    }

    /// <summary>A venue's listed equities.</summary>
    /// <param name="code">The venue.</param>
    /// <returns>The scope.</returns>
    public static BreadthScope Of(ExchangeCode code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return new BreadthScope(BreadthScopeKind.Venue, code.Value);
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>
/// What a venue-wide count can and cannot say about its own member set.
/// </summary>
/// <remarks>
/// A universe's members are sourced; a venue's are the equities this system's
/// instrument master holds on it, which is not every name the exchange lists.
/// Two gaps are stated rather than hidden: names whose listing date nobody
/// recorded, counted as listed because their record says they are, and names
/// with no daily bar on any session, which can only ever be no data.
/// </remarks>
/// <param name="WithoutListingDate">Members whose listing date is not recorded.</param>
/// <param name="NeverIngested">Members with no daily bar on any session, by ticker.</param>
public sealed record VenueListing(int WithoutListingDate, IReadOnlyList<Ticker> NeverIngested);

/// <summary>
/// Advancers, decliners and the rest, for one universe or venue on one session.
/// </summary>
/// <remarks>
/// The counts are derived from <see cref="Members"/> rather than stored beside
/// them, so a summary line and the rows under it cannot disagree.
/// </remarks>
/// <param name="Scope">The universe or venue counted.</param>
/// <param name="Session">The session counted.</param>
/// <param name="SessionWasDefaulted">
/// Whether the caller named no session and this is the latest one any member
/// has a bar for. Said, because "today" and "the last day we hold" are
/// different answers to the same request.
/// </param>
/// <param name="Venues">The venues the members trade on, by code.</param>
/// <param name="Members">Every member that day, by ticker.</param>
/// <param name="Listing">For a venue, what its member set leaves out; null for a universe.</param>
public sealed record MarketBreadth(
    BreadthScope Scope,
    DateOnly Session,
    bool SessionWasDefaulted,
    IReadOnlyList<BreadthVenue> Venues,
    IReadOnlyList<BreadthMember> Members,
    VenueListing? Listing = null)
{
    /// <summary>Gets how many members advanced.</summary>
    public int Advancing => Count(BreadthCategory.Advancing);

    /// <summary>Gets how many members declined.</summary>
    public int Declining => Count(BreadthCategory.Declining);

    /// <summary>Gets how many members closed unchanged.</summary>
    public int Unchanged => Count(BreadthCategory.Unchanged);

    /// <summary>Gets how many members could not be counted for want of a bar.</summary>
    public int NoData => Count(BreadthCategory.NoData);

    /// <summary>Gets the session volume of the members that advanced.</summary>
    public long AdvancingVolume => Volume(BreadthCategory.Advancing);

    /// <summary>Gets the session volume of the members that declined.</summary>
    public long DecliningVolume => Volume(BreadthCategory.Declining);

    /// <summary>Gets the session volume of the members that closed unchanged.</summary>
    public long UnchangedVolume => Volume(BreadthCategory.Unchanged);

    private int Count(BreadthCategory category) =>
        Members.Count(member => member.Category == category);

    private long Volume(BreadthCategory category) =>
        Members.Where(member => member.Category == category).Sum(member => member.Volume ?? 0);
}

/// <summary>
/// Why a breadth count was not given.
/// </summary>
public enum BreadthOutcome
{
    /// <summary>The count was computed.</summary>
    Computed = 1,

    /// <summary>No universe is defined under that code.</summary>
    UnknownUniverse = 2,

    /// <summary>
    /// The universe does not claim to know who belonged to it that day, so
    /// there is no member set to count.
    /// </summary>
    MembershipUnknown = 3,

    /// <summary>
    /// A member's venue calendar was not transcribed for the session or the
    /// session before it, so neither "it was a trading day" nor "this was the
    /// previous one" can be said.
    /// </summary>
    CalendarNotCovered = 4,

    /// <summary>The session is not a trading day on a member's venue.</summary>
    NotATradingDay = 5,

    /// <summary>
    /// No session was named, and no member has a recent bar to default to.
    /// </summary>
    NoData = 6,

    /// <summary>No venue is registered under that code.</summary>
    UnknownVenue = 7,
}

/// <summary>
/// A breadth count, or the stated reason there is none.
/// </summary>
/// <remarks>
/// A result rather than an exception, for the reason
/// <see cref="Universes.UniverseConstituents"/> gives: refusing is an ordinary
/// answer to an ordinary question, and every interface — API, CLI, console —
/// has to render each refusal differently from the others.
/// </remarks>
public sealed record BreadthResult
{
    private BreadthResult(
        BreadthOutcome outcome,
        BreadthScope scope,
        DateOnly? session,
        MarketBreadth? breadth,
        string detail)
    {
        Outcome = outcome;
        Scope = scope;
        Session = session;
        Breadth = breadth;
        Detail = detail;
    }

    /// <summary>Gets how the request ended.</summary>
    public BreadthOutcome Outcome { get; }

    /// <summary>Gets the universe or venue asked about.</summary>
    public BreadthScope Scope { get; }

    /// <summary>Gets the session asked about or chosen, when there is one.</summary>
    public DateOnly? Session { get; }

    /// <summary>Gets the count, when it was computed.</summary>
    public MarketBreadth? Breadth { get; }

    /// <summary>Gets a sentence fit to show an operator, saying what happened.</summary>
    public string Detail { get; }

    /// <summary>Records a computed count.</summary>
    /// <param name="breadth">The count.</param>
    /// <returns>The result.</returns>
    public static BreadthResult Computed(MarketBreadth breadth)
    {
        ArgumentNullException.ThrowIfNull(breadth);

        return new BreadthResult(
            BreadthOutcome.Computed,
            breadth.Scope,
            breadth.Session,
            breadth,
            $"{breadth.Scope} on {breadth.Session:yyyy-MM-dd}.");
    }

    /// <summary>Records a refusal.</summary>
    /// <param name="outcome">Why. Must not be <see cref="BreadthOutcome.Computed"/>.</param>
    /// <param name="scope">The universe or venue asked about.</param>
    /// <param name="session">The session, when one was named or chosen.</param>
    /// <param name="detail">What to tell the operator.</param>
    /// <returns>The result.</returns>
    public static BreadthResult Refused(
        BreadthOutcome outcome,
        BreadthScope scope,
        DateOnly? session,
        string detail)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);

        if (outcome == BreadthOutcome.Computed)
        {
            throw new ArgumentOutOfRangeException(
                nameof(outcome), "A refusal needs a reason other than success.");
        }

        return new BreadthResult(outcome, scope, session, breadth: null, detail);
    }
}
