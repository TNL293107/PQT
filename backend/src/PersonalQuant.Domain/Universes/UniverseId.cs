namespace PersonalQuant.Domain.Universes;

/// <summary>
/// The canonical internal identifier of a <see cref="Universe"/>.
/// </summary>
/// <remarks>
/// A surrogate rather than the code, for the same reason an exchange has one: a
/// universe can be renamed or re-coded — an index rebrands, a custom list is
/// reorganised — and every membership row pointing at it must survive that
/// without being rewritten.
/// </remarks>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct UniverseId(Guid Value)
{
    /// <summary>Gets a value indicating whether the identifier is unassigned.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <summary>Issues a new identifier.</summary>
    /// <returns>A new, unique identifier.</returns>
    public static UniverseId New() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
