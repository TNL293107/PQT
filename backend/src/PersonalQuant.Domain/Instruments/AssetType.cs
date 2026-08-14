namespace PersonalQuant.Domain.Instruments;

/// <summary>
/// The broad class of a tradable instrument.
/// </summary>
/// <remarks>
/// A closed enumeration rather than a reference table: the set is small,
/// changes on the scale of years, and every value carries behaviour the code
/// must switch on. Values are explicitly numbered because they are persisted.
/// </remarks>
public enum AssetType
{
    /// <summary>Not classified. Valid only for a record still being imported.</summary>
    Unspecified = 0,

    /// <summary>Ordinary shares.</summary>
    Equity = 1,

    /// <summary>Exchange-traded fund.</summary>
    Etf = 2,

    /// <summary>A calculated index, such as VN30. Not directly tradable.</summary>
    Index = 3,

    /// <summary>A listed futures contract.</summary>
    Futures = 4,

    /// <summary>A covered warrant.</summary>
    CoveredWarrant = 5,

    /// <summary>A listed bond.</summary>
    Bond = 6,
}
