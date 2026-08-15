using PersonalQuant.Domain.Common;

namespace PersonalQuant.Domain.Classification;

/// <summary>
/// The narrower grouping a security belongs to within its
/// <see cref="Sector"/>, such as Software and IT Services.
/// </summary>
/// <remarks>
/// <para>
/// This is the level an instrument points at. The sector is reached through
/// it, which is why an instrument carries no sector key of its own: there is
/// one place the answer lives, so the two levels cannot drift apart.
/// </para>
/// <para>
/// An industry never moves between sectors here. A taxonomy that reorganises
/// its upper level is issuing new nodes, and treating that as a reparent would
/// silently rewrite the sector history of every instrument classified under
/// it.
/// </para>
/// </remarks>
public sealed class Industry : AuditableEntity
{
    /// <summary>Longest permitted industry name.</summary>
    public const int MaxNameLength = 120;

    // EF Core materialises through this constructor and sets the properties
    // directly; it is never used by application code.
    private Industry()
    {
        Code = null!;
        Name = null!;
    }

    private Industry(IndustryId id, SectorId sectorId, ClassificationCode code, string name)
    {
        Id = id;
        SectorId = sectorId;
        Code = code;
        Name = name;
    }

    /// <summary>Gets the canonical internal identifier.</summary>
    public IndustryId Id { get; private set; }

    /// <summary>Gets the sector this industry sits under.</summary>
    public SectorId SectorId { get; private set; }

    /// <summary>Gets the taxonomy code, such as <c>TECH-SOFT</c>.</summary>
    public ClassificationCode Code { get; private set; }

    /// <summary>Gets the display name.</summary>
    public string Name { get; private set; }

    /// <summary>
    /// Registers an industry under a sector.
    /// </summary>
    /// <param name="sectorId">The sector it belongs to.</param>
    /// <param name="code">The taxonomy code.</param>
    /// <param name="name">The display name.</param>
    /// <param name="occurredAtUtc">The instant the record is created.</param>
    /// <returns>The new industry.</returns>
    /// <exception cref="DomainValidationException">A supplied value is invalid.</exception>
    public static Industry Register(
        SectorId sectorId,
        ClassificationCode code,
        string name,
        DateTimeOffset occurredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(code);

        if (sectorId.IsEmpty)
        {
            throw new DomainValidationException("An industry must belong to a sector.");
        }

        var industry = new Industry(
            IndustryId.New(),
            sectorId,
            code,
            ClassificationName.Require(name, MaxNameLength, "An industry"));

        industry.MarkCreated(occurredAtUtc);
        return industry;
    }

    /// <summary>
    /// Renames the industry.
    /// </summary>
    /// <param name="name">The new display name.</param>
    /// <param name="occurredAtUtc">The instant the change is recorded.</param>
    /// <exception cref="DomainValidationException">The name is invalid.</exception>
    public void Rename(string name, DateTimeOffset occurredAtUtc)
    {
        Name = ClassificationName.Require(name, MaxNameLength, "An industry");
        MarkUpdated(occurredAtUtc);
    }
}
