using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.Universes;

namespace PersonalQuant.Application.Universes;

/// <summary>
/// One security's spell in a universe, as a read-only fact.
/// </summary>
/// <param name="InstrumentId">The security.</param>
/// <param name="EffectiveFrom">The first date of membership, inclusive.</param>
/// <param name="EffectiveTo">The date it left, exclusive; null while it is still a member.</param>
/// <param name="AnnouncedOn">When the change was published, when known.</param>
public sealed record UniverseSpell(
    InstrumentId InstrumentId,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    DateOnly? AnnouncedOn)
{
    /// <summary>
    /// Reports whether the security was a member on a date.
    /// </summary>
    /// <remarks>
    /// The same half-open rule the as-of read applies in SQL, so a review that
    /// swaps one name for another counts each on exactly one side of it.
    /// </remarks>
    /// <param name="date">The date.</param>
    /// <returns><see langword="true"/> when the spell includes the date.</returns>
    public bool Includes(DateOnly date) =>
        EffectiveFrom <= date && (EffectiveTo is not { } end || date < end);
}

/// <summary>
/// Who belonged to a universe on every day of a window, or why that is not
/// known.
/// </summary>
/// <remarks>
/// <para>
/// The windowed counterpart of <see cref="UniverseConstituents"/>, with the
/// same refusal built into its shape: an unknown history has no spells to read,
/// so a caller that forgets to ask cannot mistake it for an index that held
/// nobody.
/// </para>
/// <para>
/// Known only when the universe's coverage claim spans the whole window. A
/// window that starts before the sourced history would otherwise come back with
/// the spells that happen to be recorded, and a backtest over it would be
/// survivorship-biased in exactly the years it did not know about.
/// </para>
/// </remarks>
public sealed class UniverseHistory
{
    private readonly IReadOnlyList<UniverseSpell>? spells;

    private UniverseHistory(
        UniverseCode code,
        DateOnly from,
        DateOnly to,
        IReadOnlyList<UniverseSpell>? spells,
        UniverseUnknownReason? unknownReason)
    {
        Code = code;
        From = from;
        To = to;
        this.spells = spells;
        UnknownReason = unknownReason;
    }

    /// <summary>Gets the universe.</summary>
    public UniverseCode Code { get; }

    /// <summary>Gets the first date of the window, inclusive.</summary>
    public DateOnly From { get; }

    /// <summary>Gets the last date of the window, inclusive.</summary>
    public DateOnly To { get; }

    /// <summary>Gets whether membership is known on every day of the window.</summary>
    public bool IsKnown => spells is not null;

    /// <summary>Gets why it is not, when it is not.</summary>
    public UniverseUnknownReason? UnknownReason { get; }

    /// <summary>
    /// Gets every spell that overlaps the window, ordered by security and start.
    /// </summary>
    /// <exception cref="InvalidOperationException">The history is not known.</exception>
    public IReadOnlyList<UniverseSpell> Spells =>
        spells ?? throw new InvalidOperationException(
            $"Membership of {Code} from {From:O} to {To:O} is not known ({UnknownReason}); it has "
            + "no spells. An unknown membership is not an empty one.");

    /// <summary>Creates a known history.</summary>
    /// <param name="code">The universe.</param>
    /// <param name="from">The first date, inclusive.</param>
    /// <param name="to">The last date, inclusive.</param>
    /// <param name="spells">Every spell overlapping the window.</param>
    /// <returns>The history.</returns>
    public static UniverseHistory Known(
        UniverseCode code,
        DateOnly from,
        DateOnly to,
        IReadOnlyList<UniverseSpell> spells)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(spells);

        return new UniverseHistory(code, from, to, spells, unknownReason: null);
    }

    /// <summary>Creates a history that is not known, and says why.</summary>
    /// <param name="code">The universe.</param>
    /// <param name="from">The first date, inclusive.</param>
    /// <param name="to">The last date, inclusive.</param>
    /// <param name="reason">Why it is not known.</param>
    /// <returns>The history.</returns>
    public static UniverseHistory Unknown(
        UniverseCode code,
        DateOnly from,
        DateOnly to,
        UniverseUnknownReason reason)
    {
        ArgumentNullException.ThrowIfNull(code);

        return new UniverseHistory(code, from, to, spells: null, reason);
    }
}
