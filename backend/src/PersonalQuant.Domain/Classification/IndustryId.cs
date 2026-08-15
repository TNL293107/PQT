namespace PersonalQuant.Domain.Classification;

/// <summary>
/// The canonical internal identifier of an <see cref="Industry"/>.
/// </summary>
/// <remarks>
/// This is what <c>Instrument</c> stores. The sector is reached through the
/// industry rather than held alongside it, so the two levels cannot disagree
/// about which sector a security belongs to.
/// </remarks>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct IndustryId(Guid Value)
{
    /// <summary>Gets a value indicating whether the identifier is unassigned.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <summary>Issues a new identifier.</summary>
    /// <returns>A new, unique identifier.</returns>
    public static IndustryId New() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
