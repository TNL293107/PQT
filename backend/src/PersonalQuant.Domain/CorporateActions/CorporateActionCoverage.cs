using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.Domain.CorporateActions;

/// <summary>Identifies a coverage record.</summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct CorporateActionCoverageId(Guid Value)
{
    /// <summary>Creates a new identifier, ordered by creation time.</summary>
    /// <returns>The identifier.</returns>
    public static CorporateActionCoverageId New() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>
/// A statement that every corporate action of an issuer is recorded over a
/// stretch of ex-dates (ADR-032).
/// </summary>
/// <remarks>
/// <para>
/// Recorded, never inferred. The actions recorded before sprint 8 were sourced
/// to explain price-band breaches, so a first recorded action proves nothing
/// about the cash dividends after it. A record says a person read every notice
/// of the issuer: every action with an ex-date from
/// <see cref="CompleteFrom"/> to <see cref="SweptOn"/> is in the record.
/// Outside that stretch, "no dividend recorded" is not "no dividend paid".
/// </para>
/// <para>
/// Append-only. A later sweep is a new record with a later
/// <see cref="SweptOn"/>; the earlier one stays, so a read as of an earlier
/// instant sees the coverage that was recorded then.
/// </para>
/// </remarks>
public sealed class CorporateActionCoverage
{
    /// <summary>The longest source note a record may carry.</summary>
    public const int MaxSourceLength = 512;

    // EF Core materialises through this constructor and sets the properties
    // directly; it is never used by application code.
    private CorporateActionCoverage()
    {
        Source = null!;
    }

    private CorporateActionCoverage(
        CorporateActionCoverageId id,
        InstrumentId instrumentId,
        DateOnly completeFrom,
        DateOnly sweptOn,
        string source,
        DateTimeOffset recordedAtUtc)
    {
        Id = id;
        InstrumentId = instrumentId;
        CompleteFrom = completeFrom;
        SweptOn = sweptOn;
        Source = source;
        RecordedAtUtc = recordedAtUtc;
    }

    /// <summary>Gets the record's identifier.</summary>
    public CorporateActionCoverageId Id { get; private set; }

    /// <summary>Gets the issuer's instrument.</summary>
    public InstrumentId InstrumentId { get; private set; }

    /// <summary>Gets the first ex-date from which every action is recorded.</summary>
    public DateOnly CompleteFrom { get; private set; }

    /// <summary>Gets the day the notices were read; nothing after it is vouched for.</summary>
    public DateOnly SweptOn { get; private set; }

    /// <summary>Gets where the notices were read.</summary>
    public string Source { get; private set; }

    /// <summary>Gets when the record was made in this system.</summary>
    public DateTimeOffset RecordedAtUtc { get; private set; }

    /// <summary>Records a sweep.</summary>
    /// <param name="instrumentId">The issuer's instrument.</param>
    /// <param name="completeFrom">The first ex-date the sweep vouches for.</param>
    /// <param name="sweptOn">The day the notices were read.</param>
    /// <param name="source">Where they were read.</param>
    /// <param name="recordedAtUtc">When it is recorded.</param>
    /// <returns>The record.</returns>
    /// <exception cref="DomainValidationException">A value is not usable.</exception>
    public static CorporateActionCoverage Record(
        InstrumentId instrumentId,
        DateOnly completeFrom,
        DateOnly sweptOn,
        string source,
        DateTimeOffset recordedAtUtc)
    {
        if (instrumentId.IsEmpty)
        {
            throw new DomainValidationException("A coverage record must name an instrument.");
        }

        if (sweptOn < completeFrom)
        {
            throw new DomainValidationException(
                $"A sweep read on {sweptOn:yyyy-MM-dd} cannot vouch for actions from {completeFrom:yyyy-MM-dd}.");
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            throw new DomainValidationException("A coverage record must say where the notices were read.");
        }

        var trimmed = source.Trim();

        if (trimmed.Length > MaxSourceLength)
        {
            throw new DomainValidationException(
                $"A coverage source may be at most {MaxSourceLength} characters.");
        }

        if (recordedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new DomainValidationException(
                $"Timestamps must be UTC, but the offset was {recordedAtUtc.Offset}.");
        }

        return new CorporateActionCoverage(
            CorporateActionCoverageId.New(), instrumentId, completeFrom, sweptOn, trimmed, recordedAtUtc);
    }

    /// <summary>Whether the record vouches for every action with an ex-date in a stretch.</summary>
    /// <param name="from">The first ex-date, inclusive.</param>
    /// <param name="through">The last ex-date, inclusive.</param>
    /// <returns><see langword="true"/> when the whole stretch lies inside the record.</returns>
    public bool Covers(DateOnly from, DateOnly through) =>
        CompleteFrom <= from && through <= SweptOn;
}
