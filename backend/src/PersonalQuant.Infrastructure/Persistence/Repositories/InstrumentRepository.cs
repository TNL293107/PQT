using Microsoft.EntityFrameworkCore;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IInstrumentRepository"/>.
/// </summary>
/// <remarks>
/// The read projections join instruments to exchanges by hand.
/// <see cref="Instrument"/> holds an <see cref="ExchangeId"/> rather than a
/// navigation property — identity is a key, and the aggregate does not own the
/// venue — and the join is written out in each query rather than factored into
/// a helper because EF Core only sees through an anonymous type here. A named
/// record in the same position stops the query translating and pushes the
/// whole instrument master into memory.
/// </remarks>
/// <param name="dbContext">The unit of work to read and stage through.</param>
internal sealed class InstrumentRepository(PersonalQuantDbContext dbContext) : IInstrumentRepository
{
    /// <inheritdoc />
    public Task<Instrument?> FindByIdAsync(
        InstrumentId id,
        CancellationToken cancellationToken = default) =>
        dbContext.Instruments.FirstOrDefaultAsync(
            instrument => instrument.Id == id,
            cancellationToken);

    /// <inheritdoc />
    public Task<Instrument?> FindActiveByTickerAsync(
        ExchangeId exchangeId,
        Ticker ticker,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ticker);

        // Status is compared rather than the derived IsActive property, which
        // has no column and cannot be translated to SQL.
        return dbContext.Instruments.FirstOrDefaultAsync(
            instrument =>
                instrument.ExchangeId == exchangeId
                && instrument.Ticker == ticker
                && instrument.Status != InstrumentStatus.Delisted,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Instrument>> ListTickerHistoryAsync(
        ExchangeId exchangeId,
        Ticker ticker,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ticker);

        return await dbContext.Instruments
            .Where(instrument =>
                instrument.ExchangeId == exchangeId && instrument.Ticker == ticker)
            .OrderByDescending(instrument => instrument.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<bool> IsTickerTakenAsync(
        ExchangeId exchangeId,
        Ticker ticker,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ticker);

        return dbContext.Instruments.AnyAsync(
            instrument =>
                instrument.ExchangeId == exchangeId
                && instrument.Ticker == ticker
                && instrument.Status != InstrumentStatus.Delisted,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InstrumentSearchResult>> SearchAsync(
        InstrumentSearchCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        var text = criteria.Text;
        var prefix = LikePattern.StartsWith(text);
        var contains = LikePattern.Contains(text);

        var matches =
            from instrument in dbContext.Instruments.AsNoTracking()
            join exchange in dbContext.Exchanges.AsNoTracking()
                on instrument.ExchangeId equals exchange.Id
            where instrument.SearchTicker == text
                || EF.Functions.Like(instrument.SearchTicker, prefix, LikePattern.EscapeCharacter)
                || instrument.SearchName == text
                || EF.Functions.Like(instrument.SearchName, prefix, LikePattern.EscapeCharacter)
                || EF.Functions.Like(instrument.SearchName, contains, LikePattern.EscapeCharacter)
            select new { Instrument = instrument, Exchange = exchange };

        if (!criteria.IncludeInactive)
        {
            matches = matches.Where(row => row.Instrument.Status != InstrumentStatus.Delisted);
        }

        // The rank becomes a SQL CASE expression, which is why it is computed
        // as an integer; the enum gives those integers their meaning, and the
        // values are mapped back once the rows are materialised.
        //
        // Ordering is applied before the limit and is total: rank, then
        // ticker, then identifier. Without the final tie-break two rows with
        // the same rank and ticker could come back in either order, and a
        // search box whose results reshuffle between identical queries is
        // worse than one that is merely wrong — the row under the cursor moves
        // between the keystroke and the Enter.
        var ranked = await matches
            .Select(row => new
            {
                row.Instrument,
                row.Exchange,
                Rank =
                    row.Instrument.SearchTicker == text
                        ? (int)InstrumentMatchKind.ExactTicker
                    : EF.Functions.Like(row.Instrument.SearchTicker, prefix, LikePattern.EscapeCharacter)
                        ? (int)InstrumentMatchKind.TickerPrefix
                    : row.Instrument.SearchName == text
                        ? (int)InstrumentMatchKind.ExactName
                    : EF.Functions.Like(row.Instrument.SearchName, prefix, LikePattern.EscapeCharacter)
                        ? (int)InstrumentMatchKind.NamePrefix
                    : (int)InstrumentMatchKind.NameContains,
            })
            .OrderBy(row => row.Rank)
            .ThenBy(row => row.Instrument.SearchTicker)
            .ThenBy(row => row.Instrument.Id)
            .Take(criteria.Limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. ranked.Select(row =>
            ToResult(row.Instrument, row.Exchange, (InstrumentMatchKind)row.Rank))];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InstrumentSearchResult>> ListActiveByTickerAsync(
        Ticker ticker,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ticker);

        var rows = await (
            from instrument in dbContext.Instruments.AsNoTracking()
            join exchange in dbContext.Exchanges.AsNoTracking()
                on instrument.ExchangeId equals exchange.Id
            where instrument.Ticker == ticker
                && instrument.Status != InstrumentStatus.Delisted
            orderby exchange.Code, instrument.Id
            select new { Instrument = instrument, Exchange = exchange })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. rows.Select(row => ToResult(row.Instrument, row.Exchange, matchKind: null))];
    }

    /// <inheritdoc />
    public async Task<InstrumentSearchResult?> FindResultByIdAsync(
        InstrumentId id,
        CancellationToken cancellationToken = default)
    {
        var row = await (
            from instrument in dbContext.Instruments.AsNoTracking()
            join exchange in dbContext.Exchanges.AsNoTracking()
                on instrument.ExchangeId equals exchange.Id
            where instrument.Id == id
            select new { Instrument = instrument, Exchange = exchange })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return row is null ? null : ToResult(row.Instrument, row.Exchange, matchKind: null);
    }

    /// <inheritdoc />
    public void Add(Instrument instrument)
    {
        ArgumentNullException.ThrowIfNull(instrument);

        dbContext.Instruments.Add(instrument);
    }

    private static InstrumentSearchResult ToResult(
        Instrument instrument,
        Exchange exchange,
        InstrumentMatchKind? matchKind) =>
        new(
            instrument.Id,
            instrument.Ticker,
            instrument.Name,
            instrument.AssetType,
            exchange.Code,
            instrument.Currency,
            instrument.Status,
            matchKind);
}
