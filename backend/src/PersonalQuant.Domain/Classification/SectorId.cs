namespace PersonalQuant.Domain.Classification;

/// <summary>
/// The canonical internal identifier of a <see cref="Sector"/>.
/// </summary>
/// <remarks>
/// A distinct type rather than a bare <see cref="Guid"/>, so a sector
/// identifier cannot be passed where an industry identifier is expected — the
/// two are the same shape and one level apart, which is exactly the pair a
/// compiler should be keeping straight.
/// </remarks>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct SectorId(Guid Value)
{
    /// <summary>Gets a value indicating whether the identifier is unassigned.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <summary>Issues a new identifier.</summary>
    /// <returns>A new, unique identifier.</returns>
    public static SectorId New() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
