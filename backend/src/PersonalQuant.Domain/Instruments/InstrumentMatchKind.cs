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
/// Matching on external identifiers — ISIN, FIGI, provider symbols — will slot
/// in after <see cref="NameContains"/> when the alias workstream lands. It is
/// absent rather than stubbed because no alias data exists to match against.
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
}
