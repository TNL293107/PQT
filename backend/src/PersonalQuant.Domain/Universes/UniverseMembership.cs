using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Domain.Universes;

/// <summary>
/// That a security belonged to a universe, over the span of dates it belonged.
/// </summary>
/// <remarks>
/// <para>
/// Append-only. A membership is never updated except to close its interval, and
/// never deleted — a security removed from an index in July was a constituent
/// in June, and a row that can be edited cannot be trusted to say so.
/// </para>
/// <para>
/// The interval is half-open, <c>[EffectiveFrom, EffectiveTo)</c>. A review
/// that removes one name and admits another does both on the same date, and
/// only a half-open interval puts that date on exactly one side of each: the
/// leaver's last session is the day before, the joiner's first is the date
/// itself. Inclusive bounds would make an index of thirty briefly hold
/// thirty-one.
/// </para>
/// <para>
/// Re-entry is a second row, not an edit to the first. A security demoted at
/// one review and restored at a later one has two disjoint spells, and the gap
/// between them is precisely what a backtest must not be allowed to skip over.
/// </para>
/// <para>
/// <see cref="AnnouncedOn"/> is recorded and not read. An index review is
/// published before it takes effect, so a strategy rebalancing on the effective
/// date could legitimately have known — but one acting on the announcement
/// earlier could not have known before it was published. Filtering on that is
/// U4's, and until then this field must not quietly move what the interval
/// says.
/// </para>
/// </remarks>
public sealed class UniverseMembership
{
    // EF Core materialises through this constructor and sets the properties
    // directly; it is never used by application code.
    private UniverseMembership() => Source = null!;

    private UniverseMembership(
        UniverseId universeId,
        InstrumentId instrumentId,
        DateOnly effectiveFrom,
        DateOnly? announcedOn,
        SourceCode source,
        DateTimeOffset recordedAtUtc)
    {
        UniverseId = universeId;
        InstrumentId = instrumentId;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = null;
        AnnouncedOn = announcedOn;
        Source = source;
        RecordedAtUtc = recordedAtUtc;
    }

    /// <summary>Gets the universe the security belonged to.</summary>
    public UniverseId UniverseId { get; private set; }

    /// <summary>Gets the security that belonged to it.</summary>
    public InstrumentId InstrumentId { get; private set; }

    /// <summary>Gets the first date of membership. Inclusive.</summary>
    /// <remarks>
    /// Effective time: the date the security actually belonged from, which is
    /// not the date the review was announced and not the date this system found
    /// out.
    /// </remarks>
    public DateOnly EffectiveFrom { get; private set; }

    /// <summary>
    /// Gets the first date of non-membership, or <see langword="null"/> while
    /// the security still belongs. Exclusive.
    /// </summary>
    /// <remarks>
    /// <see langword="null"/> means still a member, not "end unknown". There is
    /// at most one such row per security per universe, and the database refuses
    /// a second.
    /// </remarks>
    public DateOnly? EffectiveTo { get; private set; }

    /// <summary>
    /// Gets the date the change was made public, when it is known.
    /// </summary>
    /// <remarks>
    /// Announcement time. Stored now so that the history does not have to be
    /// re-sourced when U4 starts filtering on it; unread until then.
    /// </remarks>
    public DateOnly? AnnouncedOn { get; private set; }

    /// <summary>Gets where this membership fact came from.</summary>
    /// <remarks>
    /// Per row rather than per universe, because a history is usually assembled
    /// from more than one source — a vendor file for recent reviews, a
    /// hand-transcribed announcement for an older one — and which rows came
    /// from where is the first thing anyone reconciling them needs.
    /// </remarks>
    public SourceCode Source { get; private set; }

    /// <summary>Gets the instant this system recorded the fact, in UTC.</summary>
    /// <remarks>
    /// Observation time, kept distinct from the effective dates for the same
    /// reason a bar's is: learning in 2026 that a security joined in 2018 does
    /// not mean this system knew it in 2018.
    /// </remarks>
    public DateTimeOffset RecordedAtUtc { get; private set; }

