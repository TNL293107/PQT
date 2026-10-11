using Microsoft.EntityFrameworkCore;
using PersonalQuant.Application.Terminal;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.Terminal;

namespace PersonalQuant.Infrastructure.Persistence.Repositories;

/// <summary>EF Core implementation of <see cref="IWatchlistRepository"/>.</summary>
internal sealed class WatchlistRepository(PersonalQuantDbContext dbContext) : IWatchlistRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<WatchlistSummary>> ListAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Watchlists
            .AsNoTracking()
            .OrderBy(watchlist => watchlist.Name)
            .Select(watchlist => new WatchlistSummary(
                watchlist.Name,
                watchlist.Entries.Count,
                watchlist.CreatedAtUtc,
                watchlist.UpdatedAtUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public Task<Watchlist?> FindForUpdateAsync(string name, CancellationToken cancellationToken = default) =>
        dbContext.Watchlists
            .Include(watchlist => watchlist.Entries)
            .FirstOrDefaultAsync(watchlist => watchlist.Name == name, cancellationToken);

    /// <inheritdoc />
    public async Task<WatchlistView?> FindViewAsync(string name, CancellationToken cancellationToken = default)
    {
        var watchlist = await dbContext.Watchlists
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Name == name, cancellationToken)
            .ConfigureAwait(false);

        if (watchlist is null)
        {
            return null;
        }

        // Two queries rather than an Include joined to instruments: the entries
        // are a backing-field collection, and projecting through it together
        // with a join is the shape EF Core has translated wrongly before.
        var rows = await (
            from entry in dbContext.Set<WatchlistEntry>().AsNoTracking()
            join instrument in dbContext.Instruments.AsNoTracking()
                on entry.InstrumentId equals instrument.Id
            join exchange in dbContext.Exchanges.AsNoTracking()
                on instrument.ExchangeId equals exchange.Id
            where entry.WatchlistId == watchlist.Id
            orderby entry.Position
            select new
            {
                entry.InstrumentId,
                instrument.Ticker,
                instrument.Name,
                ExchangeCode = exchange.Code,
                instrument.Status,
                entry.Position,
                entry.AddedAtUtc,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new WatchlistView(
            watchlist.Name,
            watchlist.CreatedAtUtc,
            watchlist.UpdatedAtUtc,
            [.. rows.Select(row => new WatchlistItem(
                row.InstrumentId,
                row.Ticker.Value,
                row.Name,
                row.ExchangeCode.Value,
                row.Status,
                row.Position,
                row.AddedAtUtc))]);
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string name, CancellationToken cancellationToken = default) =>
        dbContext.Watchlists.AnyAsync(watchlist => watchlist.Name == name, cancellationToken);

    /// <inheritdoc />
    public Task<bool> InstrumentExistsAsync(InstrumentId instrumentId, CancellationToken cancellationToken = default) =>
        dbContext.Instruments.AnyAsync(instrument => instrument.Id == instrumentId, cancellationToken);

    /// <inheritdoc />
    public void Add(Watchlist watchlist) => dbContext.Watchlists.Add(watchlist);

    /// <inheritdoc />
    public void Remove(Watchlist watchlist) => dbContext.Watchlists.Remove(watchlist);
}
