using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Npgsql;
using PersonalQuant.Application.Abstractions;
using PersonalQuant.Application.Classification;
using PersonalQuant.Application.CorporateActions;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Application.Exchanges;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Application.Universes;
using PersonalQuant.Infrastructure.Caching;
using PersonalQuant.Infrastructure.CorporateActions;
using PersonalQuant.Infrastructure.Exchanges;
using PersonalQuant.Infrastructure.Instruments;
using PersonalQuant.Infrastructure.MarketData;
using PersonalQuant.Infrastructure.Universes;
using PersonalQuant.Infrastructure.Configuration;
using PersonalQuant.Infrastructure.HealthChecks;
using PersonalQuant.Infrastructure.Persistence;
using PersonalQuant.Infrastructure.Persistence.Repositories;
using PersonalQuant.Infrastructure.Persistence.Seeding;
using PersonalQuant.Infrastructure.Time;

namespace PersonalQuant.Infrastructure;

/// <summary>
/// Composition root for the infrastructure layer.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>Health check tag for dependencies required to serve traffic.</summary>
    public const string ReadinessTag = "ready";

    /// <summary>
    /// Registers PostgreSQL, Redis and the health checks that observe them.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The same service collection, to allow chaining.</returns>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptionsWithValidation<PostgresOptions>(configuration, PostgresOptions.SectionName);
        services.AddOptionsWithValidation<RedisOptions>(configuration, RedisOptions.SectionName);
        services.AddOptionsWithValidation<MarketDataOptions>(configuration, MarketDataOptions.SectionName);

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IDelayScheduler, SystemDelayScheduler>();

        AddPostgres(services);
        AddRedis(services);
        AddMarketDataSources(services, configuration);
        AddHealthChecks(services);

        return services;
    }

    private static void AddPostgres(IServiceCollection services)
    {
        // One data source per process: it owns the connection pool and is
        // shared by EF Core and the health check.
        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<PostgresOptions>>().Value;
            return NpgsqlDataSource.Create(options.BuildConnectionString());
        });

        services.AddDbContext<PersonalQuantDbContext>((provider, builder) =>
        {
            var dataSource = provider.GetRequiredService<NpgsqlDataSource>();

            builder.UseNpgsql(dataSource, npgsql =>
            {
                npgsql.MigrationsHistoryTable(
                    MigrationDefaults.HistoryTableName,
                    PersonalQuantDbContext.Schema);

                // Transient network faults are normal against a containerised
                // database; retry rather than surface them as request errors.
                npgsql.EnableRetryOnFailure(maxRetryCount: 3, TimeSpan.FromSeconds(5), null);
            });
        });

        services.AddScoped<IUnitOfWork>(provider =>
            provider.GetRequiredService<PersonalQuantDbContext>());
        services.AddScoped<IExchangeRepository, ExchangeRepository>();
        services.AddScoped<IInstrumentRepository, InstrumentRepository>();
        services.AddScoped<IClassificationRepository, ClassificationRepository>();
        services.AddScoped<IBarRepository, BarRepository>();
        services.AddScoped<IIngestionJournal, IngestionJournalRepository>();
        services.AddScoped<IDataQualityRepository, DataQualityRepository>();
        services.AddScoped<ICorporateActionRepository, CorporateActionRepository>();
        services.AddScoped<IUniverseRepository, UniverseRepository>();

        services.AddHostedService<DatabaseMigrationHostedService>();

        // Registered after migration: hosted services start in registration
        // order, and seeding writes to tables the migration has to create
        // first.
        services.AddHostedService<ReferenceDataSeedHostedService>();
    }

    private static void AddRedis(IServiceCollection services) =>
        services.AddSingleton<IRedisConnectionProvider, RedisConnectionProvider>();

    private static void AddHealthChecks(IServiceCollection services) =>
        services.AddHealthChecks()
            .AddCheck<PostgreSqlHealthCheck>(
                PostgreSqlHealthCheck.Name,
                HealthStatus.Unhealthy,
                [ReadinessTag, "postgres", "database"])
            .AddCheck<RedisHealthCheck>(
                RedisHealthCheck.Name,
                HealthStatus.Unhealthy,
                [ReadinessTag, "redis", "cache"]);

    /// <summary>
    /// Binds an options type and fails fast at start-up when it is invalid,
    /// so a misconfigured deployment cannot serve requests.
    /// </summary>
    private static void AddOptionsWithValidation<TOptions>(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName)
        where TOptions : class
    {
        services.AddOptions<TOptions>()
            .Bind(configuration.GetSection(sectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
    }

    /// <summary>
    /// Registers the ingestion policy and whichever market data sources the
    /// configuration names.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The policy is bound and validated here, then handed to the application
    /// layer as a plain object. That is what lets the pipeline hold no
    /// dependency on configuration binding at all, and it means an unusable
    /// setting fails the deployment rather than the first scheduled run.
    /// </para>
    /// <para>
    /// No source is registered by default. A deployment that has not been
    /// pointed at one ingests nothing and records skipped runs saying so,
    /// which is a better answer than silently reading whatever happens to sit
    /// in a conventional directory.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configuration">Application configuration.</param>
    private static void AddMarketDataSources(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration
            .GetSection(MarketDataOptions.SectionName)
            .Get<MarketDataOptions>() ?? new MarketDataOptions();

        services.AddSingleton(options.BuildPolicy());

        if (!string.IsNullOrWhiteSpace(options.FileProviderDirectory))
        {
            var directory = options.FileProviderDirectory;

            services.AddSingleton<IMarketDataProvider>(_ => new FileMarketDataProvider(directory));
        }

        if (!string.IsNullOrWhiteSpace(options.InstrumentListPath))
        {
            var path = options.InstrumentListPath;

            services.AddSingleton<IInstrumentProvider>(_ => new FileInstrumentProvider(path));
        }

        if (!string.IsNullOrWhiteSpace(options.TradingCalendarPath))
        {
            var path = options.TradingCalendarPath;

            services.AddSingleton<ITradingCalendarProvider>(
                _ => new FileTradingCalendarProvider(path));
        }

        if (!string.IsNullOrWhiteSpace(options.CorporateActionPath))
        {
            var path = options.CorporateActionPath;

            services.AddSingleton<ICorporateActionProvider>(
                _ => new FileCorporateActionProvider(path));
        }

        if (!string.IsNullOrWhiteSpace(options.UniverseDirectory))
        {
            var path = options.UniverseDirectory;

            services.AddSingleton<IUniverseMembershipProvider>(
                _ => new FileUniverseMembershipProvider(path));
        }


        // The hosts the pipelines were written for. Both check their own flag
        // and return immediately when it is off, so registering them
        // unconditionally costs a deployment that wants neither nothing at
        // all — and keeps the decision in configuration rather than in the
        // composition root.
        services.AddHostedService<ReferenceDataImportHostedService>();
        services.AddHostedService<MarketDataIngestionHostedService>();
    }
}
