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
    /// Stages a new exchange. Call
    /// <see cref="Abstractions.IUnitOfWork.SaveChangesAsync"/> to persist it.
    /// </summary>
    /// <param name="exchange">The exchange to add.</param>
    void Add(Exchange exchange);
}
