using PersonalQuant.Domain.CorporateActions;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Application.Quotes;

/// <summary>
/// Reads what quoting a set of instruments on a session needs, for the whole
/// set at once.
/// </summary>
/// <remarks>
/// <para>
/// A port of its own rather than three more methods on the instrument, bar and
/// corporate action repositories. Each of those reads one instrument at a time,
/// because every caller so far wanted one series; a quote board wants two
/// sessions of thirty, and asking the per-instrument reads thirty times is the
/// N+1 the console would feel on every keystroke of <c>BREADTH</c> or <c>WL</c>.
/// </para>
/// <para>
/// Every method takes the whole set and answers in one query, so quoting costs
/// the same number of round trips for VN30 as for a venue of three hundred.
/// </para>
/// </remarks>
public interface IQuoteReader
{
    /// <summary>
    /// Reads the ticker, venue and asset type of each instrument named.
    /// </summary>
    /// <param name="instrumentIds">The instruments.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>One entry per instrument found, in no particular order.</returns>
    Task<IReadOnlyList<QuoteInstrument>> ListInstrumentsAsync(
        IReadOnlyCollection<InstrumentId> instrumentIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the daily bars of every instrument named over a window of
    /// sessions, inclusive at both ends, as currently stored.
    /// </summary>
    /// <remarks>
    /// The current series, not a point-in-time one. A quote answers "how did
    /// it move that day" with what this system now holds, as the bars endpoint
    /// does without <c>knownAsOf</c>.
    /// </remarks>
    /// <param name="instrumentIds">The instruments.</param>
    /// <param name="fromDate">The first session, inclusive.</param>
    /// <param name="toDate">The last session, inclusive.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The closes, in no particular order.</returns>
    Task<IReadOnlyList<SessionClose>> ListDailyClosesAsync(
        IReadOnlyCollection<InstrumentId> instrumentIds,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the stored adjustment factors of every instrument named whose
    /// ex-date falls in a window, inclusive at both ends.
    /// </summary>
    /// <remarks>
    /// The factors the adjustment engine already computed and the bars endpoint
    /// already applies. Nothing here derives a factor from an action: a second
    /// derivation is a second opinion about what a dividend did to a price.
    /// </remarks>
    /// <param name="instrumentIds">The instruments.</param>
    /// <param name="fromDate">The first ex-date, inclusive.</param>
    /// <param name="toDate">The last ex-date, inclusive.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The factors, in no particular order.</returns>
    Task<IReadOnlyList<ExDateFactor>> ListFactorsGoingExAsync(
        IReadOnlyCollection<InstrumentId> instrumentIds,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// An instrument as a quote names it.
/// </summary>
/// <param name="InstrumentId">The canonical identifier.</param>
/// <param name="Ticker">The ticker it trades under now.</param>
/// <param name="ExchangeId">The venue whose calendar its sessions follow.</param>
/// <param name="AssetType">What it is, which decides whether its reference is rounded to a tick.</param>
public sealed record QuoteInstrument(
    InstrumentId InstrumentId,
    Ticker Ticker,
    ExchangeId ExchangeId,
    AssetType AssetType);

/// <summary>
/// One instrument's close and volume on one session.
/// </summary>
/// <param name="InstrumentId">The instrument.</param>
/// <param name="Session">The session the daily bar belongs to.</param>
/// <param name="Close">The close, as the market printed it.</param>
/// <param name="Volume">Units traded, as the market printed them.</param>
/// <param name="Source">
/// Where the bar came from, so a source that adjusts at source is not adjusted
/// a second time.
/// </param>
public sealed record SessionClose(
    InstrumentId InstrumentId,
    DateOnly Session,
    Price Close,
    long Volume,
    SourceCode Source);

/// <summary>
/// One stored adjustment factor and the day it goes ex.
/// </summary>
/// <param name="InstrumentId">The instrument.</param>
/// <param name="ExDate">The first session that trades without the entitlement.</param>
/// <param name="Factor">What a price before the ex-date is multiplied by.</param>
public sealed record ExDateFactor(
    InstrumentId InstrumentId,
    DateOnly ExDate,
    AdjustmentFactor Factor);
