using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PersonalQuant.Api.Diagnostics;

/// <summary>
/// Serialises a <see cref="HealthReport"/> into the JSON contract consumed by
/// the terminal's system status page.
/// </summary>
/// <remarks>
/// The default writer emits only a status string. The frontend needs to show
/// each dependency separately, so a per-check breakdown is written instead.
/// Exception detail is never included.
/// </remarks>
internal static class HealthReportResponseWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Writes the report to the response body.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="report">The evaluated health report.</param>
    /// <returns>A task that completes when the body has been written.</returns>
    public static async Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        // A status endpoint that a proxy or browser caches is worse than no
        // status endpoint at all.
        context.Response.Headers.CacheControl = "no-store, no-cache";

        var payload = new HealthResponse(
            report.Status.ToString(),
            report.TotalDuration.TotalMilliseconds,
            [.. report.Entries.Select(entry => new HealthCheckResponse(
                entry.Key,
                entry.Value.Status.ToString(),
                entry.Value.Duration.TotalMilliseconds,
                entry.Value.Description))]);

        await context.Response
            .WriteAsync(JsonSerializer.Serialize(payload, SerializerOptions), context.RequestAborted)
            .ConfigureAwait(false);
    }

    private sealed record HealthResponse(
        string Status,
        double TotalDurationMs,
        IReadOnlyList<HealthCheckResponse> Checks);

    private sealed record HealthCheckResponse(
        string Name,
        string Status,
        double DurationMs,
        string? Description);
}
