using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalQuant.Application.Abstractions;
using PersonalQuant.Application.Classification;
using PersonalQuant.Application.Exchanges;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Infrastructure.Configuration;
using PersonalQuant.Infrastructure.Diagnostics;

namespace PersonalQuant.Infrastructure.Persistence.Seeding;

/// <summary>
/// Runs <see cref="ReferenceDataSeeder"/> at start-up when
/// <see cref="PostgresOptions.SeedReferenceDataOnStartup"/> is enabled.
/// </summary>
/// <remarks>
/// <para>
/// Registered after the migration service so that it runs against a schema
/// that is already current. It is off by default for the same reason
/// automatic migration is: a deployed environment should decide for itself
/// what is in its instrument master.
/// </para>
/// <para>
/// A failure is logged, not thrown. Seeding is a convenience; taking the API
/// process down because a starter dataset could not be written would trade a
/// missing search result for an outage.
/// </para>
/// </remarks>
/// <param name="scopeFactory">Factory used to resolve scoped repositories.</param>
/// <param name="options">Validated PostgreSQL settings.</param>
/// <param name="logger">Logger for seeding progress.</param>
public sealed class ReferenceDataSeedHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<PostgresOptions> options,
    ILogger<ReferenceDataSeedHostedService> logger) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.SeedReferenceDataOnStartup)
        {
            return;
        }

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();

            var seeder = new ReferenceDataSeeder(
                scope.ServiceProvider.GetRequiredService<IExchangeRepository>(),
                scope.ServiceProvider.GetRequiredService<IClassificationRepository>(),
                scope.ServiceProvider.GetRequiredService<IInstrumentRepository>(),
                scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
                scope.ServiceProvider.GetRequiredService<IClock>());

            var outcome = await seeder.SeedAsync(cancellationToken).ConfigureAwait(false);

            InfrastructureLog.ReferenceDataSeeded(
                logger,
                outcome.ExchangesCreated,
                outcome.SectorsCreated,
                outcome.IndustriesCreated,
                outcome.InstrumentsCreated);
        }
        catch (Exception exception) when (
            exception is DbException or TimeoutException or InvalidOperationException)
        {
            InfrastructureLog.ReferenceDataSeedFailed(logger, exception);
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
