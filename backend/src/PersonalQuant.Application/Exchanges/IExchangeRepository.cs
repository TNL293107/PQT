using PersonalQuant.Domain.Exchanges;

namespace PersonalQuant.Application.Exchanges;

/// <summary>
/// Reads and records trading venues.
/// </summary>
/// <remarks>
/// There is no delete. An exchange that closes is historical fact that
/// instruments and prices continue to reference.
/// </remarks>
public interface IExchangeRepository
{
    /// <summary>Finds an exchange by its canonical identifier.</summary>
    /// <param name="id">The identifier to look up.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The exchange, or <see langword="null"/> when unknown.</returns>
    Task<Exchange?> FindByIdAsync(ExchangeId id, CancellationToken cancellationToken = default);

    /// <summary>Finds an exchange by its operating code.</summary>
    /// <param name="code">The code to look up.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The exchange, or <see langword="null"/> when unknown.</returns>
    Task<Exchange?> FindByCodeAsync(ExchangeCode code, CancellationToken cancellationToken = default);

    /// <summary>Lists every known exchange, ordered by code.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>All exchanges.</returns>
    Task<IReadOnlyList<Exchange>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists a venue's scheduled closures over a date range, inclusive at both
    /// ends.
    /// </summary>
    /// <remarks>
    /// A window rather than the whole calendar. Quality checks run over a range
    /// and answer many questions about it, so the holidays for that range are
    /// loaded once and consulted in memory; loading every holiday a venue has
    /// ever had would grow without bound for no benefit.
    /// </remarks>
    /// <param name="exchangeId">The venue.</param>
    /// <param name="fromDate">The first date to include.</param>
    /// <param name="toDate">The last date to include.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The closures in the range, ordered by date.</returns>
    Task<IReadOnlyList<TradingHoliday>> ListHolidaysAsync(
        ExchangeId exchangeId,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports whether a venue already has a closure recorded for a date.
    /// </summary>
    /// <param name="exchangeId">The venue.</param>
    /// <param name="onDate">The date to check.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><see langword="true"/> when a closure is already recorded.</returns>
    Task<bool> HasHolidayAsync(
        ExchangeId exchangeId,
        DateOnly onDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stages a new exchange. Call
    /// <see cref="Abstractions.IUnitOfWork.SaveChangesAsync"/> to persist it.
    /// </summary>
    /// <param name="exchange">The exchange to add.</param>
    void Add(Exchange exchange);

    /// <summary>
    /// Stages a new scheduled closure. Call
    /// <see cref="Abstractions.IUnitOfWork.SaveChangesAsync"/> to persist it.
    /// </summary>
    /// <param name="holiday">The closure to add.</param>
    void AddHoliday(TradingHoliday holiday);
}
