using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.Exchanges;

namespace PersonalQuant.Domain.Instruments;

/// <summary>Identifies a venue spell.</summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct InstrumentVenueSpellId(Guid Value)
{
    /// <summary>Creates a new identifier, ordered by creation time.</summary>
    /// <returns>The identifier.</returns>
    public static InstrumentVenueSpellId New() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>
/// A span of sessions during which an instrument traded on one venue.
/// </summary>
/// <remarks>
/// <para>
/// Vietnamese issuers move: UPCOM to HNX to HOSE, and occasionally back. The
/// instrument keeps its identity across the move — that is what makes its
/// series one series — but the rules its sessions are judged by do not: a 12%
/// fall is impossible on HOSE and ordinary on UPCOM. Without a record of when
/// the move happened, every earlier session is judged by the venue the
/// instrument is on <em>now</em>.
/// </para>
/// <para>
/// Half-open, like a universe spell: <see cref="EffectiveFrom"/> is the first
/// session on the venue and <see cref="EffectiveTo"/> the first session on the
/// next one. A null start means "since before anything this system records",
/// which is how the venue an instrument left is stated without inventing the
/// day it first listed there.
/// </para>
/// </remarks>
public sealed class InstrumentVenueSpell
{
    // EF Core materialises through this constructor and sets the properties
    // directly; it is never used by application code.
    private InstrumentVenueSpell()
    {
    }

    private InstrumentVenueSpell(
        InstrumentVenueSpellId id,
        InstrumentId instrumentId,
        ExchangeId exchangeId,
        DateOnly? effectiveFrom,
        DateOnly? effectiveTo,
        DateTimeOffset recordedAtUtc)
    {
        Id = id;
        InstrumentId = instrumentId;
        ExchangeId = exchangeId;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        RecordedAtUtc = recordedAtUtc;
    }

    /// <summary>Gets the spell's identifier.</summary>
    public InstrumentVenueSpellId Id { get; private set; }

    /// <summary>Gets the instrument.</summary>
    public InstrumentId InstrumentId { get; private set; }

    /// <summary>Gets the venue it traded on.</summary>
    public ExchangeId ExchangeId { get; private set; }

    /// <summary>Gets the first session on the venue, or null for "since before any record".</summary>
    public DateOnly? EffectiveFrom { get; private set; }

    /// <summary>Gets the first session no longer on the venue, or null while it trades there.</summary>
    public DateOnly? EffectiveTo { get; private set; }

    /// <summary>Gets when the spell was recorded.</summary>
    public DateTimeOffset RecordedAtUtc { get; private set; }

    /// <summary>Records a spell.</summary>
    /// <param name="instrumentId">The instrument.</param>
    /// <param name="exchangeId">The venue.</param>
    /// <param name="effectiveFrom">The first session there, or null.</param>
    /// <param name="effectiveTo">The first session elsewhere, or null.</param>
    /// <param name="recordedAtUtc">When it is recorded.</param>
    /// <returns>The spell.</returns>
    /// <exception cref="DomainValidationException">A value is not usable.</exception>
    public static InstrumentVenueSpell Record(
        InstrumentId instrumentId,
        ExchangeId exchangeId,
        DateOnly? effectiveFrom,
        DateOnly? effectiveTo,
        DateTimeOffset recordedAtUtc)
    {
        if (instrumentId.IsEmpty)
        {
            throw new DomainValidationException("A venue spell must name an instrument.");
        }

        if (exchangeId.IsEmpty)
        {
            throw new DomainValidationException("A venue spell must name a venue.");
        }

        if (effectiveFrom is { } from && effectiveTo is { } to && to <= from)
        {
            throw new DomainValidationException(
                $"A venue spell must end after it starts; {to:yyyy-MM-dd} is not after {from:yyyy-MM-dd}.");
        }

        if (recordedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new DomainValidationException(
                $"Timestamps must be UTC, but the offset was {recordedAtUtc.Offset}.");
        }

        return new InstrumentVenueSpell(
            InstrumentVenueSpellId.New(), instrumentId, exchangeId, effectiveFrom, effectiveTo, recordedAtUtc);
    }

    /// <summary>Gets a value indicating whether a session falls inside the spell.</summary>
    /// <param name="session">The session.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    public bool Includes(DateOnly session) =>
        (EffectiveFrom is not { } from || from <= session)
        && (EffectiveTo is not { } to || session < to);
}

/// <summary>
/// The venue an instrument traded on, session by session.
/// </summary>
/// <remarks>
/// An instrument with no recorded spells is on its current venue for every
/// session — the reading every instrument had before venue history existed,
/// and still the right one for a name that never moved. Once spells are
/// recorded they are complete by construction (the import refuses gaps and
/// overlaps, and the last spell is the current venue), so a session always
/// has an answer.
/// </remarks>
public sealed class VenueTimeline
{
    private readonly IReadOnlyList<InstrumentVenueSpell> _spells;

    /// <summary>Initialises a timeline.</summary>
    /// <param name="current">The venue the instrument is on now.</param>
    /// <param name="spells">Its recorded spells, in any order.</param>
    public VenueTimeline(ExchangeId current, IEnumerable<InstrumentVenueSpell> spells)
    {
        ArgumentNullException.ThrowIfNull(spells);

        Current = current;
        _spells = [.. spells.OrderBy(spell => spell.EffectiveFrom ?? DateOnly.MinValue)];
    }

    /// <summary>Gets the venue the instrument is on now.</summary>
    public ExchangeId Current { get; }

    /// <summary>Gets a value indicating whether the instrument has ever moved, as far as is recorded.</summary>
    public bool HasMoved => _spells.Select(spell => spell.ExchangeId).Distinct().Skip(1).Any();

    /// <summary>Gets the venue the instrument traded on in a session.</summary>
    /// <param name="session">The session.</param>
    /// <returns>The venue.</returns>
    public ExchangeId On(DateOnly session) =>
        _spells.FirstOrDefault(spell => spell.Includes(session))?.ExchangeId ?? Current;

    /// <summary>
    /// Splits a window of sessions into the stretches spent on each venue.
    /// </summary>
    /// <param name="fromDate">The first session, inclusive.</param>
    /// <param name="toDate">The last session, inclusive.</param>
    /// <returns>Each stretch, in date order; one stretch when the instrument never moved in the window.</returns>
    public IReadOnlyList<(ExchangeId Venue, DateOnly From, DateOnly To)> Stretches(DateOnly fromDate, DateOnly toDate)
    {
        if (toDate < fromDate)
        {
            return [];
        }

        if (_spells.Count == 0)
        {
            return [(Current, fromDate, toDate)];
        }

        var stretches = new List<(ExchangeId, DateOnly, DateOnly)>();

        foreach (var spell in _spells)
        {
            var start = spell.EffectiveFrom is { } from && from > fromDate ? from : fromDate;
            var end = spell.EffectiveTo is { } to && to.AddDays(-1) < toDate ? to.AddDays(-1) : toDate;

            if (start <= end)
            {
                stretches.Add((spell.ExchangeId, start, end));
            }
        }

        return stretches;
    }
}
