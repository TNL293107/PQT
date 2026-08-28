using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Application.MarketData;

/// <summary>
/// Reads and records canonical bars.
/// </summary>
/// <remarks>
/// <para>
/// The canonical series, not the raw payloads it was derived from. Those live
/// behind <see cref="IIngestionJournal"/> and are never read by anything that
/// computes on prices.
/// </para>
/// <para>
/// There is no delete, and no update beyond a restatement the aggregate
/// itself applies. Removing bars is how a backtest silently starts running on
/// a different history than the one it was validated against.
/// </para>
/// </remarks>
public interface IBarRepository
{
    /// <summary>
    /// Reads the bars already held for a range.
    /// </summary>
    /// <remarks>
    /// Tracked, not read-only: the ingestion pipeline uses this to decide
    /// between storing a new period and restating one it already has, and a
    /// restatement has to be written back through the same entity.
    /// </remarks>
    /// <param name="instrumentId">The instrument.</param>
    /// <param name="interval">The resolution.</param>
    /// <param name="fromUtc">The inclusive start of the range.</param>
    /// <param name="toUtc">The exclusive end of the range.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The bars held, oldest first.</returns>
    Task<IReadOnlyList<OhlcvBar>> ListForUpdateAsync(
        InstrumentId instrumentId,
        BarInterval interval,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a bounded window of a series.
    /// </summary>
    /// <remarks>
    /// Newest-first with a bound, because that is what a chart and a terminal
    /// panel ask for: the most recent N periods. The result is reversed to
    /// oldest-first before it is returned, so a caller never has to know that
    /// the bound was applied from the other end.
    /// </remarks>
    /// <param name="query">The validated query.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The bars, oldest first.</returns>
    Task<IReadOnlyList<OhlcvBar>> QueryAsync(
        BarQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the last bar that opened strictly before an instant.
    /// </summary>
    /// <remarks>
    /// The close a corporate action is measured against: the price the market
    /// last saw with the entitlement attached. Strictly before, because a bar
    /// opening on the ex-date is already trading without it.
    /// </remarks>
    /// <param name="instrumentId">The instrument.</param>
    /// <param name="interval">The resolution.</param>
    /// <param name="beforeUtc">The instant to look back from, exclusive.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The bar, or <see langword="null"/> when nothing precedes it.</returns>
    Task<OhlcvBar?> FindLastBeforeAsync(
        InstrumentId instrumentId,
        BarInterval interval,
        DateTimeOffset beforeUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a bounded window of a series as it was believed at an instant.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reads the observation history rather than the current projection, and
    /// returns for each period the one statement whose observation window
    /// covers <see cref="BarQuery.KnownAsOfUtc"/>.
    /// </para>
    /// <para>
    /// A period first observed after that instant contributes nothing. It is
    /// not filled from the current value: a bar the system had not yet seen is
    /// absent, and pretending otherwise is the leak point-in-time reads exist
    /// to close.
    /// </para>
    /// </remarks>
    /// <param name="query">The validated query, carrying a non-null as-of.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The statements held at that instant, oldest period first.</returns>
    Task<IReadOnlyList<BarRevision>> QueryAsOfAsync(
        BarQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the statements already held for a range, for restatement.
    /// </summary>
    /// <remarks>
    /// Tracked, and only the open ones: the ingestion pipeline closes the
    /// current statement when a source restates a period, and a closed window
    /// is never reopened.
    /// </remarks>
    /// <param name="instrumentId">The instrument.</param>
    /// <param name="interval">The resolution.</param>
    /// <param name="fromUtc">The inclusive start of the range.</param>
    /// <param name="toUtc">The exclusive end of the range.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The open revision of each period held in the range.</returns>
    Task<IReadOnlyList<BarRevision>> ListOpenRevisionsForUpdateAsync(
        InstrumentId instrumentId,
        BarInterval interval,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stages new bars. Call
    /// <see cref="Abstractions.IUnitOfWork.SaveChangesAsync"/> to persist them.
    /// </summary>
    /// <param name="bars">The bars to add.</param>
    void AddRange(IReadOnlyList<OhlcvBar> bars);

    /// <summary>
    /// Stages observation-history snapshots alongside the bars they describe.
    /// </summary>
    /// <remarks>
    /// Staged through the same unit of work as the bars, so the current
    /// projection and its history commit together or not at all. A history
    /// that could commit without its bar would be a record of something that
    /// never happened.
    /// </remarks>
    /// <param name="revisions">The revisions to add.</param>
    void AddRevisions(IReadOnlyList<BarRevision> revisions);
}
