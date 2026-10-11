using System.Text.RegularExpressions;
using Npgsql;

namespace PersonalQuant.IntegrationTests;

/// <summary>
/// Proves the boundary of ADR-016 against real PostgreSQL, as the role the
/// quant worker connects as.
/// </summary>
/// <remarks>
/// <para>
/// "Python may not write quant" is a sentence until a connection made as
/// <c>quant_research</c> tries and is refused. These tests are that
/// connection. A convention that only a code review enforces is not a
/// boundary.
/// </para>
/// <para>
/// Each test gets a database of its own, migrated by the real host, because
/// the role is cluster-wide and its login is reprovisioned on every start-up.
/// </para>
/// </remarks>
/// <param name="containers">Shared container fixture.</param>
[Collection(DependencyContainerTests.Name)]
public sealed partial class ResearchBoundaryTests(DependencyContainerFixture containers)
{
    private const string PermissionDenied = "42501";

    [Fact]
    public async Task The_research_role_can_read_quant()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        var database = await MigrateAsync("rb_read");
        await using var connection = await ConnectAsResearchAsync(database);

        await using var command = new NpgsqlCommand("SELECT count(*) FROM quant.exchanges", connection);

        Assert.True((long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))! >= 0);
    }

    [Theory]
    [InlineData("INSERT INTO quant.bars DEFAULT VALUES")]
    [InlineData("UPDATE quant.instruments SET updated_at_utc = now()")]
    [InlineData("DELETE FROM quant.exchanges")]
    [InlineData("TRUNCATE quant.bar_revisions")]
    [InlineData("CREATE TABLE quant.scratch (x integer)")]
    public async Task The_research_role_cannot_write_quant(string sql)
    {
        // The assertion the whole ADR rests on. Each statement is refused for
        // want of a privilege, before any row or constraint is looked at.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        var database = await MigrateAsync("rb_write");
        await using var connection = await ConnectAsResearchAsync(database);
        await using var command = new NpgsqlCommand(sql, connection);

        var refusal = await Assert.ThrowsAsync<PostgresException>(
            () => command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));

        Assert.Equal(PermissionDenied, refusal.SqlState);
    }

    [Fact]
    public async Task The_research_role_can_work_a_job_through_its_life()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        var database = await MigrateAsync("rb_jobs");
        await using var connection = await ConnectAsResearchAsync(database);
        var id = Guid.CreateVersion7();

        await ExecuteAsync(
            connection,
            "INSERT INTO research.jobs (id, kind, payload, status, created_at_utc) "
                + "VALUES (@id, 'momentum_slice', '{}'::jsonb, 'queued', now())",
            id);
        await ExecuteAsync(
            connection,
            "UPDATE research.jobs SET status = 'running', claimed_by = 'test', "
                + "claimed_at_utc = now(), lease_until_utc = now() + interval '5 minutes', "
                + "attempts = attempts + 1 WHERE id = @id",
            id);
        await ExecuteAsync(
            connection,
            "UPDATE research.jobs SET status = 'succeeded', result = '{\"ok\": true}'::jsonb, "
                + "finished_at_utc = now() WHERE id = @id",
            id);
        await ExecuteAsync(connection, "DELETE FROM research.jobs WHERE id = @id", id);
    }

    [Fact]
    public async Task The_research_role_cannot_change_the_research_schema()
    {
        // The backend owns every migration, research's included. A worker that
        // could add tables would grow a second, unversioned schema.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        var database = await MigrateAsync("rb_ddl");
        await using var connection = await ConnectAsResearchAsync(database);
        await using var command = new NpgsqlCommand("CREATE TABLE research.scratch (x integer)", connection);

        var refusal = await Assert.ThrowsAsync<PostgresException>(
            () => command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));

        Assert.Equal(PermissionDenied, refusal.SqlState);
    }

    [Fact]
    public async Task A_finished_job_must_say_how_it_finished()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        var database = await MigrateAsync("rb_outcome");
        await using var connection = await ConnectAsResearchAsync(database);
        await using var command = new NpgsqlCommand(
            "INSERT INTO research.jobs (id, kind, payload, status, created_at_utc, finished_at_utc) "
                + "VALUES (gen_random_uuid(), 'momentum_slice', '{}'::jsonb, 'failed', now(), now())",
            connection);

        var refusal = await Assert.ThrowsAsync<PostgresException>(
            () => command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));

        Assert.Equal("ck_jobs_outcome", refusal.ConstraintName);
    }

    [Fact]
    public async Task The_python_fixture_matches_the_migrated_table()
    {
        // The worker's SQL is tested in Python against a table built from
        // quant/tests/fixtures/research_jobs.sql. If that file and the
        // migration disagreed, the worker would pass its tests against a
        // table that does not exist.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        var database = await MigrateAsync("rb_fixture");
        await using var connection = await ConnectAsOwnerAsync(database);

        var fixture = SchemaReference().Replace(await File.ReadAllTextAsync(
            FixturePath(), TestContext.Current.CancellationToken), "research_fixture");
        await ExecuteAsync(connection, fixture, null);

        Assert.Equal(await DescribeAsync(connection, "research"), await DescribeAsync(connection, "research_fixture"));
    }

    private async Task<(string Host, int Port, string Database, string Username, string Password)>
        MigrateAsync(string name)
    {
        var database = await containers.CreateDatabaseAsync(name + "_" + Guid.NewGuid().ToString("N")[..8]);

        await using var factory = PersonalQuantApiFactory.WithDependencies(
            database, containers.Redis, applyMigrations: true);

        using var client = factory.CreateClient();
        _ = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        return database;
    }

    private static async Task<NpgsqlConnection> ConnectAsResearchAsync(
        (string Host, int Port, string Database, string Username, string Password) database) =>
        await OpenAsync(database with
        {
            Username = "quant_research",
            Password = PersonalQuantApiFactory.ResearchPassword,
        });

    private static Task<NpgsqlConnection> ConnectAsOwnerAsync(
        (string Host, int Port, string Database, string Username, string Password) database) =>
        OpenAsync(database);

    private static async Task<NpgsqlConnection> OpenAsync(
        (string Host, int Port, string Database, string Username, string Password) database)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = database.Host,
            Port = database.Port,
            Database = database.Database,
            Username = database.Username,
            Password = database.Password,
            Pooling = false,
        };

        var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        return connection;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, Guid? id)
    {
        await using var command = new NpgsqlCommand(sql, connection);

        if (id is { } value)
        {
            command.Parameters.AddWithValue("id", value);
        }

        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<string> DescribeAsync(NpgsqlConnection connection, string schema)
    {
        // Columns with their types, lengths, nullability and defaults, then the
        // check and key constraints as PostgreSQL itself renders them.
        await using var command = new NpgsqlCommand(
            """
            SELECT string_agg(line, E'\n' ORDER BY line) FROM (
                SELECT format('column %s %s %s %s %s', column_name, data_type,
                              coalesce(character_maximum_length::text, '-'), is_nullable,
                              coalesce(column_default, '-')) AS line
                FROM information_schema.columns
                WHERE table_schema = @schema AND table_name = 'jobs'
                UNION ALL
                SELECT format('constraint %s %s', c.conname, pg_get_constraintdef(c.oid))
                FROM pg_constraint c
                JOIN pg_class t ON t.oid = c.conrelid
                JOIN pg_namespace n ON n.oid = t.relnamespace
                WHERE n.nspname = @schema AND t.relname = 'jobs'
                UNION ALL
                SELECT format('index %s %s', i.indexname,
                              replace(i.indexdef, @schema || '.', ''))
                FROM pg_indexes i
                WHERE i.schemaname = @schema AND i.tablename = 'jobs'
            ) described
            """,
            connection);
        command.Parameters.AddWithValue("schema", schema);

        return (string)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private static string FixturePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "quant")))
        {
            directory = directory.Parent;
        }

        return directory is null
            ? throw new InvalidOperationException("The repository root holding quant/ was not found.")
            : Path.Combine(directory.FullName, "quant", "tests", "fixtures", "research_jobs.sql");
    }

    [GeneratedRegex(@"\bresearch(?=[.;])")]
    private static partial Regex SchemaReference();
}
