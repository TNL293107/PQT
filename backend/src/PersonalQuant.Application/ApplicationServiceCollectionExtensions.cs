using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Application.MarketData;

namespace PersonalQuant.Application;

/// <summary>
/// Composition root for the application layer.
/// </summary>
public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registers application layer services.
    /// </summary>
    /// <remarks>
    /// Scoped, matching the repositories and the unit of work they read
    /// through. The instrument services hold no state between requests; the
    /// lifetime is dictated by their dependencies. The exceptions are called
    /// out where they are registered.
    /// </remarks>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, to allow chaining.</returns>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IInstrumentSearchService, InstrumentSearchService>();
        services.AddScoped<IInstrumentResolver, InstrumentResolver>();
        services.AddScoped<IInstrumentCatalog, InstrumentCatalog>();

        AddMarketData(services);

        return services;
    }

    /// <summary>
    /// Registers the market data ingestion pipeline and its read side.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The lifetimes are not uniform here, and each departure is deliberate.
    /// The rate limiter is a singleton because a gate that is created per
    /// request does not limit anything — every caller would see an empty
    /// history and proceed immediately. The provider registry is a singleton
    /// because it is built once from whatever sources were registered, and
    /// rebuilding it per request would re-run its duplicate-code check on
    /// every call.
    /// </para>
    /// <para>
    /// The policy is added only if nothing has supplied one, so infrastructure
    /// can bind it from configuration while a test or a minimal host still
    /// gets working defaults.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection to add to.</param>
    private static void AddMarketData(IServiceCollection services)
    {
        services.TryAddSingleton(IngestionPolicy.Default);

        services.AddSingleton<IMarketDataProviderRegistry, MarketDataProviderRegistry>();
        services.AddSingleton<IMarketDataCallLimiter, MarketDataCallLimiter>();

        // Stateless and thread-safe: the normaliser holds nothing between
        // calls, so one instance serves every request.
        services.AddSingleton<IMarketDataNormalizer, MarketDataNormalizer>();

        services.AddScoped<IMarketDataFetcher, MarketDataFetcher>();
        services.AddScoped<IMarketDataIngestionService, MarketDataIngestionService>();
        services.AddScoped<IMarketDataQueryService, MarketDataQueryService>();
    }
}
