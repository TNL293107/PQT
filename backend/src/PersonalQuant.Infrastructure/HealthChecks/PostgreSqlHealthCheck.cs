using System.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Npgsql;
using PersonalQuant.Infrastructure.Diagnostics;

namespace PersonalQuant.Infrastructure.HealthChecks;

/// <summary>
/// Reports whether the API can reach PostgreSQL and execute a statement.
/// </summary>
/// <remarks>
/// A round trip is used rather than an inspection of pool state, because a
/// pooled connection object proves nothing about the server actually
/// answering.
/// </remarks>
/// <param name="dataSource">The shared Npgsql data source.</param>
/// <param name="logger">Logger for failure diagnostics.</param>
public sealed class PostgreSqlHealthCheck(
    NpgsqlDataSource dataSource,
    ILogger<PostgreSqlHealthCheck> logger) : IHealthCheck
{
    /// <summary>Name under which this check is registered.</summary>
    public const string Name = "postgres";

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await using var connection = await dataSource
                .OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            _ = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();

            return HealthCheckResult.Healthy(
                "PostgreSQL responded to a round-trip query.",
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["server"] = connection.PostgreSqlVersion.ToString(),
                    ["database"] = connection.Database,
                    ["latencyMs"] = stopwatch.Elapsed.TotalMilliseconds,
                });
        }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException)
        {
            // The exception is logged in full but deliberately not returned to
            // the caller: a health endpoint must not leak host names, ports or
            // authentication detail to an unauthenticated client.
            InfrastructureLog.PostgresHealthCheckFailed(logger, exception);

            return HealthCheckResult.Unhealthy("PostgreSQL is not reachable.");
        }
    }
}