    /// <summary>Gets a value indicating whether the security still belongs.</summary>
    public bool IsCurrent => EffectiveTo is null;

    /// <summary>
    /// Records that a security joined a universe.
    /// </summary>
    /// <param name="universeId">The universe joined.</param>
    /// <param name="instrumentId">The security that joined.</param>
    /// <param name="effectiveFrom">The first date of membership.</param>
    /// <param name="announcedOn">The date the change was published, when known.</param>
    /// <param name="source">Where the fact came from.</param>
    /// <param name="recordedAtUtc">The instant this system recorded it.</param>
    /// <returns>The open membership.</returns>
    /// <exception cref="DomainValidationException">A supplied value is invalid.</exception>
    public static UniverseMembership Admit(
        UniverseId universeId,
        InstrumentId instrumentId,
        DateOnly effectiveFrom,
        DateOnly? announcedOn,
        SourceCode source,
        DateTimeOffset recordedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (universeId.IsEmpty)
        {
            throw new DomainValidationException("A membership must name a universe.");
        }

        if (instrumentId.IsEmpty)
        {
            throw new DomainValidationException("A membership must name a security.");
        }

        if (recordedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new DomainValidationException(
                $"Observation timestamps must be UTC, but the offset was {recordedAtUtc.Offset}.");
        }

        return new UniverseMembership(
            universeId,
            instrumentId,
            effectiveFrom,
            announcedOn,
            source,
            recordedAtUtc);
    }

    /// <summary>
    /// Closes the interval, recording that the security left.
    /// </summary>
    /// <param name="effectiveTo">
    /// The first date the security was no longer a member. Exclusive, so it is
    /// the removal date itself rather than the last day held.
    /// </param>
    /// <exception cref="DomainValidationException">The date is not usable.</exception>
    /// <exception cref="DomainStateException">The interval is already closed.</exception>
    public void Remove(DateOnly effectiveTo)
    {
        if (EffectiveTo is not null)
        {
            // Rewriting a closed interval would erase a spell that happened. A
            // security that came back belongs in a new row.
            throw new DomainStateException(
                "This membership has already ended; a later spell is a new membership.");
        }

        if (effectiveTo <= EffectiveFrom)
        {
            throw new DomainValidationException(
                $"A membership from {EffectiveFrom:O} cannot end on {effectiveTo:O}; it would cover no session.");
        }

        EffectiveTo = effectiveTo;
    }

    /// <summary>
    /// Reports whether the security belonged on a date.
    /// </summary>
    /// <param name="asOf">The date to test.</param>
    /// <returns>
    /// <see langword="true"/> when the date falls in
    /// <c>[EffectiveFrom, EffectiveTo)</c>.
    /// </returns>
    public bool WasMemberOn(DateOnly asOf) =>
        EffectiveFrom <= asOf && (EffectiveTo is not { } end || asOf < end);

    /// <summary>
    /// Reports whether two memberships claim the same security belonged to the
    /// same universe at the same time.
    /// </summary>
    /// <remarks>
    /// The database refuses an overlap outright. This exists so that an import
    /// staging several rows can say which two rows disagree, and name the
    /// security, before the transaction fails on a constraint that can only
    /// name itself.
    /// </remarks>
    /// <param name="other">The membership to compare against.</param>
    /// <returns><see langword="true"/> when the two intervals intersect.</returns>
    public bool Overlaps(UniverseMembership other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (UniverseId != other.UniverseId || InstrumentId != other.InstrumentId)
        {
            return false;
        }

        // Half-open intervals intersect when each starts before the other ends.
        // Two spells that meet at a date do not: one ends exactly where the
        // next begins.
        return EffectiveFrom < (other.EffectiveTo ?? DateOnly.MaxValue)
            && other.EffectiveFrom < (EffectiveTo ?? DateOnly.MaxValue);
    }
}
