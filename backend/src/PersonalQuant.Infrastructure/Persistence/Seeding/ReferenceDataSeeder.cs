using PersonalQuant.Application.Abstractions;
using PersonalQuant.Application.Exchanges;
using PersonalQuant.Application.Instruments;
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
/// <param name="instruments">The instrument master.</param>
/// <param name="unitOfWork">Commits the staged records.</param>
/// <param name="clock">Supplies the audit timestamp.</param>
internal sealed class ReferenceDataSeeder(
    IExchangeRepository exchanges,
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

        var exchangesByCode = await SeedExchangesAsync(occurredAtUtc, cancellationToken)
            .ConfigureAwait(false);

        var instrumentsCreated = await SeedInstrumentsAsync(
            exchangesByCode, occurredAtUtc, cancellationToken).ConfigureAwait(false);

        return new ReferenceDataSeedOutcome(exchangesByCode.CreatedCount, instrumentsCreated);
    }

    private async Task<SeededExchanges> SeedExchangesAsync(
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

        return new SeededExchanges(byCode, created);
    }

    private async Task<int> SeedInstrumentsAsync(
        SeededExchanges exchangesByCode,
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

            instruments.Add(instrument);
            created++;
        }

        if (created > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return created;
    }

    private sealed record SeededExchanges(
        IReadOnlyDictionary<string, ExchangeId> ByCode,
        int CreatedCount);
}

/// <summary>What a seeding run created.</summary>
/// <param name="ExchangesCreated">Venues added.</param>
/// <param name="InstrumentsCreated">Securities added.</param>
internal sealed record ReferenceDataSeedOutcome(int ExchangesCreated, int InstrumentsCreated);
