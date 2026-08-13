namespace PersonalQuant.Api.Configuration;

/// <summary>
/// Cross-origin settings for browser clients (the React terminal).
/// </summary>
public sealed class CorsOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Cors";

    /// <summary>Name of the policy applied to the pipeline.</summary>
    public const string PolicyName = "PersonalQuantTerminal";

    /// <summary>
    /// Gets or sets the comma-separated list of allowed origins.
    /// </summary>
    /// <remarks>
    /// A single delimited string rather than an array, because the value is
    /// supplied as the <c>CORS_ALLOWED_ORIGINS</c> environment variable and
    /// indexed environment keys (<c>Cors__AllowedOrigins__0</c>) are awkward to
    /// write in a <c>.env</c> file.
    /// </remarks>
    public string AllowedOrigins { get; set; } = string.Empty;

    /// <summary>
    /// Parses <see cref="AllowedOrigins"/> into individual origins.
    /// </summary>
    /// <returns>
    /// The configured origins, or an empty array when none are configured.
    /// An empty array means no browser origin is permitted — the API is still
    /// fully usable by non-browser clients.
    /// </returns>
    public string[] ParseAllowedOrigins() =>
        AllowedOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
