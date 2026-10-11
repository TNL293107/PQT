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

    [Theory]
    [InlineData("SELECT count(*) FROM terminal.watchlists")]
    [InlineData("DELETE FROM terminal.watchlist_entries")]
    [InlineData("SELECT entries FROM terminal.workspaces")]
    public async Task The_research_role_has_no_access_to_operator_state(string sql)
    {
        // Watchlists are the operator's, not research input or output
        // (ADR-027): the role is granted nothing in the terminal schema.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        var database = await MigrateAsync("rb_terminal");
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
    public async Task The_research_role_can_record_a_run_with_its_metrics()
    {
        // The experiment store's tables arrived in a later migration than the
        // grants. Default privileges are what reach them; this proves they do.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        var database = await MigrateAsync("rb_runs");
        await using var connection = await ConnectAsResearchAsync(database);
        var experiment = Guid.CreateVersion7();
        var run = Guid.CreateVersion7();

        await ExecuteAsync(
            connection,
            "INSERT INTO research.experiments (id, name, created_at_utc) VALUES (@id, 'momentum', now())",
            experiment);
        await ExecuteAsync(connection, RunInsert(run, experiment, "1, 1, NULL"), null);
        await ExecuteAsync(
            connection,
            "INSERT INTO research.run_metrics (run_id, name, split, value) VALUES (@id, 'ic_mean', 'all', 0.07)",
            run);
        await ExecuteAsync(
            connection,
            "INSERT INTO research.run_artifacts (run_id, kind, path, sha256) "
                + "VALUES (@id, 'report', 'runs/x.json', repeat('a', 64))",
            run);
    }

    [Fact]
    public async Task A_reproduction_carries_no_trial_and_a_configuration_always_does()
    {
        // trial_count is a statistical input. A reproduction is not a new
        // configuration and must not be counted as one; a configuration
        // without its count would leave the deflated statistics guessing.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        var database = await MigrateAsync("rb_trial");
        await using var connection = await ConnectAsResearchAsync(database);
        var experiment = Guid.CreateVersion7();
        var original = Guid.CreateVersion7();

        await ExecuteAsync(
            connection,
            "INSERT INTO research.experiments (id, name, created_at_utc) VALUES (@id, 'trials', now())",
            experiment);
        await ExecuteAsync(connection, RunInsert(original, experiment, "1, 1, NULL"), null);

        foreach (var trial in new[]
        {
            "NULL, NULL, NULL",
            $"1, 1, '{original}'",
            "2, 1, NULL",
        })
        {
            await using var command = new NpgsqlCommand(
                RunInsert(Guid.CreateVersion7(), experiment, trial), connection);

            var refusal = await Assert.ThrowsAsync<PostgresException>(
                () => command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));

            Assert.Equal("ck_runs_trial", refusal.ConstraintName);
        }

        await ExecuteAsync(
            connection, RunInsert(Guid.CreateVersion7(), experiment, $"NULL, NULL, '{original}'"), null);
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
        // The worker's SQL is tested in Python against tables built from
        // quant/tests/fixtures/research_schema.sql. If that file and the
        // migrations disagreed, the worker would pass its tests against a
        // schema that does not exist.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        var database = await MigrateAsync("rb_fixture");
        await using var connection = await ConnectAsOwnerAsync(database);

        var fixture = SchemaReference().Replace(await File.ReadAllTextAsync(
            FixturePath(), TestContext.Current.CancellationToken), "research_fixture");
        await ExecuteAsync(connection, fixture, null);

        Assert.Equal(await DescribeAsync(connection, "research"), await DescribeAsync(connection, "research_fixture"));
    }

    private static string RunInsert(Guid run, Guid experiment, string trial) =>
        "INSERT INTO research.runs (id, experiment_id, status, strategy, universe_code, membership, "
            + "dataset_id, dataset_version, schema_version, content_hash, factor_version, params, "
            + "params_hash, research_engine, research_engine_version, trial_index, trial_count, "
            + "reproduces_run_id, started_at_utc, finished_at_utc) "
            + $"VALUES ('{run}', '{experiment}', 'succeeded', 'momentum_slice', 'VN30', "
            + "'point_in_time', '3856e4ce77841c1a', 3, 2, repeat('b', 64), 'momentum_12_1@1', "
            + "'{}'::jsonb, repeat('c', 64), 'personal_quant', '0.1.0', "
            + $"{trial}, now(), now())";

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
        // Every table's columns with their types, lengths, nullability and
        // defaults, then its constraints and indexes as PostgreSQL itself
        // renders them, with the schema's own name taken out so the copy and
        // the original can be compared.
        await using var command = new NpgsqlCommand(
            """
            SELECT string_agg(line, E'\n' ORDER BY line) FROM (
                SELECT format('column %s.%s %s %s %s %s', table_name, column_name, data_type,
                              coalesce(character_maximum_length::text, '-'), is_nullable,
                              coalesce(column_default, '-')) AS line
                FROM information_schema.columns
                WHERE table_schema = @schema
                UNION ALL
                SELECT format('constraint %s.%s %s', t.relname, c.conname,
                              replace(pg_get_constraintdef(c.oid), @schema || '.', ''))
                FROM pg_constraint c
                JOIN pg_class t ON t.oid = c.conrelid
                JOIN pg_namespace n ON n.oid = t.relnamespace
                WHERE n.nspname = @schema
                UNION ALL
                SELECT format('index %s.%s %s', i.tablename, i.indexname,
                              replace(i.indexdef, @schema || '.', ''))
                FROM pg_indexes i
                WHERE i.schemaname = @schema
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
            : Path.Combine(directory.FullName, "quant", "tests", "fixtures", "research_schema.sql");
    }

    [GeneratedRegex(@"\bresearch(?=[.;])")]
    private static partial Regex SchemaReference();
}
