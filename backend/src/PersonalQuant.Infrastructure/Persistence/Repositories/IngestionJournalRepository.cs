using Microsoft.EntityFrameworkCore;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IIngestionJournal"/>.
/// </summary>
/// <remarks>
/// All three tables are staged through the same context, which is what lets a
/// run commit its payload, its bars, its checkpoint and its audit row in one
/// transaction. Splitting them across contexts would make the partial-failure
/// case — a checkpoint that survives while the bars it covers do not —
/// reachable.
/// </remarks>
/// <param name="dbContext">The unit of work to read and stage through.</param>
internal sealed class IngestionJournalRepository(PersonalQuantDbContext dbContext)
    : IIngestionJournal
{
    /// <inheritdoc />
    public void AddRawBatch(RawMarketDataBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        dbContext.RawMarketDataBatches.Add(batch);
    }

    /// <inheritdoc />
    public void AddRun(IngestionRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        // Idempotent against a run already being tracked: the pipeline stages
        // the record once and then closes it, and a second Add of a tracked
        // entity would be a no-op anyway. Guarding explicitly keeps that from
        // depending on EF's change-tracker behaviour.
        if (dbContext.Entry(run).State == EntityState.Detached)
        {
            dbContext.IngestionRuns.Add(run);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IngestionRun>> ListRecentRunsAsync(
        InstrumentId instrumentId,
        BarInterval interval,
        int limit,
        CancellationToken cancellationToken = default) =>
        await dbContext.IngestionRuns
            .AsNoTracking()
            .Where(run => run.InstrumentId == instrumentId && run.Interval == interval)
            .OrderByDescending(run => run.StartedAtUtc)
            .ThenByDescending(run => run.Id)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public Task<IngestionCheckpoint?> FindCheckpointAsync(
        InstrumentId instrumentId,
        BarInterval interval,
        SourceCode source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        // Tracked: the caller advances the position in the same unit of work
        // that stores the bars it covers.
        return dbContext.IngestionCheckpoints.FirstOrDefaultAsync(
            checkpoint =>
                checkpoint.InstrumentId == instrumentId
                && checkpoint.Interval == interval
                && checkpoint.Source == source,
            cancellationToken);
    }

    /// <inheritdoc />
    public void AddCheckpoint(IngestionCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        dbContext.IngestionCheckpoints.Add(checkpoint);
    }
}
