namespace PersonalQuant.Domain.Common;

/// <summary>
/// Base for master-data entities that must record when they were created and
/// last changed.
/// </summary>
/// <remarks>
/// <para>
/// Instrument master data is historical reference data: a row is never
/// meaningfully "current only". Knowing when a record entered the system and
/// when it last moved is the minimum needed to reconcile it against a provider
/// later.
/// </para>
/// <para>
/// Timestamps are supplied by the caller rather than read from a clock inside
/// the entity. That keeps this assembly dependency-free and makes every
/// transition deterministic under test.
/// </para>
/// </remarks>
public abstract class AuditableEntity
{
    /// <summary>Gets the instant the record was created, in UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; protected set; }

    /// <summary>Gets the instant the record last changed, in UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; protected set; }

    /// <summary>
    /// Stamps the creation and last-changed instants.
    /// </summary>
    /// <param name="occurredAtUtc">The instant the record was created.</param>
    /// <exception cref="DomainValidationException">
    /// The instant is not expressed in UTC.
    /// </exception>
    protected void MarkCreated(DateTimeOffset occurredAtUtc)
    {
        RequireUtc(occurredAtUtc);

        CreatedAtUtc = occurredAtUtc;
        UpdatedAtUtc = occurredAtUtc;
    }

    /// <summary>
    /// Advances the last-changed instant.
    /// </summary>
    /// <param name="occurredAtUtc">The instant the record changed.</param>
    /// <exception cref="DomainValidationException">
    /// The instant is not expressed in UTC, or predates the creation instant.
    /// </exception>
    protected void MarkUpdated(DateTimeOffset occurredAtUtc)
    {
        RequireUtc(occurredAtUtc);

        if (occurredAtUtc < CreatedAtUtc)
        {
            throw new DomainValidationException(
                $"An update at {occurredAtUtc:O} predates creation at {CreatedAtUtc:O}.");
        }

        UpdatedAtUtc = occurredAtUtc;
    }

    private static void RequireUtc(DateTimeOffset occurredAtUtc)
    {
        // A local-time audit stamp is worse than none: it looks authoritative
        // and silently shifts when the process moves between machines.
        if (occurredAtUtc.Offset != TimeSpan.Zero)
        {
            throw new DomainValidationException(
                $"Audit timestamps must be UTC, but the offset was {occurredAtUtc.Offset}.");
        }
    }
}
