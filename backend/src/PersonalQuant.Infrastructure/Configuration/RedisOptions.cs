using System.ComponentModel.DataAnnotations;
using StackExchange.Redis;

namespace PersonalQuant.Infrastructure.Configuration;

/// <summary>
/// Redis connection settings, bound from the <c>Redis</c> configuration
/// section and validated at application start.
/// </summary>
public sealed class RedisOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Redis";

    /// <summary>Gets or sets the Redis host name.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Host { get; set; } = string.Empty;

    /// <summary>Gets or sets the Redis port.</summary>
    [Range(1, 65535)]
    public int Port { get; set; } = 6379;

    /// <summary>Gets or sets the password, or an empty string when unset.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Gets or sets the logical database index.</summary>
    [Range(0, 15)]
    public int Database { get; set; }

    /// <summary>Gets or sets the connect timeout in milliseconds.</summary>
    [Range(100, 60000)]
    public int ConnectTimeoutMilliseconds { get; set; } = 5000;

    /// <summary>
    /// Builds the StackExchange.Redis configuration from the validated
    /// settings.
    /// </summary>
    /// <returns>Configuration for a connection multiplexer.</returns>
    public ConfigurationOptions BuildConfiguration()
    {
        var configuration = new ConfigurationOptions
        {
            // Redis being unreachable must degrade readiness, not prevent the
            // API from starting. The multiplexer reconnects in the background.
            AbortOnConnectFail = false,
            ConnectTimeout = ConnectTimeoutMilliseconds,
            DefaultDatabase = Database,
            ClientName = "personal-quant-api",
        };

        configuration.EndPoints.Add(Host, Port);

        if (!string.IsNullOrWhiteSpace(Password))
        {
            configuration.Password = Password;
        }

        return configuration;
    }
}
