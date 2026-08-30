using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace PersonalQuant.IntegrationTests;

/// <summary>
/// Boots the real API host in memory with an explicit configuration, so a
/// test never depends on the developer's local <c>appsettings</c> or
/// environment.
/// </summary>
/// <param name="settings">Configuration entries to apply.</param>
public sealed class PersonalQuantApiFactory(IReadOnlyDictionary<string, string?> settings)
    : WebApplicationFactory<Program>
{
    /// <summary>
    /// Creates a factory pointed at dependencies that are guaranteed not to
    /// answer, to verify that readiness reports the outage.
    /// </summary>
    /// <returns>A configured factory.</returns>
    public static PersonalQuantApiFactory WithUnreachableDependencies() =>
        new(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            // Ports in the ephemeral range with nothing bound: the connection
            // is refused immediately rather than hanging until a timeout.
            ["Postgres:Host"] = "127.0.0.1",
            ["Postgres:Port"] = "59595",
            ["Postgres:Database"] = "personal_quant",
            ["Postgres:Username"] = "quant_user",
            ["Postgres:Password"] = "unused-by-this-test",
            ["Postgres:ConnectTimeoutSeconds"] = "2",
            ["Postgres:ApplyMigrationsOnStartup"] = "false",
            ["Redis:Host"] = "127.0.0.1",
            ["Redis:Port"] = "59596",
            ["Redis:ConnectTimeoutMilliseconds"] = "500",
            ["Cors:AllowedOrigins"] = "http://localhost:3000",
        });

    /// <summary>
    /// Creates a factory pointed at real containerised dependencies.
    /// </summary>
    /// <param name="postgres">PostgreSQL connection details.</param>
    /// <param name="redis">Redis connection details.</param>
    /// <param name="applyMigrations">Whether to migrate on start-up.</param>
    /// <returns>A configured factory.</returns>
    public static PersonalQuantApiFactory WithDependencies(
        (string Host, int Port, string Database, string Username, string Password) postgres,
        (string Host, int Port) redis,
        bool applyMigrations) =>
        new(BaseSettings(postgres, redis, applyMigrations));

    private static Dictionary<string, string?> BaseSettings(
        (string Host, int Port, string Database, string Username, string Password) postgres,
        (string Host, int Port) redis,
        bool applyMigrations) =>
        new(StringComparer.Ordinal)
        {
            ["Postgres:Host"] = postgres.Host,
            ["Postgres:Port"] = postgres.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Postgres:Database"] = postgres.Database,
            ["Postgres:Username"] = postgres.Username,
            ["Postgres:Password"] = postgres.Password,
            ["Postgres:ConnectTimeoutSeconds"] = "10",
            ["Postgres:ApplyMigrationsOnStartup"] =
                applyMigrations.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Redis:Host"] = redis.Host,
            ["Redis:Port"] = redis.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Redis:ConnectTimeoutMilliseconds"] = "5000",
            ["Cors:AllowedOrigins"] = "http://localhost:3000",
        };

    /// <summary>
    /// Creates a factory pointed at real dependencies and a universe source on
    /// disk.
    /// </summary>
    /// <remarks>
    /// The real file provider, reading real files. The universe import's
    /// contract is with a directory of CSV, and a fake provider in its place
    /// would prove the reconciliation while leaving the parsing and the
    /// resolution untested.
    /// </remarks>
    /// <param name="postgres">PostgreSQL connection details.</param>
    /// <param name="redis">Redis connection details.</param>
    /// <param name="universeDirectory">Directory holding the universe CSV files.</param>
    /// <returns>A configured factory.</returns>
    public static PersonalQuantApiFactory WithUniverseDirectory(
        (string Host, int Port, string Database, string Username, string Password) postgres,
        (string Host, int Port) redis,
        string universeDirectory)
    {
        var settings = BaseSettings(postgres, redis, applyMigrations: true);
        settings["MarketData:UniverseDirectory"] = universeDirectory;

        return new PersonalQuantApiFactory(settings);
    }

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment(Environments.Production);

        // Applied twice, deliberately. AddInMemoryCollection reaches anything
        // that binds options lazily, which is most of the host; UseSetting
        // reaches host configuration, which is what composition-time reads see
        // — the registration that decides whether a file source exists at all
        // happens before the app is built and cannot wait for the former.
        foreach (var (key, value) in settings.Where(entry => entry.Value is not null))
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(settings));
    }
}
