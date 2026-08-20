using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Domain.Instruments;

/// <summary>The canonical internal identifier of an <see cref="InstrumentIdentifier"/>.</summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct InstrumentIdentifierId(Guid Value)
{
    /// <summary>Gets a value indicating whether the identifier is unassigned.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <summary>Issues a new identifier.</summary>
    /// <returns>A new, unique identifier.</returns>
    public static InstrumentIdentifierId New() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>
/// An alias by which some outside system knows an instrument.
/// </summary>
/// <remarks>
/// <para>
/// This is what makes the instrument master's promise true: every provider's
/// spelling of FPT maps to the same canonical identifier. Without aliases the
/// only route from a provider's record to an instrument is a ticker, and a
/// ticker is neither unique across venues nor stable across an issuer's life.
/// </para>
/// <para>
/// An alias is never identity. <see cref="InstrumentId"/> remains the key
/// everything joins on: an ISIN is licensed reference data that its issuing
/// agency can reassign, and a provider symbol belongs to the provider.
/// </para>
/// <para>
/// The scheme and value are held as two flat properties rather than as the
/// <see cref="IdentifierValue"/> that produced them. Construction still goes
/// through that type, so nothing unvalidated can be recorded — but the stored
/// shape is two indexable columns, and "every ISIN in the master" is a real
/// query that a single composite string would turn into a scan.
/// </para>
/// <para>
/// The record is not deleted when a provider stops using a symbol. A price
/// series imported under it stays attached to the instrument, and removing the
/// alias would leave no way to explain how the two were ever connected.
/// </para>
/// </remarks>
public sealed class InstrumentIdentifier : AuditableEntity
{
    // EF Core materialises through this constructor and sets the properties
    // directly; it is never used by application code.
    private InstrumentIdentifier() => Value = null!;

    private InstrumentIdentifier(
        InstrumentIdentifierId id,
        InstrumentId instrumentId,
        IdentifierScheme scheme,
        string value,
        SourceCode? source)
    {
        Id = id;
        InstrumentId = instrumentId;
        Scheme = scheme;
        Value = value;
        Source = source;
    }

    /// <summary>Gets the canonical internal identifier of the alias itself.</summary>
    public InstrumentIdentifierId Id { get; private set; }

    /// <summary>Gets the instrument the alias names.</summary>
    public InstrumentId InstrumentId { get; private set; }

    /// <summary>Gets the naming system the value belongs to.</summary>
    public IdentifierScheme Scheme { get; private set; }

    /// <summary>Gets the value, in the form its scheme requires.</summary>
    public string Value { get; private set; }

    /// <summary>
    /// Gets the provider that issued the alias, or <see langword="null"/> for
    /// a globally scoped scheme.
    /// </summary>
    /// <remarks>
    /// Required for a provider symbol and forbidden for an ISIN or a FIGI. The
    /// two are enforced together at construction, because a provider symbol
    /// with no source names nothing, and a global identifier attributed to one
    /// provider would be recorded as if the next vendor's copy were a
    /// different thing.
    /// </remarks>
    public SourceCode? Source { get; private set; }

    /// <summary>
    /// Records that an outside system knows this instrument by a value.
    /// </summary>
    /// <param name="instrumentId">The instrument the alias names.</param>
    /// <param name="value">The validated scheme and value.</param>
    /// <param name="source">
    /// The provider that issued it. Required for
    /// <see cref="IdentifierScheme.ProviderSymbol"/>, and rejected otherwise.
    /// </param>
    /// <param name="occurredAtUtc">The instant the record is created.</param>
    /// <returns>The new alias.</returns>
    /// <exception cref="DomainValidationException">A supplied value is invalid.</exception>
    public static InstrumentIdentifier Record(
        InstrumentId instrumentId,
        IdentifierValue value,
        SourceCode? source,
        DateTimeOffset occurredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (instrumentId.IsEmpty)
        {
            throw new DomainValidationException("An identifier must belong to an instrument.");
        }

        if (value.Scheme.IsGlobal() && source is not null)
        {
            throw new DomainValidationException(
                $"A {value.Scheme} names a security everywhere and cannot be attributed to '{source}'.");
        }

        if (!value.Scheme.IsGlobal() && source is null)
        {
            throw new DomainValidationException(
                $"A {value.Scheme} is only unique within a provider, so it requires one.");
        }

        var identifier = new InstrumentIdentifier(
            InstrumentIdentifierId.New(), instrumentId, value.Scheme, value.Value, source);

        identifier.MarkCreated(occurredAtUtc);
        return identifier;
    }

    /// <summary>
    /// Reports whether this alias is the one an outside system would have used.
    /// </summary>
    /// <remarks>
    /// Source is compared as well as scheme and value, because two providers
    /// legitimately use the same decorated symbol for different securities.
    /// </remarks>
    /// <param name="value">The scheme and value to compare.</param>
    /// <param name="source">The provider, when the scheme is provider-scoped.</param>
    /// <returns><see langword="true"/> when the alias matches.</returns>
    public bool Matches(IdentifierValue value, SourceCode? source)
    {
        ArgumentNullException.ThrowIfNull(value);

        return Scheme == value.Scheme
            && string.Equals(Value, value.Value, StringComparison.Ordinal)
            && Source == source;
    }
}
