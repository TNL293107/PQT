using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.Application.Instruments;

/// <summary>
/// Reads and records instrument master data.
/// </summary>
/// <remarks>
/// <para>
/// There is deliberately no delete. Instrument master data is historical
/// reference data that prices, fundamentals and positions join against;
/// removing a row would silently invalidate every series built on it. An
/// instrument that stops trading is delisted, which is a state change.
/// </para>
/// <para>
/// Provider symbol aliases and related-instrument lookups are separate
/// workstreams and are not part of this interface yet.
/// </para>
/// </remarks>
public interface IInstrumentRepository
{
    /// <summary>Finds an instrument by its canonical identifier.</summary>
    /// <param name="id">The identifier to look up.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The instrument, or <see langword="null"/> when unknown.</returns>
    Task<Instrument?> FindByIdAsync(InstrumentId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the instrument currently occupying a ticker on an exchange.
    /// </summary>
    /// <remarks>
    /// Delisted instruments are excluded. A ticker can be reassigned to a
    /// different issuer once released, so only one instrument holds it at a
    /// time, and looking up by ticker alone would be ambiguous across history.
    /// </remarks>
    /// <param name="exchangeId">The venue to search.</param>
    /// <param name="ticker">The ticker to look up.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The active instrument, or <see langword="null"/> when none holds the ticker.</returns>
    Task<Instrument?> FindActiveByTickerAsync(
        ExchangeId exchangeId,
        Ticker ticker,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every instrument that has ever held a ticker on an exchange,
    /// including delisted ones, most recently created first.
    /// </summary>
    /// <remarks>
    /// Exists so that reassignment of a ticker can be audited rather than
    /// discovered by surprise.
    /// </remarks>
    /// <param name="exchangeId">The venue to search.</param>
    /// <param name="ticker">The ticker to look up.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Every instrument that has held the ticker.</returns>
    Task<IReadOnlyList<Instrument>> ListTickerHistoryAsync(
        ExchangeId exchangeId,
        Ticker ticker,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports whether a ticker is currently taken on an exchange.
    /// </summary>
    /// <param name="exchangeId">The venue to check.</param>
    /// <param name="ticker">The ticker to check.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><see langword="true"/> when an active instrument holds the ticker.</returns>
    Task<bool> IsTickerTakenAsync(
        ExchangeId exchangeId,
        Ticker ticker,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns instruments matching a query, strongest match first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ranking, filtering and the result bound are all applied by the
    /// database. Reading the instrument master into memory to filter it there
    /// would work today and stop working the moment the table is the size it
    /// is meant to be.
    /// </para>
    /// <para>
    /// The order is total — match kind, then ticker, then identifier — so the
    /// result never depends on the order rows happen to come back in.
    /// </para>
    /// </remarks>
    /// <param name="criteria">The validated query.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Ranked results, bounded by the criteria's limit.</returns>
    Task<IReadOnlyList<InstrumentSearchResult>> SearchAsync(
        InstrumentSearchCriteria criteria,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every active instrument holding a ticker, across all venues.
    /// </summary>
    /// <remarks>
    /// Returns more than one row when the same ticker is live on two
    /// exchanges, which is what makes symbol resolution able to report
    /// ambiguity instead of guessing.
    /// </remarks>
    /// <param name="ticker">The ticker to look up.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Every active holder of the ticker, ordered by exchange code.</returns>
    Task<IReadOnlyList<InstrumentSearchResult>> ListActiveByTickerAsync(
        Ticker ticker,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads one instrument as a search result, including its exchange code.
    /// </summary>
    /// <param name="id">The identifier to look up.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The instrument, or <see langword="null"/> when unknown.</returns>
    Task<InstrumentSearchResult?> FindResultByIdAsync(
        InstrumentId id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stages a new instrument. Call
    /// <see cref="Abstractions.IUnitOfWork.SaveChangesAsync"/> to persist it.
    /// </summary>
    /// <param name="instrument">The instrument to add.</param>
    void Add(Instrument instrument);
}
