using System.ComponentModel.DataAnnotations;
using Npgsql;

namespace PersonalQuant.Infrastructure.Configuration;

/// <summary>
/// PostgreSQL connection settings, bound from the <c>Postgres</c>
/// configuration section and validated at application start.
/// </summary>
public sealed class PostgresOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Postgres";

    /// <summary>Gets or sets the database host name.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Host { get; set; } = string.Empty;

    /// <summary>Gets or sets the database port.</summary>
    [Range(1, 65535)]
    public int Port { get; set; } = 5432;

    /// <summary>Gets or sets the database name.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Database { get; set; } = string.Empty;

    /// <summary>Gets or sets the login user name.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Username { get; set; } = string.Empty;

    /// <summary>Gets or sets the login password.</summary>
    /// <remarks>
    /// Supplied through the environment only. It must never be written to a
    /// committed configuration file.
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    public string Password { get; set; } = string.Empty;

    /// <summary>Gets or sets the per-command timeout in seconds.</summary>
    [Range(1, 600)]
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>Gets or sets the connect timeout in seconds.</summary>
    /// <remarks>
    /// Bounds how long the readiness probe can block when the database is
    /// unreachable but not actively refusing connections.
    /// </remarks>
    [Range(1, 300)]
    public int ConnectTimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// Gets or sets a value indicating whether pending EF Core migrations are
    /// applied automatically when the host starts.
    /// </summary>
    /// <remarks>
    /// Convenient for local development and containers. It defaults to
    /// <see langword="false"/> so that a deployed environment has to opt in
    /// rather than migrate itself by accident.
    /// </remarks>
    public bool ApplyMigrationsOnStartup { get; set; }

    /// <summary>
    /// Builds a connection string from the validated settings.
    /// </summary>
    /// <remarks>
    /// The builder escapes every value, so no user-supplied text is ever
    /// concatenated into the connection string.
    /// </remarks>
    /// <returns>An Npgsql connection string.</returns>
    public string BuildConnectionString() =>
        new NpgsqlConnectionStringBuilder
        {
            Host = Host,
            Port = Port,
            Database = Database,
            Username = Username,
            Password = Password,
            CommandTimeout = CommandTimeoutSeconds,
            Timeout = ConnectTimeoutSeconds,
            IncludeErrorDetail = false,
        }.ConnectionString;
}
