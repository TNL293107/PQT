using Microsoft.Extensions.DependencyInjection;

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
    /// Phase 0 registers nothing: there are no use cases yet. The seam exists
    /// so that Phase 1 adds registrations here rather than in the API host.
    /// </remarks>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, to allow chaining.</returns>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services;
    }
}
