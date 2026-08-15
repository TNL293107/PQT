using PersonalQuant.Domain.Common;

namespace PersonalQuant.Domain.Classification;

/// <summary>
/// The broad economic grouping a security belongs to, such as
/// <c>Technology</c> or <c>Financials</c>.
/// </summary>
/// <remarks>
/// <para>
/// The upper level of a two-level taxonomy — sector, then
/// <see cref="Industry"/>. Two levels are enough for what the terminal does
/// with a classification: filter a universe, compute a peer group, and
/// aggregate exposure. A four-level standard would carry three levels nothing
/// reads.
/// </para>
/// <para>
/// A reference entity rather than an enumeration, unlike
/// <see cref="Instruments.AssetType"/>. The set is not closed: a taxonomy
/// revision adds and splits sectors, and the mapping a provider supplies has
/// to be recordable without a code change and a redeploy.
/// </para>
/// </remarks>
public sealed class Sector : AuditableEntity
{
    /// <summary>Longest permitted sector name.</summary>
    public const int MaxNameLength = 120;

    // EF Core materialises through this constructor and sets the properties
    // directly; it is never used by application code.
    private Sector()
    {
        Code = null!;
        Name = null!;
    }

    private Sector(SectorId id, ClassificationCode code, string name)
    {
        Id = id;
        Code = code;
        Name = name;
    }

    /// <summary>Gets the canonical internal identifier.</summary>
    public SectorId Id { get; private set; }

    /// <summary>Gets the taxonomy code, such as <c>TECH</c>.</summary>
    public ClassificationCode Code { get; private set; }

    /// <summary>Gets the display name.</summary>
    public string Name { get; private set; }

    /// <summary>
    /// Registers a sector.
    /// </summary>
    /// <param name="code">The taxonomy code.</param>
    /// <param name="name">The display name.</param>
    /// <param name="occurredAtUtc">The instant the record is created.</param>
    /// <returns>The new sector.</returns>
    /// <exception cref="DomainValidationException">A supplied value is invalid.</exception>
    public static Sector Register(
        ClassificationCode code,
        string name,
        DateTimeOffset occurredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(code);

        var sector = new Sector(
            SectorId.New(),
            code,
            ClassificationName.Require(name, MaxNameLength, "A sector"));

        sector.MarkCreated(occurredAtUtc);
        return sector;
    }

    /// <summary>
    /// Renames the sector.
    /// </summary>
    /// <param name="name">The new display name.</param>
    /// <param name="occurredAtUtc">The instant the change is recorded.</param>
    /// <exception cref="DomainValidationException">The name is invalid.</exception>
    public void Rename(string name, DateTimeOffset occurredAtUtc)
    {
        Name = ClassificationName.Require(name, MaxNameLength, "A sector");
        MarkUpdated(occurredAtUtc);
    }
}
