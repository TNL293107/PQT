using Microsoft.Extensions.DependencyInjection;
using PersonalQuant.Application.Instruments;

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
    /// through. Neither service holds state between requests; the lifetime is
    /// dictated by their dependencies.
    /// </remarks>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, to allow chaining.</returns>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IInstrumentSearchService, InstrumentSearchService>();
        services.AddScoped<IInstrumentResolver, InstrumentResolver>();

        return services;
    }
}
