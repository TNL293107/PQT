using Microsoft.EntityFrameworkCore;
using PersonalQuant.Application.Coverage;
using PersonalQuant.Application.Universes;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ICoverageRepository"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every read is an aggregate the database computes, and none is tracked.
/// Coverage is answered for an instrument whose history may run to thousands of
/// bars, and the answer is a handful of rows; reading the history to count it
/// would make a terminal read cost what an export costs.
/// </para>
/// <para>
/// Converted properties — <see cref="SourceCode"/>, the run identifier — are
/// grouped and ordered as themselves, never through a member, for the reason
/// <see cref="BarRepository"/> records: EF can translate the column and cannot
/// translate a member of the type it converts to.
/// </para>
/// </remarks>
/// <param name="dbContext">The context.</param>
internal sealed class CoverageRepository(PersonalQuantDbContext dbContext) : ICoverageRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<BarSeriesExtent>> SummariseBarsAsync(
        InstrumentId instrumentId,
        CancellationToken cancellationToken = default)
    {
        // The primary key leads with instrument and resolution, so this is one
        // range scan of the instrument's rows, grouped as it goes.
        var rows = await dbContext.Bars
            .AsNoTracking()
            .Where(bar => bar.InstrumentId == instrumentId)
            .GroupBy(bar => new { bar.Interval, bar.Source })
            .Select(group => new
            {
                group.Key.Interval,
                group.Key.Source,
                Bars = group.Count(),
                First = group.Min(bar => bar.OpenedAtUtc),
                Last = group.Max(bar => bar.OpenedAtUtc),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. rows
                .Select(row => new BarSeriesExtent(
                    row.Interval, row.Source, row.Bars, row.First, row.Last))
                .OrderBy(extent => (int)extent.Interval)
                .ThenBy(extent => extent.Source.Value, StringComparer.Ordinal),
        ];
    }

    /// <inheritdoc />
    public async Task<int> CountDailyBarsOnAsync(
        InstrumentId instrumentId,
        IReadOnlyCollection<DateOnly> sessions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        if (sessions.Count == 0)
        {
            return 0;
        }

        // One array parameter, compared with = ANY. A decade of sessions is a
        // few thousand values, which is a parameter rather than a query per day.
        var openings = sessions
            .Select(session => new DateTimeOffset(session.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero))
            .ToArray();

        return await dbContext.Bars
            .AsNoTracking()
            .CountAsync(
                bar =>
                    bar.InstrumentId == instrumentId
                    && bar.Interval == BarInterval.OneDay
                    && openings.Contains(bar.OpenedAtUtc),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<BarInterval, int>> CountOpenFindingsAsync(
        InstrumentId instrumentId,
        CancellationToken cancellationToken = default)
    {
        var counts = await dbContext.DataQualityIssues
            .AsNoTracking()
            .Where(issue =>
                issue.InstrumentId == instrumentId
                && issue.Status == DataQualityIssueStatus.Open)
            .GroupBy(issue => issue.Interval)
            .Select(group => new { Interval = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return counts.ToDictionary(row => row.Interval, row => row.Count);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IngestionAttempt>> ListLatestAttemptsAsync(
        InstrumentId instrumentId,
        CancellationToken cancellationToken = default)
    {
        // The newest run of each resolution, chosen in the database. Ties on
        // the start instant break by identifier, which is time-ordered, the
        // same way the ingestion history endpoint orders them.
        var latest = await dbContext.IngestionRuns
            .AsNoTracking()
            .Where(run => run.InstrumentId == instrumentId)
            .GroupBy(run => run.Interval)
            .Select(group => group
                .OrderByDescending(run => run.StartedAtUtc)
                .ThenByDescending(run => run.Id)
                .First())
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. latest
                .Select(run => new IngestionAttempt(
                    run.Interval, run.Source, run.StartedAtUtc, run.CompletedAtUtc, run.Outcome))
                .OrderBy(attempt => (int)attempt.Interval),
        ];
    }

    /// <inheritdoc />
    public async Task<CorporateActionExtent> SummariseCorporateActionsAsync(
        InstrumentId instrumentId,
        CancellationToken cancellationToken = default)
    {
        var summary = await dbContext.CorporateActions
            .AsNoTracking()
            .Where(action => action.InstrumentId == instrumentId)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Count = group.Count(),
                Cancelled = group.Count(action => action.IsCancelled),
                Undated = group.Count(action => action.AnnouncedOn == null),
                First = group.Min(action => (DateOnly?)action.ExDate),
                Last = group.Max(action => (DateOnly?)action.ExDate),
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return summary is null
            ? CorporateActionExtent.None
            : new CorporateActionExtent(
                summary.Count, summary.Cancelled, summary.Undated, summary.First, summary.Last);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InstrumentUniverseSpell>> ListUniverseSpellsAsync(
        InstrumentId instrumentId,
        CancellationToken cancellationToken = default) =>
        await dbContext.UniverseMemberships
            .AsNoTracking()
            .Where(membership => membership.InstrumentId == instrumentId)
            .OrderBy(membership => membership.UniverseId)
            .ThenBy(membership => membership.EffectiveFrom)
            .Select(membership => new InstrumentUniverseSpell(
                membership.UniverseId,
                new UniverseSpell(
                    membership.InstrumentId,
                    membership.EffectiveFrom,
                    membership.EffectiveTo,
                    membership.AnnouncedOn,
                    membership.RemovalAnnouncedOn)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}
