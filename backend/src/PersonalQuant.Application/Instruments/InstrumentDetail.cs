using PersonalQuant.Application.Classification;
using PersonalQuant.Domain.Currencies;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.Application.Instruments;

/// <summary>
/// Everything the instrument master knows about one security.
/// </summary>
/// <remarks>
/// <para>
/// The read behind a security's reference page, and deliberately richer than
/// <see cref="InstrumentSearchResult"/>. Search returns many rows on every
/// keystroke and pays for every column it carries; this returns one row when a
/// user has already chosen what they want to look at, so it can afford the
/// joins that make the answer complete.
/// </para>
/// <para>
/// Still no price. Market data arrives in Phase 2 and is a series, not an
/// attribute of identity.
/// </para>
/// </remarks>
/// <param name="InstrumentId">The canonical internal identifier.</param>
/// <param name="Ticker">The exchange ticker.</param>
/// <param name="Name">The security name, in its original spelling.</param>
/// <param name="AssetType">The broad asset class.</param>
/// <param name="ExchangeCode">The venue's operating code.</param>
/// <param name="ExchangeName">The venue's full name.</param>
/// <param name="Currency">The quote currency.</param>
/// <param name="Status">The lifecycle state.</param>
/// <param name="ListedOn">The first trading date, when it has been sourced.</param>
/// <param name="DelistedOn">The last trading date, once delisted.</param>
/// <param name="Classification">
/// The sector and industry, or <see langword="null"/> when the security is
/// unclassified — an index, a fund, or a record no mapping covers yet.
/// </param>
/// <param name="Aliases">
/// Every identifier an outside system knows this instrument by. Empty until a
/// provider import has run, which is the ordinary state of a seeded record.
/// </param>
public sealed record InstrumentDetail(
    InstrumentId InstrumentId,
    Ticker Ticker,
    string Name,
    AssetType AssetType,
    ExchangeCode ExchangeCode,
    string ExchangeName,
    CurrencyCode Currency,
    InstrumentStatus Status,
    DateOnly? ListedOn,
    DateOnly? DelistedOn,
    InstrumentClassification? Classification,
    IReadOnlyList<InstrumentAlias> Aliases);
