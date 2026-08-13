using StackExchange.Redis;

namespace PersonalQuant.Infrastructure.Caching;

/// <summary>
/// Supplies the shared Redis connection multiplexer.
/// </summary>
/// <remarks>
/// Connecting is asynchronous and must not happen while the dependency
/// injection container is being built, otherwise an unavailable Redis would
/// stall or fail application start-up. This abstraction defers the connect to
/// first use and keeps the result for the lifetime of the process.
/// </remarks>
public interface IRedisConnectionProvider
{
    /// <summary>Gets the multiplexer, connecting on first call.</summary>
    /// <param name="cancellationToken">Cancels the connect attempt.</param>
    /// <returns>The shared multiplexer.</returns>
    Task<IConnectionMultiplexer> GetConnectionAsync(CancellationToken cancellationToken = default);
}
