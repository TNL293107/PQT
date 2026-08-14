namespace PersonalQuant.Domain.Exchanges;

/// <summary>
/// The canonical internal identifier of an <see cref="Exchange"/>.
/// </summary>
/// <remarks>
/// A distinct type rather than a bare <see cref="Guid"/>, so an exchange
/// identifier cannot be passed where an instrument identifier is expected.
/// </remarks>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct ExchangeId(Guid Value)
{
    /// <summary>Gets a value indicating whether the identifier is unassigned.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <summary>Issues a new identifier.</summary>
    /// <returns>A new, unique identifier.</returns>
    public static ExchangeId New() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
