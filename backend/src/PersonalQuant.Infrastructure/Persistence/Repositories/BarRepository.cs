using Microsoft.EntityFrameworkCore;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IBarRepository"/>.
/// </summary>
/// <remarks>
/// Every query here filters on the leading columns of the primary key —
/// instrument, then interval, then period — so each one is an index range
/// scan rather than a table scan. The bars table is the only one in the system
/// that grows without bound, and it is the one where a query that works on a
/// developer's laptop is a query that stops working in a year.
/// </remarks>
/// <param name="dbContext">The unit of work to read and stage through.</param>
internal sealed class BarRepository(PersonalQuantDbContext dbContext) : IBarRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<OhlcvBar>> ListForUpdateAsync(
        InstrumentId instrumentId,
        BarInterval interval,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default) =>
        // Tracked on purpose. The ingestion pipeline restates bars it already
        // holds, and a restatement applied to a detached entity would be
        // computed correctly and never written.
        await dbContext.Bars
            .Where(bar =>
                bar.InstrumentId == instrumentId
                && bar.Interval == interval
                && bar.OpenedAtUtc >= fromUtc
                && bar.OpenedAtUtc < toUtc)
            .OrderBy(bar => bar.OpenedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<OhlcvBar>> QueryAsync(
        BarQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var bars = dbContext.Bars
            .AsNoTracking()
            .Where(bar => bar.InstrumentId == query.InstrumentId && bar.Interval == query.Interval);

        if (query.FromUtc is { } fromUtc)
        {
            bars = bars.Where(bar => bar.OpenedAtUtc >= fromUtc);
        }

        if (query.ToUtc is { } toUtc)
        {
            bars = bars.Where(bar => bar.OpenedAtUtc < toUtc);
        }

        // The bound is applied from the newest end, because "the last 300
        // days" is what a chart asks for. Taking from the oldest end would
        // return the start of the instrument's history and quietly omit
        // everything the caller wanted.
        var newestFirst = await bars
            .OrderByDescending(bar => bar.OpenedAtUtc)
            .Take(query.Limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        newestFirst.Reverse();

        return newestFirst;
    }

    /// <inheritdoc />
    public Task<OhlcvBar?> FindLastBeforeAsync(
        InstrumentId instrumentId,
        BarInterval interval,
        DateTimeOffset beforeUtc,
        CancellationToken cancellationToken = default) =>
        // Uses the leading columns of the primary key, so it is a single index
        // seek backwards rather than a scan of the instrument's history.
        dbContext.Bars
            .AsNoTracking()
            .Where(bar =>
                bar.InstrumentId == instrumentId
                && bar.Interval == interval
                && bar.OpenedAtUtc < beforeUtc)
            .OrderByDescending(bar => bar.OpenedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<BarRevision>> QueryAsOfAsync(
        BarQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.KnownAsOfUtc is not { } knownAsOfUtc)
        {
            throw new InvalidOperationException(
                "An as-of read requires a known-as-of instant on the query.");
        }

        var revisions = dbContext.BarRevisions
            .AsNoTracking()
            .Where(revision =>
                revision.InstrumentId == query.InstrumentId
                && revision.Interval == query.Interval
                // The half-open window. Inclusive at the instant the statement
                // was first held, exclusive at the instant it was superseded,
                // so adjacent revisions never both match and no instant falls
                // between two of them.
                && revision.ObservedFromUtc <= knownAsOfUtc
                && (revision.ObservedToUtc == null || revision.ObservedToUtc > knownAsOfUtc));

        if (query.FromUtc is { } fromUtc)
        {
            revisions = revisions.Where(revision => revision.OpenedAtUtc >= fromUtc);
        }

        if (query.ToUtc is { } toUtc)
        {
            revisions = revisions.Where(revision => revision.OpenedAtUtc < toUtc);
        }

        // Bounded from the newest period, for the same reason the current-value
        // read is: a chart asks for the last N periods, and taking from the
        // oldest end would silently return the start of the instrument's life.
        var newestFirst = await revisions
            .OrderByDescending(revision => revision.OpenedAtUtc)
            .Take(query.Limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        newestFirst.Reverse();

        return newestFirst;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BarRevision>> ListOpenRevisionsForUpdateAsync(
        InstrumentId instrumentId,
        BarInterval interval,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default) =>
        // Tracked, like the bars they accompany: a restatement closes the open
        // window in place, and a change applied to a detached entity would be
        // computed correctly and never written.
        await dbContext.BarRevisions
            .Where(revision =>
                revision.InstrumentId == instrumentId
                && revision.Interval == interval
                && revision.OpenedAtUtc >= fromUtc
                && revision.OpenedAtUtc < toUtc
                && revision.ObservedToUtc == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<SourceCode>> ListSourcesAsync(
        InstrumentId instrumentId,
        BarInterval interval,
        CancellationToken cancellationToken = default)
    {
        // Distinct in the database rather than in memory. The alternative is
        // reading every bar of a fifteen-year series to learn one fact about
        // it, and this runs before every ingestion.
        var codes = await dbContext.Bars
            .AsNoTracking()
            .Where(bar => bar.InstrumentId == instrumentId && bar.Interval == interval)
            .Select(bar => bar.Source.Value)
            .Distinct()
            .OrderBy(code => code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. codes.Select(SourceCode.Create)];
    }

    /// <inheritdoc />
    public void AddRange(IReadOnlyList<OhlcvBar> bars)
    {
        ArgumentNullException.ThrowIfNull(bars);

        if (bars.Count == 0)
        {
            return;
        }

        dbContext.Bars.AddRange(bars);
    }

    /// <inheritdoc />
    public void AddRevisions(IReadOnlyList<BarRevision> revisions)
    {
        ArgumentNullException.ThrowIfNull(revisions);

        if (revisions.Count == 0)
        {
            return;
        }

        dbContext.BarRevisions.AddRange(revisions);
    }
}
