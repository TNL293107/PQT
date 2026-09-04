using PersonalQuant.Application.Abstractions;
using PersonalQuant.Application.Exchanges;
using PersonalQuant.Cli.CommandLine;
using PersonalQuant.Cli.Commands;
using PersonalQuant.UnitTests.Cli.Fakes;

namespace PersonalQuant.UnitTests.Cli;

/// <summary>
/// Verifies the two questions a deployment could not previously be asked.
/// </summary>
/// <remarks>
/// Both cover degradations that are correct and silent. A database behind the
/// build answers every query; a calendar that has run out reports completeness
/// as unmeasured rather than wrongly. Neither raises an error and neither fails
/// a health check, so the only defence is being able to ask.
/// </remarks>
public sealed class DeploymentCommandsTests
{
    private static readonly DateTimeOffset Today = new(2026, 9, 4, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_schema_that_matches_the_build_reports_so_and_exits_zero()
    {
        var harness = new Harness();
        harness.Schema.Holds(appliedCount: 21, lastApplied: "20260828_AddBarRevisions");

        var code = await harness.RunAsync("schema", "status");

        Assert.Equal(ExitCode.Ok, code);
        Assert.Contains("21", harness.Result, StringComparison.Ordinal);
        Assert.Contains("20260828_AddBarRevisions", harness.Result, StringComparison.Ordinal);
        Assert.Contains(
            "holds the schema this build expects", harness.Result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_schema_behind_the_build_names_every_migration_and_exits_non_zero()
    {
        // The state that went unnoticed: an image running against a database
        // nine migrations behind it, each internally consistent, nothing said.
        var harness = new Harness();
        harness.Schema.Holds(
            appliedCount: 12,
            lastApplied: "20260801_AddUniverses",
            "20260810_AddCoverage",
            "20260828_AddBarRevisions");

        var code = await harness.RunAsync("schema", "status");

        Assert.Equal(ExitCode.Refused, code);
        Assert.Contains("20260810_AddCoverage", harness.Result, StringComparison.Ordinal);
        Assert.Contains("20260828_AddBarRevisions", harness.Result, StringComparison.Ordinal);
        Assert.Contains("2 migrations behind", harness.Problems, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_database_that_was_never_migrated_is_not_reported_as_having_fallen_behind()
    {
        // Different states, different remedies. An empty database was never
        // initialised; one missing three migrations stopped being maintained.
        var harness = new Harness();
        harness.Schema.Holds(appliedCount: 0, lastApplied: null, "20260801_Initial");

        var code = await harness.RunAsync("schema", "status");

        Assert.Equal(ExitCode.Refused, code);
        Assert.Contains("never been migrated", harness.Problems, StringComparison.Ordinal);
        Assert.DoesNotContain("fallen behind.", harness.Problems, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_empty_database_renders_its_last_migration_as_unknown_not_as_none()
    {
        var harness = new Harness();
        harness.Schema.Holds(appliedCount: 0, lastApplied: null, "20260801_Initial");

        await harness.RunAsync("schema", "status");

        Assert.Contains("unknown", harness.Result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_calendar_covering_today_with_room_to_spare_exits_zero()
    {
        var harness = new Harness();
        harness.Calendar.Covers("HOSE", new DateOnly(2026, 12, 31));

        var code = await harness.RunAsync("calendar", "status");

        Assert.Equal(ExitCode.Ok, code);
        Assert.Contains("2026-12-31", harness.Result, StringComparison.Ordinal);
        Assert.Contains("covered", harness.Result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_calendar_running_out_within_the_notice_window_says_so_before_it_lapses()
    {
        // The whole point. Coverage ending is not an error and produces none —
        // completeness simply becomes unmeasurable — so the warning has to
        // arrive while there is still something to do about it.
        var harness = new Harness();
        harness.Calendar.Covers("HOSE", new DateOnly(2026, 10, 31));

        var code = await harness.RunAsync("calendar", "status");

        Assert.Equal(ExitCode.Ok, code);
        Assert.Contains("expiring", harness.Result, StringComparison.Ordinal);
        Assert.Contains("57 days", harness.Problems, StringComparison.Ordinal);
        Assert.Contains("transcribed", harness.Problems, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_calendar_that_has_lapsed_exits_non_zero()
    {
        var harness = new Harness();
        harness.Calendar.Covers("HOSE", new DateOnly(2026, 8, 1));

        var code = await harness.RunAsync("calendar", "status");

        Assert.Equal(ExitCode.Refused, code);
        Assert.Contains("lapsed", harness.Result, StringComparison.Ordinal);
        Assert.Contains("reported as unmeasured", harness.Problems, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_venue_declaring_no_coverage_is_not_the_same_as_one_that_lapsed()
    {
        // No claim was ever made about it. Collapsing "never recorded" into
        // "expired" is the same mistake as reading an unstated coverage floor
        // as unbounded.
        var harness = new Harness();
        harness.Calendar.Covers("HOSE", new DateOnly(2026, 12, 31));
        harness.Calendar.Covers("UPCOM", through: null);

        var code = await harness.RunAsync("calendar", "status");

        Assert.Equal(ExitCode.Ok, code);
        Assert.Contains("not declared", harness.Result, StringComparison.Ordinal);
        Assert.Contains("never been measurable", harness.Result, StringComparison.Ordinal);
        Assert.DoesNotContain("lapsed", harness.Result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_undeclared_calendar_renders_its_reach_as_unknown_rather_than_blank()
    {
        var harness = new Harness();
        harness.Calendar.Covers("UPCOM", through: null);

        await harness.RunAsync("calendar", "status");

        Assert.Contains("unknown", harness.Result, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("schema", "status", "--verbose")]
    [InlineData("schema", "apply")]
    [InlineData("calendar", "extend")]
    public async Task A_command_line_the_operator_got_wrong_never_reaches_the_deployment(
        params string[] args)
    {
        var harness = Harness.WithNothingConstructible();

        var code = await harness.RunAsync(args);

        Assert.Equal(ExitCode.Usage, code);
        Assert.NotEmpty(harness.Problems);
    }

    /// <summary>Wires the command class over in-memory services.</summary>
    private sealed class Harness
    {
        private readonly DeploymentCommands _commands;
        private readonly RecordedOutput _output = new();

        public Harness()
            : this(constructible: true)
        {
        }

        private Harness(bool constructible)
        {
            Schema = new FakeSchemaState();
            Calendar = new FakeTradingCalendar();

            _commands = constructible
                ? new DeploymentCommands(
                    new Lazy<ISchemaState>(() => Schema),
                    new Lazy<ITradingCalendar>(() => Calendar),
                    new Lazy<IClock>(() => new FixedClock(Today)),
                    _output.Output)
                : new DeploymentCommands(
                    Unreachable.Service<ISchemaState>(),
                    Unreachable.Service<ITradingCalendar>(),
                    Unreachable.Service<IClock>(),
                    _output.Output);
        }

        public FakeSchemaState Schema { get; }

        public FakeTradingCalendar Calendar { get; }

        public string Result => _output.Result;

        public string Problems => _output.Problems;

        /// <summary>A harness whose every service throws when constructed.</summary>
        public static Harness WithNothingConstructible() => new(constructible: false);

        public async Task<int> RunAsync(params string[] args)
        {
            Assert.True(
                CommandArguments.TryParse(args, out var command, out var problem), problem);

            return await _commands.RunAsync(command, TestContext.Current.CancellationToken);
        }
    }
}
