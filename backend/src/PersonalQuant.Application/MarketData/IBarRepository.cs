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
    /// Reads the newest bar held, if any.
    /// </summary>
    /// <param name="instrumentId">The instrument.</param>
    /// <param name="interval">The resolution.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The newest bar, or <see langword="null"/> when the series is empty.</returns>
    Task<OhlcvBar?> FindLatestAsync(
        InstrumentId instrumentId,
        BarInterval interval,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stages new bars. Call
    /// <see cref="Abstractions.IUnitOfWork.SaveChangesAsync"/> to persist them.
    /// </summary>
    /// <param name="bars">The bars to add.</param>
    void AddRange(IReadOnlyList<OhlcvBar> bars);
}
