using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using PersonalQuant.Api.Contracts;

namespace PersonalQuant.IntegrationTests;

/// <summary>
/// Verifies <c>GET /research/runs</c>: the CLI's rows, every metric, and each
/// run's experiment trial count beside it, newest first (sprint 11.2).
/// </summary>
[Collection(DependencyContainerTests.Name)]
public sealed class ResearchEndpointTests(DependencyContainerFixture containers)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task With_no_runs_the_answer_is_an_empty_list_and_the_exploratory_note()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var host = await HostAsync("re_empty");

        var body = await GetAsync(host.Client, "/research/runs");

        Assert.Empty(body.Runs);
        Assert.Contains("exploratory", body.Note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Each_run_carries_its_metrics_and_its_experiment_s_current_trial_count()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var host = await HostAsync("re_rows");
        var experiment = await host.ExperimentAsync("phase-7-vn30");

        // Three trials and a reproduction of the first. The first run recorded
        // trial_count 1 when it finished; the experiment has tried 3 since.
        var first = await host.RunAsync(experiment, "momentum_12_1@1", "1, 1, NULL", minutesAgo: 40);
        await host.RunAsync(experiment, "size@1", "2, 2, NULL", minutesAgo: 30);
        await host.RunAsync(experiment, "value_ep@1", "3, 3, NULL", minutesAgo: 20);
        await host.RunAsync(experiment, "momentum_12_1@1", $"NULL, NULL, '{first}'", minutesAgo: 10);
        await host.MetricAsync(first, "rank_ic_mean", "0.0784");
        await host.MetricAsync(first, "rank_ic_periods", "24");
        await host.MetricAsync(first, "spread_mean", "NULL");

        var body = await GetAsync(host.Client, "/research/runs");

        // Newest first, never ranked by a metric.
        Assert.Equal(4, body.Runs.Count);
        Assert.Equal(body.Runs.OrderByDescending(run => run.FinishedAtUtc).Select(run => run.RunId), body.Runs.Select(run => run.RunId));
        Assert.All(body.Runs, run => Assert.Equal(3, run.TrialCount));

        var reproduction = body.Runs[0];
        Assert.Null(reproduction.TrialIndex);
        Assert.Equal(first, reproduction.ReproducesRunId);

        var original = body.Runs.Single(run => run.RunId == first);
        Assert.Equal(1, original.TrialIndex);
        Assert.Equal("phase-7-vn30", original.Experiment);
        Assert.Equal("momentum_12_1@1", original.FactorVersion);
        Assert.Equal("3856e4ce77841c1a", original.DatasetId);
        Assert.Equal(3, original.DatasetVersion);

        // A metric the run could not measure stays absent, never 0.
        var metrics = original.Metrics.ToDictionary(metric => metric.Name, metric => metric.Value);
        Assert.Equal(0.0784, metrics["rank_ic_mean"]);
        Assert.Equal(24, metrics["rank_ic_periods"]);
        Assert.Null(metrics["spread_mean"]);
    }

    [Fact]
    public async Task The_experiment_filter_and_the_limit_are_applied_and_a_bad_limit_is_refused()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var host = await HostAsync("re_filter");
        var evaluation = await host.ExperimentAsync("phase-7-vn30");
        var correlation = await host.ExperimentAsync("phase-7-vn30-correlation");
        await host.RunAsync(evaluation, "size@1", "1, 1, NULL", minutesAgo: 30);
        await host.RunAsync(evaluation, "value_ep@1", "2, 2, NULL", minutesAgo: 20);
        await host.RunAsync(correlation, "size@1,value_ep@1", "1, 1, NULL", minutesAgo: 10);

        var only = await GetAsync(host.Client, "/research/runs?experiment=phase-7-vn30");
        var limited = await GetAsync(host.Client, "/research/runs?limit=1");
        var unknown = await GetAsync(host.Client, "/research/runs?experiment=nothing-here");
        var refused = await host.Client.GetAsync(new Uri("/research/runs?limit=0", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(2, only.Runs.Count);
        Assert.All(only.Runs, run => Assert.Equal("phase-7-vn30", run.Experiment));
        Assert.All(only.Runs, run => Assert.Equal(2, run.TrialCount));

        // The two experiments count apart: correlation never adds to the evaluation's count.
        Assert.Equal("phase-7-vn30-correlation", Assert.Single(limited.Runs).Experiment);
        Assert.Equal(1, limited.Runs[0].TrialCount);

        Assert.Empty(unknown.Runs);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [Fact]
    public async Task Every_column_the_cli_prints_is_in_the_api_row_and_from_the_same_read()
    {
        // `pqt research runs` prints run, experiment, trial, dataset, factor,
        // ic mean, t and finished, from IResearchJobService.ListRunsAsync. The
        // endpoint reads the same service; this pins that every one of those
        // columns has its value in the API row, so the two cannot drift.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var host = await HostAsync("re_cli");
        var experiment = await host.ExperimentAsync("momentum");
        var run = await host.RunAsync(experiment, "momentum_12_1@1", "1, 1, NULL", minutesAgo: 5);
        await host.MetricAsync(run, "ic_mean", "0.07");
        await host.MetricAsync(run, "t_statistic", "1.5");

        var row = Assert.Single((await GetAsync(host.Client, "/research/runs")).Runs);
        var metrics = row.Metrics.ToDictionary(metric => metric.Name, metric => metric.Value);

        Assert.Equal(run, row.RunId); // run
        Assert.Equal("momentum", row.Experiment); // experiment
        Assert.Equal((1, 1), (row.TrialIndex, row.TrialCount)); // trial "1 of 1"
        Assert.Equal(("3856e4ce77841c1a", 3), (row.DatasetId, row.DatasetVersion)); // dataset
        Assert.Equal("momentum_12_1@1", row.FactorVersion); // factor
        Assert.Equal(0.07, metrics["ic_mean"]); // ic mean
        Assert.Equal(1.5, metrics["t_statistic"]); // t
        Assert.True(row.FinishedAtUtc < DateTimeOffset.UtcNow); // finished
    }

    private static async Task<ResearchRunsResponse> GetAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<ResearchRunsResponse>(Json, TestContext.Current.CancellationToken))!;
    }

    private async Task<Host> HostAsync(string name)
    {
        var database = await containers.CreateDatabaseAsync(name + "_" + Guid.NewGuid().ToString("N")[..8]);
        var factory = PersonalQuantApiFactory.WithDependencies(database, containers.Redis, applyMigrations: true);
        var client = factory.CreateClient();

        // The first request applies the migrations.
        _ = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder
        {
            Host = database.Host,
            Port = database.Port,
            Database = database.Database,
            Username = database.Username,
            Password = database.Password,
            Pooling = false,
        }.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        return new Host(factory, client, connection);
    }

    /// <summary>The API under test, and a connection that seeds runs as the worker would write them.</summary>
    private sealed class Host(PersonalQuantApiFactory factory, HttpClient client, NpgsqlConnection connection) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        public async Task<Guid> ExperimentAsync(string name)
        {
            var id = Guid.CreateVersion7();
            await ExecuteAsync("INSERT INTO research.experiments (id, name, created_at_utc) VALUES (@id, @name, now())", id, name);
            return id;
        }

        public async Task<Guid> RunAsync(Guid experiment, string factorVersion, string trial, int minutesAgo)
        {
            var run = Guid.CreateVersion7();
            await ExecuteAsync(
                "INSERT INTO research.runs (id, experiment_id, status, strategy, universe_code, membership, "
                    + "dataset_id, dataset_version, schema_version, content_hash, factor_version, params, "
                    + "params_hash, research_engine, research_engine_version, trial_index, trial_count, "
                    + "reproduces_run_id, started_at_utc, finished_at_utc) "
                    + "VALUES (@id, @experiment, 'succeeded', 'factor_evaluation', 'VN30', 'point_in_time', "
                    + "'3856e4ce77841c1a', 3, 5, repeat('b', 64), @factor, '{}'::jsonb, repeat('c', 64), "
                    + $"'personal_quant', '0.1.0', {trial}, "
                    + $"now() - interval '{minutesAgo + 1} minutes', now() - interval '{minutesAgo} minutes')",
                run,
                null,
                experiment,
                factorVersion);
            return run;
        }

        public Task MetricAsync(Guid run, string name, string value) =>
            ExecuteAsync(
                $"INSERT INTO research.run_metrics (run_id, name, split, value) VALUES (@id, @name, 'all', {value})",
                run,
                name);

        public async ValueTask DisposeAsync()
        {
            await connection.DisposeAsync();
            Client.Dispose();
            await factory.DisposeAsync();
        }

        private async Task ExecuteAsync(string sql, Guid id, string? name = null, Guid? experiment = null, string? factor = null)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", id);

            if (name is not null)
            {
                command.Parameters.AddWithValue("name", name);
            }

            if (experiment is { } experimentId)
            {
                command.Parameters.AddWithValue("experiment", experimentId);
            }

            if (factor is not null)
            {
                command.Parameters.AddWithValue("factor", factor);
            }

            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
    }
}
