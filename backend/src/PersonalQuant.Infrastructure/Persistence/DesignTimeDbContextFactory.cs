using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using PersonalQuant.Infrastructure.Configuration;

namespace PersonalQuant.Infrastructure.Persistence;

/// <summary>
/// Creates a <see cref="PersonalQuantDbContext"/> for the <c>dotnet ef</c>
/// tooling, so that migrations can be authored without booting the API host.
/// </summary>
/// <remarks>
/// <para>
/// Scaffolding a migration never opens a connection; the settings below only
/// have to describe a valid PostgreSQL target. Values come from the same
/// <c>POSTGRES_*</c> environment variables used everywhere else, with local
/// development defaults so the common case needs no setup.
/// </para>
/// <para>
/// No password default is provided. Commands that do reach the database
/// (<c>database update</c>, <c>migrations script --idempotent</c> against a
/// live server) require <c>POSTGRES_PASSWORD</c> to be exported.
/// </para>
/// </remarks>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<PersonalQuantDbContext>
{
    /// <inheritdoc />
    public PersonalQuantDbContext CreateDbContext(string[] args)
    {
        var options = new PostgresOptions
        {
            Host = ReadEnvironment("POSTGRES_HOST", "localhost"),
            Port = ReadPort("POSTGRES_PORT", 5432),
            Database = ReadEnvironment("POSTGRES_DATABASE", "personal_quant"),
            Username = ReadEnvironment("POSTGRES_USER", "quant_user"),
            Password = ReadEnvironment("POSTGRES_PASSWORD", string.Empty),
        };

        var builder = new DbContextOptionsBuilder<PersonalQuantDbContext>();
        builder.UseNpgsql(
            options.BuildConnectionString(),
            npgsql => npgsql.MigrationsHistoryTable(
                MigrationDefaults.HistoryTableName,
                PersonalQuantDbContext.Schema));

        return new PersonalQuantDbContext(builder.Options);
    }

    private static string ReadEnvironment(string name, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static int ReadPort(string name, int fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return int.TryParse(value, out var parsed) && parsed is > 0 and <= 65535
            ? parsed
            : fallback;
    }
}
