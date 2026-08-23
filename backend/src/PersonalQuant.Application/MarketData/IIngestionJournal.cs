using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Application.MarketData;

/// <summary>
/// The ingestion pipeline's own bookkeeping: raw payloads, run history and
/// resume positions.
/// </summary>
/// <remarks>
/// <para>
/// One port rather than three, because the three are written in one
/// transaction by one caller and are never useful apart. A run that stored
/// bars but failed to record that it did, or advanced a checkpoint past data
/// it did not persist, is worse than a run that failed outright — the first
/// leaves a hole nothing will ever notice.
/// </para>
/// <para>
/// Nothing here is read by code that computes on prices. It exists to answer
/// "where did this come from, when, and what happened", which is a different
/// question from "what is the series".
/// </para>
/// </remarks>
public interface IIngestionJournal
{
    /// <summary>
    /// Stages a retained provider response.
    /// </summary>
    /// <param name="batch">The payload to retain.</param>
    void AddRawBatch(RawMarketDataBatch batch);

    /// <summary>
    /// Stages an audit record.
    /// </summary>
    /// <param name="run">The run to record.</param>
    void AddRun(IngestionRun run);

    /// <summary>
    /// Reads the most recent runs for an instrument and resolution, newest
    /// first.
    /// </summary>
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

    /// <summary>
    /// Summarises the runs over a range, for quality scoring.
    /// </summary>
    /// <remarks>
    /// Aggregated in the database. Scoring a series means counting outcomes
    /// over a window, and materialising every audit row to count them in memory
    /// would make a dashboard read proportional to how long the series has been
    /// ingested for.
    /// </remarks>
    /// <param name="instrumentId">The instrument.</param>
    /// <param name="interval">The resolution.</param>
    /// <param name="fromUtc">The inclusive start of the range.</param>
    /// <param name="toUtc">The exclusive end of the range.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>What the runs in the range did.</returns>
    Task<IngestionSummary> SummariseRunsAsync(
        InstrumentId instrumentId,
        BarInterval interval,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the resume position for an instrument, resolution and source.
    /// </summary>
    /// <remarks>
    /// Tracked rather than read-only: the caller advances it in the same unit
    /// of work that stores the bars it covers.
    /// </remarks>
    /// <param name="instrumentId">The instrument.</param>
    /// <param name="interval">The resolution.</param>
    /// <param name="source">The provider.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The checkpoint, or <see langword="null"/> when nothing has been ingested yet.</returns>
    Task<IngestionCheckpoint?> FindCheckpointAsync(
        InstrumentId instrumentId,
        BarInterval interval,
        SourceCode source,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stages a new resume position.
    /// </summary>
    /// <param name="checkpoint">The checkpoint to add.</param>
    void AddCheckpoint(IngestionCheckpoint checkpoint);
}

/// <summary>
/// What the ingestion runs over one window did, in aggregate.
/// </summary>
/// <remarks>
/// Skipped runs are counted but are neither successes nor failures. A run that
/// found nothing to ask for did not demonstrate that the source works, and
/// counting it as a success would let a schedule that has quietly stopped
/// requesting anything report perfect reliability.
/// </remarks>
/// <param name="Runs">Runs recorded.</param>
/// <param name="Succeeded">Runs that completed.</param>
/// <param name="Failed">Runs that could not read the source.</param>
/// <param name="Skipped">Runs that had nothing to ask for.</param>
/// <param name="BarsFetched">Rows the sources returned.</param>
/// <param name="BarsAccepted">Rows that passed validation.</param>
/// <param name="BarsRejected">Rows validation refused.</param>
public sealed record IngestionSummary(
    int Runs,
    int Succeeded,
    int Failed,
    int Skipped,
    int BarsFetched,
    int BarsAccepted,
    int BarsRejected)
{
    /// <summary>A window in which nothing ran.</summary>
    public static IngestionSummary None { get; } = new(0, 0, 0, 0, 0, 0, 0);
}
