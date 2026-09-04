using Microsoft.EntityFrameworkCore;
using PersonalQuant.Application.Exchanges;
using PersonalQuant.Domain.Exchanges;

namespace PersonalQuant.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IExchangeRepository"/>.
/// </summary>
/// <param name="dbContext">The unit of work to read and stage through.</param>
internal sealed class ExchangeRepository(PersonalQuantDbContext dbContext) : IExchangeRepository
{
    /// <inheritdoc />
    public Task<Exchange?> FindByIdAsync(
        ExchangeId id,
        CancellationToken cancellationToken = default) =>
        dbContext.Exchanges.FirstOrDefaultAsync(
            exchange => exchange.Id == id,
            cancellationToken);

    /// <inheritdoc />
    public Task<Exchange?> FindByCodeAsync(
        ExchangeCode code,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(code);

        return dbContext.Exchanges.FirstOrDefaultAsync(
            exchange => exchange.Code == code,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Exchange>> ListAsync(
        CancellationToken cancellationToken = default) =>
        await dbContext.Exchanges
            .OrderBy(exchange => exchange.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<TradingHoliday>> ListHolidaysAsync(
        ExchangeId exchangeId,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default) =>
        await dbContext.TradingHolidays
            .AsNoTracking()
            .Where(holiday =>
                holiday.ExchangeId == exchangeId
                && holiday.Date >= fromDate
                && holiday.Date <= toDate)
            .OrderBy(holiday => holiday.Date)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public Task<bool> HasHolidayAsync(
        ExchangeId exchangeId,
        DateOnly onDate,
        CancellationToken cancellationToken = default) =>
        dbContext.TradingHolidays.AnyAsync(
            holiday => holiday.ExchangeId == exchangeId && holiday.Date == onDate,
            cancellationToken);

    /// <inheritdoc />
    public void AddHoliday(TradingHoliday holiday)
    {
        ArgumentNullException.ThrowIfNull(holiday);

        dbContext.TradingHolidays.Add(holiday);
    }

    /// <inheritdoc />
    public void Add(Exchange exchange)
    {
        ArgumentNullException.ThrowIfNull(exchange);

        dbContext.Exchanges.Add(exchange);
    }
}
