namespace PersonalQuant.Domain.Instruments;

/// <summary>
/// The canonical internal identifier of an <see cref="Instrument"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the key every other domain joins on. It is issued by this system
/// and is never a ticker, a provider symbol, or a licensed external identifier.
/// </para>
/// <para>
/// On Vietnamese exchanges that is a correctness requirement rather than a
/// preference: tickers change when an issuer transfers between UPCOM, HNX and
/// HOSE, and a delisted ticker can later be reassigned to an unrelated company.
/// A ticker used as a key would eventually merge two different securities.
/// </para>
/// </remarks>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct InstrumentId(Guid Value)
{
    /// <summary>Gets a value indicating whether the identifier is unassigned.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <summary>Issues a new identifier.</summary>
    /// <remarks>
    /// Version 7 UUIDs are time-ordered, which keeps the clustered index on a
    /// table that will only ever grow from fragmenting.
    /// </remarks>
    /// <returns>A new, unique identifier.</returns>
    public static InstrumentId New() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
