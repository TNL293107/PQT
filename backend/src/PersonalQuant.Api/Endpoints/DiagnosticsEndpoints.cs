using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PersonalQuant.Api.Diagnostics;
using PersonalQuant.Infrastructure;

namespace PersonalQuant.Api.Endpoints;

/// <summary>
/// Infrastructure-level endpoints. These are the only endpoints that exist in
/// Phase 0; business endpoints arrive from Phase 1 onwards.
/// </summary>
internal static class DiagnosticsEndpoints
{
    /// <summary>Health check tag for the process-local liveness probe.</summary>
    public const string LivenessTag = "live";

    /// <summary>Name of the liveness check.</summary>
    public const string SelfCheckName = "self";

    /// <summary>
    /// Maps <c>GET /health</c> and <c>GET /health/ready</c>.
    /// </summary>
    /// <remarks>
    /// The split is the standard liveness/readiness distinction:
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <c>/health</c> answers "is this process alive?". It touches no external
    /// dependency, so a restart supervisor never kills the API because
    /// PostgreSQL is briefly down.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <c>/health/ready</c> answers "can this process serve traffic?". It
    /// includes PostgreSQL and Redis, so a load balancer or Compose dependency
    /// waits until the dependencies really answer.
    /// </description>
    /// </item>
    /// </list>
    /// </remarks>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The same builder, to allow chaining.</returns>
    public static IEndpointRouteBuilder MapDiagnosticsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapHealthChecks("/health", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(LivenessTag),
            ResponseWriter = HealthReportResponseWriter.WriteAsync,
        })
        .WithName("Liveness")
        .WithTags("Diagnostics")
        .AllowAnonymous();

        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration =>
                registration.Tags.Contains(InfrastructureServiceCollectionExtensions.ReadinessTag),
            ResponseWriter = HealthReportResponseWriter.WriteAsync,
        })
        .WithName("Readiness")
        .WithTags("Diagnostics")
        .AllowAnonymous();

        return endpoints;
    }

    /// <summary>
    /// Registers the liveness check, which reports the process itself.
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <returns>The same builder, to allow chaining.</returns>
    public static IHealthChecksBuilder AddLivenessCheck(this IHealthChecksBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddCheck(
            SelfCheckName,
            () => HealthCheckResult.Healthy("The API process is running."),
            [LivenessTag]);
    }
}
