namespace PersonalQuant.Domain.Instruments;

/// <summary>
/// Why a search result matched, and therefore how strongly it ranks.
/// </summary>
/// <remarks>
/// <para>
/// The numeric values are the ranking: lower is a stronger match, and results
/// are ordered by this before anything else. They are explicit because the
/// order is the contract, not an accident of declaration.
/// </para>
/// <para>
/// A trader typing <c>FPT</c> means the security whose ticker is FPT, never a
/// company whose name happens to contain those letters. Ticker matches
/// therefore outrank every name match, and an exact match outranks a prefix of
/// itself.
/// </para>
/// <para>
/// Matching on an external identifier ranks last, and deliberately so. It is
/// an exact match on an ISIN, a FIGI or a provider symbol, which sounds strong
/// — but nobody types twelve characters of ISIN into a command bar by
/// accident, so nothing else will be competing with it. Where it does compete,
/// the query looked like a ticker or a name, and that is what the user meant.
/// </para>
/// </remarks>
public enum InstrumentMatchKind
{
    /// <summary>The query is exactly the instrument's ticker.</summary>
    ExactTicker = 1,

    /// <summary>The instrument's ticker begins with the query.</summary>
    TickerPrefix = 2,

    /// <summary>The query is exactly the instrument's name.</summary>
    ExactName = 3,

    /// <summary>The instrument's name begins with the query.</summary>
    NamePrefix = 4,

    /// <summary>The instrument's name contains the query.</summary>
    NameContains = 5,

    /// <summary>
    /// The query is exactly one of the instrument's external identifiers.
    /// </summary>
    IdentifierExact = 6,
}
