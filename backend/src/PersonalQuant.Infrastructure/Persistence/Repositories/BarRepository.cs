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
    public void AddRange(IReadOnlyList<OhlcvBar> bars)
    {
        ArgumentNullException.ThrowIfNull(bars);

        if (bars.Count == 0)
        {
            return;
        }

        dbContext.Bars.AddRange(bars);
    }
}
