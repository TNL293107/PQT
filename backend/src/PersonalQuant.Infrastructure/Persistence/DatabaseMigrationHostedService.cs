using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalQuant.Infrastructure.Configuration;
using PersonalQuant.Infrastructure.Diagnostics;

namespace PersonalQuant.Infrastructure.Persistence;

/// <summary>
/// Applies pending EF Core migrations during host start-up when
/// <see cref="PostgresOptions.ApplyMigrationsOnStartup"/> is enabled.
/// </summary>
/// <remarks>
/// <para>
/// Enabled for local development and the Docker Compose environment, where the
/// database is disposable. Deployed environments are expected to run
/// migrations as an explicit, reviewable step instead.
/// </para>
/// <para>
/// The database is not assumed to be reachable at start-up. Compose orders the
/// first boot with a health condition, but that condition is only evaluated by
/// <c>docker compose up</c> — when the Docker daemon restarts and brings
/// containers back under their restart policy, the API can and does start while
/// PostgreSQL is still recovering, which surfaces as <c>57P03: the database
/// system is starting up</c>.
/// </para>
/// <para>
/// So migration is retried with backoff, and a persistent failure is logged
/// rather than thrown. Throwing would fail host start-up and put the container
/// into a restart loop, which contradicts the liveness/readiness split this
/// system is built on: the process being alive and its dependencies being
/// usable are separate questions, and readiness already reports the second one.
/// </para>
/// </remarks>
/// <param name="scopeFactory">Factory used to resolve the scoped context.</param>
/// <param name="options">Validated PostgreSQL settings.</param>
/// <param name="logger">Logger for migration progress.</param>
public sealed class DatabaseMigrationHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<PostgresOptions> options,
    ILogger<DatabaseMigrationHostedService> logger) : IHostedService
{
    /// <summary>How many times migration is attempted before giving up.</summary>
    private const int MaxAttempts = 10;

    /// <summary>Upper bound on the delay between attempts.</summary>
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(15);

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.ApplyMigrationsOnStartup)
        {
            InfrastructureLog.AutomaticMigrationDisabled(logger);
            return;
        }

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                await ApplyMigrationsAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception exception) when (IsTransient(exception) && attempt < MaxAttempts)
            {
                var delay = TimeSpan.FromSeconds(Math.Min(attempt * 2, MaxDelay.TotalSeconds));

                InfrastructureLog.MigrationAttemptFailed(
                    logger, exception, attempt, MaxAttempts, delay.TotalSeconds);

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsTransient(exception))
            {
                // Attempts exhausted. The API still starts; readiness reports
                // PostgreSQL as unhealthy until the schema catches up.
                InfrastructureLog.MigrationAbandoned(logger, exception, MaxAttempts);
                return;
            }
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task ApplyMigrationsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PersonalQuantDbContext>();

        var pending = (await dbContext.Database
            .GetPendingMigrationsAsync(cancellationToken)
            .ConfigureAwait(false)).ToArray();

        if (pending.Length == 0)
        {
            InfrastructureLog.SchemaUpToDate(logger);
            return;
        }

        // Formatted once, at start-up, for a list that is a handful of entries
        // long — the cost is irrelevant and the record of what ran is not.
        var migrationNames = string.Join(", ", pending);
        InfrastructureLog.ApplyingMigrations(logger, pending.Length, migrationNames);

        await dbContext.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

        InfrastructureLog.MigrationCompleted(logger);
    }

    /// <summary>
    /// Decides whether a failure is worth retrying.
    /// </summary>
    /// <remarks>
    /// Deliberately broad across connection-level faults: a database that is
    /// starting, recovering, refusing connections or unreachable are all
    /// conditions that resolve on their own. A malformed migration is not, but
    /// it also does not surface as a <see cref="DbException"/> from the
    /// connection layer, so it falls through and propagates.
    /// </remarks>
    private static bool IsTransient(Exception exception) =>
        exception is DbException or TimeoutException or InvalidOperationException;
}
