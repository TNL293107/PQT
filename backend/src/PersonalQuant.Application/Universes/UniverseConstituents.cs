using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.Universes;

namespace PersonalQuant.Application.Universes;

/// <summary>
/// Why a universe could not say who belonged to it on a date.
/// </summary>
public enum UniverseUnknownReason
{
    /// <summary>No universe is defined under that code.</summary>
    NoSuchUniverse = 1,

    /// <summary>
    /// The universe exists and has never had a coverage claim declared, so no
    /// date is known.
    /// </summary>
    /// <remarks>
    /// The state every universe starts in. It is not a defect in itself — it is
    /// the truthful answer before anyone sources a membership history — and it
    /// becomes a recorded finding rather than a silence.
    /// </remarks>
    NoCoverageDeclared = 2,

    /// <summary>The date falls outside the span the universe claims to know.</summary>
    /// <remarks>
    /// The case that would otherwise be most dangerous: VN30 sourced from 2024
    /// and asked for 2018. There are no rows for 2018, and without the claim
    /// that absence would read as an index with no constituents.
    /// </remarks>
    OutsideCoverage = 3,
}

/// <summary>
/// Who belonged to a universe on a date, or a statement that nobody knows.
/// </summary>
/// <remarks>
/// <para>
/// The type exists to make one mistake impossible. A constituent read has three
/// possible answers — <em>these securities</em>, <em>none, and that is a
/// fact</em>, and <em>nobody has recorded it</em> — and a list is only capable
/// of expressing the first two. Returning an empty list for the third is how a
/// backtest over a year nobody sourced quietly becomes a backtest over an empty
/// market, reporting no positions and no error.
/// </para>
/// <para>
/// So an unknown answer has no member list to read: <see cref="Members"/>
/// throws rather than returning empty. A caller must decide what to do about
/// not knowing, and the compiler cannot force that, but the first run of the
/// code will.
/// </para>
/// </remarks>
public sealed class UniverseConstituents
{
    private readonly IReadOnlyList<InstrumentId>? members;

    private UniverseConstituents(
        UniverseCode code,
        DateOnly asOf,
        IReadOnlyList<InstrumentId>? members,
        UniverseUnknownReason? unknownReason)
    {
        Code = code;
        AsOf = asOf;
        this.members = members;
        UnknownReason = unknownReason;
    }

    /// <summary>Gets the universe that was asked.</summary>
    public UniverseCode Code { get; }

    /// <summary>Gets the date it was asked about.</summary>
    public DateOnly AsOf { get; }

    /// <summary>Gets a value indicating whether the membership on that date is known.</summary>
    public bool IsKnown => members is not null;

    /// <summary>Gets why it is not known, when it is not.</summary>
    public UniverseUnknownReason? UnknownReason { get; }

    /// <summary>
    /// Gets the securities that belonged, canonical identifiers only.
    /// </summary>
    /// <remarks>
    /// Identifiers rather than tickers, because a ticker is a spelling that
    /// changes and gets reassigned; a set of them read years later would name
    /// different securities than it did when it was written.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The membership is not known. There is no member list to read, and an
    /// empty one would be a different claim entirely.
    /// </exception>
    public IReadOnlyList<InstrumentId> Members =>
        members ?? throw new InvalidOperationException(
            $"Membership of {Code} on {AsOf:O} is not known ({UnknownReason}); it has no member list. "
            + "An unknown membership is not an empty one.");

    /// <summary>
    /// Records a known membership.
    /// </summary>
    /// <param name="code">The universe asked.</param>
    /// <param name="asOf">The date asked about.</param>
    /// <param name="members">
    /// The securities that belonged. May legitimately be empty: an index
    /// between its creation and its first review had no constituents, and that
    /// is a fact rather than an absence of data.
    /// </param>
    /// <returns>The known answer.</returns>
    public static UniverseConstituents Known(
        UniverseCode code,
        DateOnly asOf,
        IReadOnlyList<InstrumentId> members)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(members);

        return new UniverseConstituents(code, asOf, members, unknownReason: null);
    }

    /// <summary>
    /// Records that the membership is not known.
    /// </summary>
    /// <param name="code">The universe asked.</param>
    /// <param name="asOf">The date asked about.</param>
    /// <param name="reason">Why it is not known.</param>
    /// <returns>The unknown answer.</returns>
    public static UniverseConstituents Unknown(
        UniverseCode code,
        DateOnly asOf,
        UniverseUnknownReason reason)
    {
        ArgumentNullException.ThrowIfNull(code);

        return new UniverseConstituents(code, asOf, members: null, reason);
    }
}
