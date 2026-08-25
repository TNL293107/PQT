using Microsoft.EntityFrameworkCore;
using PersonalQuant.Application.Classification;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Domain.Classification;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

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
                // Exact only. An alias is an identifier, not searchable text:
                // a prefix of an ISIN identifies nothing, and matching a
                // fragment of one would return securities that merely share a
                // country prefix.
                || dbContext.InstrumentIdentifiers.Any(identifier =>
                    identifier.InstrumentId == instrument.Id && identifier.Value == text)
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
                    : EF.Functions.Like(row.Instrument.SearchName, contains, LikePattern.EscapeCharacter)
                        ? (int)InstrumentMatchKind.NameContains
                    // Nothing else matched, so the row is here because an
                    // alias matched exactly — the only remaining branch of
                    // the WHERE clause above.
                    : (int)InstrumentMatchKind.IdentifierExact,
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
    public async Task<InstrumentDetail?> FindDetailByIdAsync(
        InstrumentId id,
        CancellationToken cancellationToken = default)
    {
        // Left joins, not inner ones: an unclassified instrument is normal,
        // and an inner join would answer "no such instrument" for every index
        // and every security a mapping has not reached yet.
        var row = await (
            from instrument in dbContext.Instruments.AsNoTracking()
            join exchange in dbContext.Exchanges.AsNoTracking()
                on instrument.ExchangeId equals exchange.Id
            join industry in dbContext.Industries.AsNoTracking()
                on instrument.IndustryId equals (IndustryId?)industry.Id into industryMatches
            from industry in industryMatches.DefaultIfEmpty()
            join sector in dbContext.Sectors.AsNoTracking()
                on industry.SectorId equals sector.Id into sectorMatches
            from sector in sectorMatches.DefaultIfEmpty()
            where instrument.Id == id
            select new
            {
                Instrument = instrument,
                Exchange = exchange,
                Industry = (Industry?)industry,
                Sector = (Sector?)sector,
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            return null;
        }

        // Both levels or neither. An industry row cannot exist without its
        // sector — the foreign key sees to that — so a half-populated
        // classification would mean the query was wrong, not the data.
        var classification = row.Industry is null || row.Sector is null
            ? null
            : new InstrumentClassification(
                row.Sector.Code,
                row.Sector.Name,
                row.Industry.Code,
                row.Industry.Name);

        // A second round trip rather than a join. Aliases are a one-to-many,
        // and joining them into the row above would multiply the instrument
        // across its identifiers and leave the caller to collapse it again.
        var aliases = await ListIdentifiersAsync(id, cancellationToken).ConfigureAwait(false);

        return new InstrumentDetail(
            row.Instrument.Id,
            row.Instrument.Ticker,
            row.Instrument.Name,
            row.Instrument.AssetType,
            row.Exchange.Code,
            row.Exchange.Name,
            row.Instrument.Currency,
            row.Instrument.Status,
            row.Instrument.ListedOn,
            row.Instrument.DelistedOn,
            classification,
            [.. aliases.Select(alias =>
                new InstrumentAlias(alias.Scheme, alias.Value, alias.Source?.Value))]);
    }

    /// <inheritdoc />
    public async Task<InstrumentPage> ListAsync(
        InstrumentListCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        // Left joins on the taxonomy, because filtering by sector must not
        // silently exclude unclassified rows from the unfiltered case.
        var matches =
            from instrument in dbContext.Instruments.AsNoTracking()
            join exchange in dbContext.Exchanges.AsNoTracking()
                on instrument.ExchangeId equals exchange.Id
            join industry in dbContext.Industries.AsNoTracking()
                on instrument.IndustryId equals (IndustryId?)industry.Id into industryMatches
            from industry in industryMatches.DefaultIfEmpty()
            join sector in dbContext.Sectors.AsNoTracking()
                on industry.SectorId equals sector.Id into sectorMatches
            from sector in sectorMatches.DefaultIfEmpty()
            select new { Instrument = instrument, Exchange = exchange, Sector = sector };

        if (criteria.Exchange is { } exchangeCode)
        {
            matches = matches.Where(row => row.Exchange.Code == exchangeCode);
        }

        if (criteria.AssetType is { } assetType)
        {
            matches = matches.Where(row => row.Instrument.AssetType == assetType);
        }

        if (criteria.Status is { } status)
        {
            matches = matches.Where(row => row.Instrument.Status == status);
        }

        if (criteria.Sector is { } sectorCode)
        {
            matches = matches.Where(row => row.Sector.Code == sectorCode);
        }

        // Counted before paging, and counted in the database. A caller cannot
        // page without knowing the total, and materialising the universe to
        // count it in memory would defeat the point of paging at all.
        var total = await matches.CountAsync(cancellationToken).ConfigureAwait(false);

        var rows = await matches
            .OrderBy(row => row.Exchange.Code)
            .ThenBy(row => row.Instrument.SearchTicker)
            .ThenBy(row => row.Instrument.Id)
            .Skip(criteria.Offset)
            .Take(criteria.Limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new InstrumentPage(
            [.. rows.Select(row => ToResult(row.Instrument, row.Exchange, matchKind: null))],
            total,
            criteria.Limit,
            criteria.Offset);
    }

    /// <inheritdoc />
    public Task<InstrumentIdentifier?> FindIdentifierAsync(
        IdentifierValue value,
        SourceCode? source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);

        var scheme = value.Scheme;
        var text = value.Value;

        return dbContext.InstrumentIdentifiers.FirstOrDefaultAsync(
            identifier =>
                identifier.Scheme == scheme
                && identifier.Value == text
                && identifier.Source == source,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InstrumentIdentifier>> ListIdentifiersAsync(
        InstrumentId id,
        CancellationToken cancellationToken = default) =>
        await dbContext.InstrumentIdentifiers
            .AsNoTracking()
            .Where(identifier => identifier.InstrumentId == id)
            .OrderBy(identifier => identifier.Scheme)
            .ThenBy(identifier => identifier.Value)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<RelatedInstrument>> ListRelatedAsync(
        InstrumentId id,
        CancellationToken cancellationToken = default)
    {
        var subject = await dbContext.Instruments
            .AsNoTracking()
            .FirstOrDefaultAsync(instrument => instrument.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (subject is null)
        {
            return [];
        }

        var rows = await (
            from instrument in dbContext.Instruments.AsNoTracking()
            join exchange in dbContext.Exchanges.AsNoTracking()
                on instrument.ExchangeId equals exchange.Id
            where instrument.ExchangeId == subject.ExchangeId
                && instrument.Ticker == subject.Ticker
                && instrument.Id != subject.Id
            // Total, so two rows created in the same transaction cannot come
            // back in either order.
            orderby instrument.CreatedAtUtc descending, instrument.Id
            select new { Instrument = instrument, Exchange = exchange })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. rows.Select(row => new RelatedInstrument(
            ToResult(row.Instrument, row.Exchange, matchKind: null),
            InstrumentRelationKind.TickerHistory,
            subject.Ticker.Value))];
    }

    /// <inheritdoc />
    public void Add(Instrument instrument)
    {
        ArgumentNullException.ThrowIfNull(instrument);

        dbContext.Instruments.Add(instrument);
    }

    /// <inheritdoc />
    public void AddIdentifier(InstrumentIdentifier identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        dbContext.InstrumentIdentifiers.Add(identifier);
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
