using PersonalQuant.Domain.Currencies;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.Application.Instruments;

/// <summary>
/// One instrument as returned by search or resolution.
/// </summary>
/// <remarks>
/// <para>
/// A projection, not the <see cref="Instrument"/> aggregate. Search results
/// are read-only, are needed in quantity, and have to carry the exchange's
/// code rather than its surrogate key so a human can tell two listings apart —
/// none of which the aggregate should be reshaped to provide.
/// </para>
/// <para>
/// It carries <see cref="InstrumentId"/> because that is what callers act on.
/// The ticker is for the user to read; every module downstream of a selection
/// joins on the identifier.
/// </para>
/// <para>
/// There is deliberately no price on this type. Market data arrives in Phase
/// 2, and a nullable price field that is always null would only invite a UI
/// that pretends otherwise.
/// </para>
/// </remarks>
/// <param name="InstrumentId">The canonical internal identifier.</param>
/// <param name="Ticker">The exchange ticker.</param>
/// <param name="Name">The security name, in its original spelling.</param>
/// <param name="AssetType">The broad asset class.</param>
/// <param name="ExchangeCode">The venue's operating code.</param>
/// <param name="Currency">The quote currency.</param>
/// <param name="Status">The lifecycle state.</param>
/// <param name="MatchKind">
/// Why this instrument matched, and how strongly. Null when the instrument was
/// not reached through a search — a direct read by identifier, or a symbol
/// resolution, did not rank anything against anything.
/// </param>
public sealed record InstrumentSearchResult(
    InstrumentId InstrumentId,
    Ticker Ticker,
    string Name,
    AssetType AssetType,
    ExchangeCode ExchangeCode,
    CurrencyCode Currency,
    InstrumentStatus Status,
    InstrumentMatchKind? MatchKind);
