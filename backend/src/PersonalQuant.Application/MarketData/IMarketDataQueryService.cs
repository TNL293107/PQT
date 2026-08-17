using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Application.MarketData;

/// <summary>
/// Reads stored market data and the record of how it got there.
/// </summary>
/// <remarks>
/// The read side of the phase, kept apart from
/// <see cref="IMarketDataIngestionService"/>. Reading a series happens on
/// every chart draw and must not be able to trigger a fetch; writing happens
/// on a schedule and must not be reachable from a query.
/// </remarks>
public interface IMarketDataQueryService
{
    /// <summary>
    /// Reads a bounded window of one series.
    /// </summary>
    /// <param name="query">The validated query.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The series, empty when nothing has been ingested.</returns>
    Task<BarSeries> GetSeriesAsync(BarQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the recent ingestion history for an instrument and resolution.
    /// </summary>
    /// <remarks>
    /// The answer to "why does this series stop on Tuesday?". Without it, a
    /// gap and a market holiday are the same absence of rows.
    /// </remarks>
    /// <param name="instrumentId">The instrument.</param>
    /// <param name="interval">The resolution.</param>
    /// <param name="limit">How many runs to return.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The runs, newest first.</returns>
    Task<IReadOnlyList<IngestionRun>> ListRecentRunsAsync(
        InstrumentId instrumentId,
        BarInterval interval,
        int limit,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A window of one instrument's bars at one resolution.
/// </summary>
/// <remarks>
/// The bars are ordered oldest first, always. Every indicator, every
/// aggregation and every chart assumes it, and a series whose order depended
/// on how it was queried would produce quietly different answers depending on
/// the caller.
/// </remarks>
/// <param name="InstrumentId">The instrument the bars belong to.</param>
/// <param name="Interval">The resolution.</param>
/// <param name="Bars">The bars, oldest first.</param>
public sealed record BarSeries(
    InstrumentId InstrumentId,
    BarInterval Interval,
    IReadOnlyList<OhlcvBar> Bars)
{
    /// <summary>Gets the opening instant of the oldest bar, if any.</summary>
    public DateTimeOffset? FirstOpenedAtUtc => Bars.Count == 0 ? null : Bars[0].OpenedAtUtc;

    /// <summary>Gets the opening instant of the newest bar, if any.</summary>
    public DateTimeOffset? LastOpenedAtUtc => Bars.Count == 0 ? null : Bars[^1].OpenedAtUtc;
}

/// <summary>
/// Default <see cref="IMarketDataQueryService"/>.
/// </summary>
/// <param name="bars">The canonical series.</param>
/// <param name="journal">The ingestion record.</param>
internal sealed class MarketDataQueryService(IBarRepository bars, IIngestionJournal journal)
    : IMarketDataQueryService
{
    /// <summary>Most runs a caller may ask for in one read.</summary>
    private const int MaxRuns = 50;

    /// <inheritdoc />
    public async Task<BarSeries> GetSeriesAsync(
        BarQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var results = await bars.QueryAsync(query, cancellationToken).ConfigureAwait(false);

        return new BarSeries(query.InstrumentId, query.Interval, results);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<IngestionRun>> ListRecentRunsAsync(
        InstrumentId instrumentId,
        BarInterval interval,
        int limit,
        CancellationToken cancellationToken = default) =>
        instrumentId.IsEmpty
            ? Task.FromResult<IReadOnlyList<IngestionRun>>([])
            : journal.ListRecentRunsAsync(
                instrumentId, interval, Math.Clamp(limit, 1, MaxRuns), cancellationToken);
}
