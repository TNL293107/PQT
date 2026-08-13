using System.Text.Json.Serialization;

namespace PersonalQuant.IntegrationTests;

/// <summary>
/// Client-side view of the JSON contract produced by the health endpoints.
/// The frontend's system status page consumes the same shape, so a change
/// here is a change to a published contract.
/// </summary>
/// <param name="Status">Aggregate status.</param>
/// <param name="TotalDurationMs">Total evaluation time.</param>
/// <param name="Checks">Per-dependency results.</param>
public sealed record HealthResponse(
    string Status,
    double TotalDurationMs,
    IReadOnlyList<HealthCheckEntry> Checks)
{
    /// <summary>Finds a check by name, or <see langword="null"/>.</summary>
    /// <param name="name">The check name.</param>
    /// <returns>The matching entry, if present.</returns>
    public HealthCheckEntry? Check(string name) =>
        Checks.FirstOrDefault(check => string.Equals(check.Name, name, StringComparison.Ordinal));
}

/// <summary>A single dependency's health result.</summary>
/// <param name="Name">Check name.</param>
/// <param name="Status">Check status.</param>
/// <param name="DurationMs">Evaluation time.</param>
/// <param name="Description">Human-readable summary.</param>
public sealed record HealthCheckEntry(
    string Name,
    string Status,
    double DurationMs,
    [property: JsonPropertyName("description")] string? Description);
