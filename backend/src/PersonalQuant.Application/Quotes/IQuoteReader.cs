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

    /// <summary>
    /// Reads every equity the instrument master holds on a venue, with its
    /// listing lifecycle, whatever its status.
    /// </summary>
    /// <remarks>
    /// The venue an instrument is on <em>now</em>: no venue history is recorded,
    /// so a name that moved here is read as having always been here.
    /// </remarks>
    /// <param name="exchangeId">The venue.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The equities, in no particular order.</returns>
    Task<IReadOnlyList<ListedEquity>> ListVenueEquitiesAsync(
        ExchangeId exchangeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Says which of the instruments named hold at least one daily bar.
    /// </summary>
    /// <param name="instrumentIds">The instruments.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The instruments with a daily bar on some session.</returns>
    Task<IReadOnlySet<InstrumentId>> ListWithDailyBarsAsync(
        IReadOnlyCollection<InstrumentId> instrumentIds,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// An equity on a venue and the dates its listing began and ended, when recorded.
/// </summary>
/// <param name="Instrument">The instrument as a quote names it.</param>
/// <param name="Status">Its listing state now.</param>
/// <param name="ListedOn">Its first trading day, or null when nobody recorded it.</param>
/// <param name="DelistedOn">Its last trading day, or null while it trades.</param>
public sealed record ListedEquity(
    QuoteInstrument Instrument,
    InstrumentStatus Status,
    DateOnly? ListedOn,
    DateOnly? DelistedOn)
{
    /// <summary>
    /// Gets a value indicating whether it was listed on a session, as far as
    /// its record says.
    /// </summary>
    /// <remarks>
    /// A pending record never traded. A missing listing date is read as
    /// "listed" — the record says it is — and counted apart by the caller, so
    /// the assumption is visible. The delisting date is the last day it
    /// traded, so a session after it is outside.
    /// </remarks>
    /// <param name="session">The session.</param>
    /// <returns><see langword="true"/> when it belongs to the venue that day.</returns>
    public bool ListedOnSession(DateOnly session) =>
        Status != InstrumentStatus.Pending
        && (ListedOn is not { } from || from <= session)
        && (DelistedOn is not { } last || session <= last);
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
