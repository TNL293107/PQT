using Microsoft.EntityFrameworkCore;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IInstrumentRepository"/>.
/// </summary>
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
    public void Add(Instrument instrument)
    {
        ArgumentNullException.ThrowIfNull(instrument);

        dbContext.Instruments.Add(instrument);
    }
}
