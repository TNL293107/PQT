using PersonalQuant.Application.Abstractions;
using PersonalQuant.Application.Classification;
using PersonalQuant.Application.Exchanges;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Domain.Classification;
using PersonalQuant.Domain.Currencies;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.Infrastructure.Persistence.Seeding;

/// <summary>
/// Creates the Vietnamese venues and a starter set of securities, so that a
/// freshly created database has something for search to find.
/// </summary>
/// <remarks>
/// <para>
/// Idempotent by inspection rather than by identifier: an exchange is created
/// only when its code is unknown, and an instrument only when its ticker is
/// free on its venue. Seeded rows therefore carry ordinary generated
/// identifiers, and running the seeder against a populated database changes
/// nothing.
/// </para>
/// <para>
/// It never updates or deletes an existing row. A record that has been
/// corrected by hand, renamed, or delisted stays as it is — the seeder's job
/// is to fill an empty database, not to assert authority over a populated one.
/// </para>
/// </remarks>
/// <param name="exchanges">The venue repository.</param>
/// <param name="classification">The sector and industry taxonomy.</param>
/// <param name="instruments">The instrument master.</param>
/// <param name="unitOfWork">Commits the staged records.</param>
/// <param name="clock">Supplies the audit timestamp.</param>
internal sealed class ReferenceDataSeeder(
    IExchangeRepository exchanges,
    IClassificationRepository classification,
    IInstrumentRepository instruments,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    /// <summary>
    /// Applies the seed, creating only what is missing.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>How many exchanges and instruments were created.</returns>
    public async Task<ReferenceDataSeedOutcome> SeedAsync(CancellationToken cancellationToken = default)
    {
        var occurredAtUtc = clock.UtcNow;

        // Strictly ordered by foreign key: sectors before industries, venues
        // and industries before instruments. Each level commits before the
        // next is staged, because a child row cannot point at a parent that
        // has not been written.
        var exchangesByCode = await SeedExchangesAsync(occurredAtUtc, cancellationToken)
            .ConfigureAwait(false);

        var sectorsByCode = await SeedSectorsAsync(occurredAtUtc, cancellationToken)
            .ConfigureAwait(false);

        var industriesByCode = await SeedIndustriesAsync(
            sectorsByCode, occurredAtUtc, cancellationToken).ConfigureAwait(false);

        var instrumentsCreated = await SeedInstrumentsAsync(
            exchangesByCode, industriesByCode, occurredAtUtc, cancellationToken)
            .ConfigureAwait(false);

        return new ReferenceDataSeedOutcome(
            exchangesByCode.CreatedCount,
            sectorsByCode.CreatedCount,
            industriesByCode.CreatedCount,
            instrumentsCreated);
    }

    private async Task<SeededNodes<SectorId>> SeedSectorsAsync(
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken)
    {
        var byCode = new Dictionary<string, SectorId>(StringComparer.Ordinal);
        var created = 0;

        foreach (var seed in VietnamReferenceData.Sectors)
        {
            var code = ClassificationCode.Create(seed.Code);
            var existing = await classification
                .FindSectorByCodeAsync(code, cancellationToken)
                .ConfigureAwait(false);

            if (existing is not null)
            {
                byCode[seed.Code] = existing.Id;
                continue;
            }

            var sector = Sector.Register(code, seed.Name, occurredAtUtc);

            classification.AddSector(sector);
            byCode[seed.Code] = sector.Id;
            created++;
        }

        if (created > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new SeededNodes<SectorId>(byCode, created);
    }

    private async Task<SeededNodes<IndustryId>> SeedIndustriesAsync(
        SeededNodes<SectorId> sectorsByCode,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken)
    {
        var byCode = new Dictionary<string, IndustryId>(StringComparer.Ordinal);
        var created = 0;

        foreach (var seed in VietnamReferenceData.Industries)
        {
            var code = ClassificationCode.Create(seed.Code);
            var existing = await classification
                .FindIndustryByCodeAsync(code, cancellationToken)
                .ConfigureAwait(false);

            if (existing is not null)
            {
                byCode[seed.Code] = existing.Id;
                continue;
            }

            // Unreachable while the seed file is internally consistent, and
            // skipped rather than thrown on so that one bad row cannot stop a
            // database from being usable.
            if (!sectorsByCode.ByCode.TryGetValue(seed.SectorCode, out var sectorId))
            {
                continue;
            }

            var industry = Industry.Register(sectorId, code, seed.Name, occurredAtUtc);

            classification.AddIndustry(industry);
            byCode[seed.Code] = industry.Id;
            created++;
        }

        if (created > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new SeededNodes<IndustryId>(byCode, created);
    }

    private async Task<SeededNodes<ExchangeId>> SeedExchangesAsync(
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken)
    {
        var byCode = new Dictionary<string, ExchangeId>(StringComparer.Ordinal);
        var created = 0;

        foreach (var seed in VietnamReferenceData.Exchanges)
        {
            var code = ExchangeCode.Create(seed.Code);
            var existing = await exchanges
                .FindByCodeAsync(code, cancellationToken)
                .ConfigureAwait(false);

            if (existing is not null)
            {
                byCode[seed.Code] = existing.Id;
                continue;
            }

            var exchange = Exchange.Register(
                code,
                seed.Name,
                VietnamReferenceData.TimeZoneId,
                occurredAtUtc,
                seed.Mic);

            exchanges.Add(exchange);
            byCode[seed.Code] = exchange.Id;
            created++;
        }

        // Committed before instruments are staged: an instrument's foreign key
        // has to point at a venue row that exists.
        if (created > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new SeededNodes<ExchangeId>(byCode, created);
    }

    private async Task<int> SeedInstrumentsAsync(
        SeededNodes<ExchangeId> exchangesByCode,
        SeededNodes<IndustryId> industriesByCode,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken)
    {
        var created = 0;

        foreach (var seed in VietnamReferenceData.Instruments)
        {
            if (!exchangesByCode.ByCode.TryGetValue(seed.ExchangeCode, out var exchangeId))
            {
                continue;
            }

            var ticker = Ticker.Create(seed.Ticker);

            var taken = await instruments
                .IsTickerTakenAsync(exchangeId, ticker, cancellationToken)
                .ConfigureAwait(false);

            if (taken)
            {
                continue;
            }

            var instrument = Instrument.Register(
                exchangeId,
                ticker,
                seed.Name,
                seed.AssetType,
                CurrencyCode.Vnd,
                occurredAtUtc);

            // Listed without a first trading date, for the reason recorded on
            // VietnamReferenceData: the dates are real but unsourced here.
            instrument.List(occurredAtUtc);

            // Only where the seed names an industry. A security the seed
            // leaves unclassified stays unclassified rather than being
            // dropped into whichever node happens to be first.
            if (seed.IndustryCode is { } industryCode
                && industriesByCode.ByCode.TryGetValue(industryCode, out var industryId))
            {
                instrument.AssignIndustry(industryId, occurredAtUtc);
            }

            instruments.Add(instrument);
            created++;
        }

        if (created > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return created;
    }

    /// <summary>
    /// The identifiers a level of the seed resolved to, keyed by the code the
    /// seed file uses, plus how many of them this run created.
    /// </summary>
    /// <remarks>
    /// Generic because all four levels need the same thing: the map lets the
    /// next level resolve a parent without going back to the database, and the
    /// count is what the run reports. It carries every code the seed names,
    /// created here or already present, so a partially populated database
    /// still resolves.
    /// </remarks>
    /// <typeparam name="TId">The identifier type of the level.</typeparam>
    /// <param name="ByCode">Identifiers keyed by seed code.</param>
    /// <param name="CreatedCount">How many rows this run created.</param>
    private sealed record SeededNodes<TId>(
        IReadOnlyDictionary<string, TId> ByCode,
        int CreatedCount);
}

/// <summary>What a seeding run created.</summary>
/// <param name="ExchangesCreated">Venues added.</param>
/// <param name="SectorsCreated">Taxonomy sectors added.</param>
/// <param name="IndustriesCreated">Taxonomy industries added.</param>
/// <param name="InstrumentsCreated">Securities added.</param>
internal sealed record ReferenceDataSeedOutcome(
    int ExchangesCreated,
    int SectorsCreated,
    int IndustriesCreated,
    int InstrumentsCreated);
