using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Application.MarketData;

/// <summary>
/// Reads and records what the quality rules found.
/// </summary>
/// <remarks>
/// Separate from <see cref="IBarRepository"/> because the two are read by
/// different callers for different reasons. A chart reads bars and never reads
/// findings; a data-quality review reads findings and rarely reads bars.
/// </remarks>
public interface IDataQualityRepository
{
    /// <summary>
    /// Lists the findings recorded against a series over a range, whatever
    /// their status.
    /// </summary>
    /// <remarks>
    /// Status is deliberately not filtered here. The inspector needs to see
    /// dismissed findings too, or it would raise them again on the next run and
    /// undo the dismissal.
    /// </remarks>
    /// <param name="instrumentId">The instrument.</param>
    /// <param name="interval">The resolution.</param>
    /// <param name="fromUtc">The inclusive start of the range.</param>
    /// <param name="toUtc">The exclusive end of the range.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The findings, oldest session first.</returns>
    Task<IReadOnlyList<DataQualityIssue>> ListAsync(
        InstrumentId instrumentId,
        BarInterval interval,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the open findings for a series, newest session first.
    /// </summary>
    /// <param name="instrumentId">The instrument.</param>
    /// <param name="interval">The resolution.</param>
    /// <param name="limit">How many to return.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The open findings.</returns>
    Task<IReadOnlyList<DataQualityIssue>> ListOpenAsync(
        InstrumentId instrumentId,
        BarInterval interval,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts open findings by kind over a range.
    /// </summary>
    /// <param name="instrumentId">The instrument.</param>
    /// <param name="interval">The resolution.</param>
    /// <param name="fromUtc">The inclusive start of the range.</param>
    /// <param name="toUtc">The exclusive end of the range.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>How many open findings of each kind the range holds.</returns>
    Task<IReadOnlyDictionary<DataQualityIssueKind, int>> CountOpenByKindAsync(
        InstrumentId instrumentId,
        BarInterval interval,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds one finding by its identifier, tracked so it can be resolved.
    /// </summary>
    /// <param name="id">The identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The finding, or <see langword="null"/> when unknown.</returns>
    Task<DataQualityIssue?> FindAsync(
        DataQualityIssueId id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stages a new finding. Call
    /// <see cref="Abstractions.IUnitOfWork.SaveChangesAsync"/> to persist it.
    /// </summary>
    /// <param name="issue">The finding to add.</param>
    void Add(DataQualityIssue issue);
}
