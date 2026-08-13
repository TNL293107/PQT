using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using PersonalQuant.Infrastructure.Caching;
using PersonalQuant.Infrastructure.Diagnostics;
using StackExchange.Redis;

namespace PersonalQuant.Infrastructure.HealthChecks;

/// <summary>
/// Reports whether the API can reach Redis.
/// </summary>
/// <remarks>
/// The multiplexer is configured with <c>AbortOnConnectFail = false</c> so
/// that a Redis outage never blocks start-up. That makes an explicit ping the
/// only reliable signal of availability.
/// </remarks>
/// <param name="connectionProvider">Provides the shared multiplexer.</param>
/// <param name="logger">Logger for failure diagnostics.</param>
public sealed class RedisHealthCheck(
    IRedisConnectionProvider connectionProvider,
    ILogger<RedisHealthCheck> logger) : IHealthCheck
{
    /// <summary>Name under which this check is registered.</summary>
    public const string Name = "redis";

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var connection = await connectionProvider
                .GetConnectionAsync(cancellationToken)
                .ConfigureAwait(false);

            if (!connection.IsConnected)
            {
                return HealthCheckResult.Unhealthy("Redis is not reachable.");
            }

            var latency = await connection.GetDatabase().PingAsync().ConfigureAwait(false);

            return HealthCheckResult.Healthy(
                "Redis responded to PING.",
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["latencyMs"] = latency.TotalMilliseconds,
                });
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            // As with PostgreSQL: log the detail, return none of it.
            InfrastructureLog.RedisHealthCheckFailed(logger, exception);

            return HealthCheckResult.Unhealthy("Redis is not reachable.");
        }
    }
}
