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
/// Enabled for local development and the Docker Compose environment, where the
/// database is disposable. Deployed environments are expected to run
/// migrations as an explicit, reviewable step instead.
/// </remarks>
/// <param name="scopeFactory">Factory used to resolve the scoped context.</param>
/// <param name="options">Validated PostgreSQL settings.</param>
/// <param name="logger">Logger for migration progress.</param>
public sealed class DatabaseMigrationHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<PostgresOptions> options,
    ILogger<DatabaseMigrationHostedService> logger) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.ApplyMigrationsOnStartup)
        {
            InfrastructureLog.AutomaticMigrationDisabled(logger);
            return;
        }

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

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
