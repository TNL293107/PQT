namespace PersonalQuant.Domain.Instruments;

/// <summary>
/// The naming system an <see cref="InstrumentIdentifier"/> belongs to.
/// </summary>
/// <remarks>
/// <para>
/// A closed enumeration rather than a reference table, for the same reason
/// <see cref="AssetType"/> is one: every value carries behaviour the code must
/// switch on — how it is validated, and whether it is unique globally or only
/// within one provider. A row in a table cannot carry a check-digit algorithm.
/// </para>
/// <para>
/// Only the three schemes Phase 1 needs. Adding CUSIP or SEDOL later is one
/// value here and one validation rule beside it; inventing them now would add
/// schemes nothing maps against.
/// </para>
/// </remarks>
public enum IdentifierScheme
{
    /// <summary>Not specified. Never valid on a stored identifier.</summary>
    Unspecified = 0,

    /// <summary>
    /// ISO 6166 International Securities Identification Number.
    /// </summary>
    /// <remarks>
    /// Twelve characters: a two-letter country prefix, nine alphanumerics, and
    /// a check digit. Globally unique — it names the security, not a listing
    /// of it.
    /// </remarks>
    Isin = 1,

    /// <summary>
    /// Financial Instrument Global Identifier.
    /// </summary>
    /// <remarks>
    /// Twelve characters with a check digit, and open rather than licensed,
    /// which is why it is here and CUSIP is not.
    /// </remarks>
    Figi = 2,

    /// <summary>
    /// A provider's own spelling of a symbol, such as <c>FPT.HM</c> or
    /// <c>FPT:VN</c>.
    /// </summary>
    /// <remarks>
    /// Unique only within the provider that issued it. Two vendors reuse the
    /// same decorated symbol for different securities, so a provider symbol
    /// without its source names nothing.
    /// </remarks>
    ProviderSymbol = 3,
}

/// <summary>
/// Facts about an <see cref="IdentifierScheme"/> that the model has to switch
/// on.
/// </summary>
public static class IdentifierSchemes
{
    /// <summary>
    /// Reports whether a scheme is one of the declared values.
    /// </summary>
    /// <remarks>
    /// An enum in .NET holds any integer of its underlying type, so a value
    /// read back from a database has to be checked rather than assumed.
    /// </remarks>
    /// <param name="scheme">The value to check.</param>
    /// <returns><see langword="true"/> when the scheme is usable.</returns>
    public static bool IsDeclared(this IdentifierScheme scheme) =>
        scheme is IdentifierScheme.Isin or IdentifierScheme.Figi or IdentifierScheme.ProviderSymbol;

    /// <summary>
    /// Reports whether a scheme names a security everywhere, rather than only
    /// inside one provider's namespace.
    /// </summary>
    /// <remarks>
    /// The distinction decides how uniqueness is enforced. An ISIN must
    /// resolve to one instrument across the whole master; a provider symbol
    /// must resolve to one instrument per provider, and may legitimately mean
    /// something else at the next vendor.
    /// </remarks>
    /// <param name="scheme">The scheme to check.</param>
    /// <returns><see langword="true"/> when the scheme is global.</returns>
    public static bool IsGlobal(this IdentifierScheme scheme) =>
        scheme is IdentifierScheme.Isin or IdentifierScheme.Figi;
}
