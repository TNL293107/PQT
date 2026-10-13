namespace PersonalQuant.Domain.Fundamentals;

/// <summary>
/// Where a statement value was read, which bounds how far it can be trusted
/// to be the value as first published.
/// </summary>
/// <remarks>
/// ADR-029, decision 3. An <see cref="AsFiled"/> value is the issuer's own
/// submission. A <see cref="Vendor"/> value is an aggregator's, which may be a
/// later restatement shown against the original filing's date. The basis is
/// carried on every value so a read can say how much of what it served was
/// each.
/// </remarks>
public enum ValueBasis
{
    /// <summary>Not specified. Never valid on a stored line.</summary>
    Unspecified = 0,

    /// <summary>Read from the filing's own structured form.</summary>
    AsFiled = 1,

    /// <summary>Read from an aggregator.</summary>
    Vendor = 2,
}
