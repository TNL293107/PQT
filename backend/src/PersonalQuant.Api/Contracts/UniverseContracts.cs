using PersonalQuant.Application.Breadth;
using PersonalQuant.Application.Universes;

namespace PersonalQuant.Api.Contracts;

/// <summary>
/// One universe on the wire.
/// </summary>
/// <param name="Code">The short code, such as <c>VN30</c>.</param>
/// <param name="Name">The full name.</param>
/// <param name="Kind">What kind of set it is: <c>Index</c>, <c>Exchange</c> or <c>Custom</c>.</param>
/// <param name="CoverageFrom">
/// The first date whose membership is claimed to be known, or null when the
/// universe claims nothing.
/// </param>
/// <param name="CoverageUntil">
/// The first date no longer claimed — exclusive — or null while the claim runs
/// on (or when there is no claim at all; read <paramref name="CoverageFrom"/>).
/// </param>
/// <param name="MemberCount">
/// Members today, or null when the claim does not reach today. Null is not
/// zero.
/// </param>
public sealed record UniverseResponse(
    string Code,
    string Name,
    string Kind,
    DateOnly? CoverageFrom,
    DateOnly? CoverageUntil,
    int? MemberCount)
{
    /// <summary>Projects a universe onto the wire contract.</summary>
    /// <param name="summary">The universe.</param>
    /// <returns>The response representation.</returns>
    public static UniverseResponse From(UniverseSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        return new UniverseResponse(
            summary.Code.Value,
            summary.Name,
            summary.Kind.ToString(),
            summary.Coverage?.From,
            summary.Coverage?.Until,
            summary.MemberCount);
    }
}

/// <summary>
/// Every universe this deployment defines.
/// </summary>
/// <param name="AsOf">The date the member counts were read as of.</param>
/// <param name="Count">How many universes are listed.</param>
/// <param name="Universes">The universes, by code.</param>
public sealed record UniverseListResponse(
    DateOnly AsOf,
    int Count,
    IReadOnlyList<UniverseResponse> Universes);

/// <summary>
/// A venue the members trade on, and its session before this one.
/// </summary>
/// <param name="Code">The venue's code.</param>
/// <param name="PreviousSession">Its previous trading day.</param>
public sealed record BreadthVenueResponse(string Code, DateOnly PreviousSession);

/// <summary>
/// One member's move on the session.
/// </summary>
/// <param name="InstrumentId">The canonical identifier.</param>
/// <param name="Ticker">The ticker it trades under now.</param>
/// <param name="Venue">Its venue.</param>
/// <param name="PreviousSession">The session its close is measured against.</param>
/// <param name="RawPreviousClose">The previous close as printed, when there is a bar.</param>
/// <param name="PreviousClose">
/// The previous close restated for actions going ex on the session — the price
/// the change is measured from — when both bars exist.
/// </param>
/// <param name="Close">The session's close, when there is a bar.</param>
/// <param name="Change">The fractional change (0.012 is 1.2%), when both bars exist.</param>
/// <param name="Volume">The session's volume, when there is a bar.</param>
/// <param name="Category"><c>Advancing</c>, <c>Declining</c>, <c>Unchanged</c> or <c>NoData</c>.</param>
/// <param name="AdjustedForAction">Whether an action going ex on the session restated the previous close.</param>
public sealed record BreadthMemberResponse(
    Guid InstrumentId,
    string Ticker,
    string Venue,
    DateOnly PreviousSession,
    decimal? RawPreviousClose,
    decimal? PreviousClose,
    decimal? Close,
    decimal? Change,
    long? Volume,
    string Category,
    bool AdjustedForAction)
{
    /// <summary>Projects a member onto the wire contract.</summary>
    /// <param name="member">The member.</param>
    /// <returns>The response representation.</returns>
    public static BreadthMemberResponse From(BreadthMember member)
    {
        ArgumentNullException.ThrowIfNull(member);

        return new BreadthMemberResponse(
            member.InstrumentId.Value,
            member.Ticker.Value,
            member.Venue.Value,
            member.PreviousSession,
            member.RawPreviousClose,
            member.PreviousClose,
            member.Close,
            member.Change,
            member.Volume,
            member.Category.ToString(),
            member.AdjustedForAction);
    }
}

/// <summary>
/// Market breadth for one universe on one session.
/// </summary>
/// <remarks>
/// The basis is stated on the response rather than left to the documentation:
/// the closes are the current series, and the previous close is restated for
/// actions going ex on the session, so an ex-dividend gap does not read as a
/// decline (ADR-026). <c>knownAsOf</c> is always null here and the policy is
/// always <c>permissive</c>, because a current read applies every factor this
/// system knows (ADR-022); they are on the wire so a reader does not have to
/// know that.
/// </remarks>
/// <param name="Universe">The universe's code.</param>
/// <param name="Session">The session counted.</param>
/// <param name="SessionWasDefaulted">
/// Whether no session was asked for and this is the latest one any member has
/// a bar for.
/// </param>
/// <param name="KnownAsOf">Always null: breadth reads the current series.</param>
/// <param name="AnnouncementPolicy">Always <c>permissive</c>.</param>
/// <param name="MemberCount">How many members the universe had that day.</param>
/// <param name="Advancing">Members that closed above their restated previous close.</param>
/// <param name="Declining">Members that closed below it.</param>
/// <param name="Unchanged">Members that closed exactly at it.</param>
/// <param name="NoData">Members without a bar on the session or the previous one.</param>
/// <param name="AdvancingVolume">The advancers' session volume.</param>
/// <param name="DecliningVolume">The decliners' session volume.</param>
/// <param name="UnchangedVolume">The unchanged members' session volume.</param>
/// <param name="Venues">The venues the members trade on.</param>
/// <param name="Members">Every member, by ticker.</param>
public sealed record BreadthResponse(
    string Universe,
    DateOnly Session,
    bool SessionWasDefaulted,
    DateTimeOffset? KnownAsOf,
    string AnnouncementPolicy,
    int MemberCount,
    int Advancing,
    int Declining,
    int Unchanged,
    int NoData,
    long AdvancingVolume,
    long DecliningVolume,
    long UnchangedVolume,
    IReadOnlyList<BreadthVenueResponse> Venues,
    IReadOnlyList<BreadthMemberResponse> Members)
{
    /// <summary>Projects a breadth count onto the wire contract.</summary>
    /// <param name="breadth">The count.</param>
    /// <returns>The response representation.</returns>
    public static BreadthResponse From(MarketBreadth breadth)
    {
        ArgumentNullException.ThrowIfNull(breadth);

        return new BreadthResponse(
            breadth.Code.Value,
            breadth.Session,
            breadth.SessionWasDefaulted,
            KnownAsOf: null,
            AnnouncementPolicy: "permissive",
            breadth.Members.Count,
            breadth.Advancing,
            breadth.Declining,
            breadth.Unchanged,
            breadth.NoData,
            breadth.AdvancingVolume,
            breadth.DecliningVolume,
            breadth.UnchangedVolume,
            [.. breadth.Venues.Select(venue => new BreadthVenueResponse(venue.Code.Value, venue.PreviousSession))],
            [.. breadth.Members.Select(BreadthMemberResponse.From)]);
    }
}
