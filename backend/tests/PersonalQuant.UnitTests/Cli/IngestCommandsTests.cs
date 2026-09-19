using PersonalQuant.Application.Instruments;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Application.Universes;
using PersonalQuant.Cli.CommandLine;
using PersonalQuant.Cli.Commands;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.Domain.Universes;
using PersonalQuant.UnitTests.Cli.Fakes;

namespace PersonalQuant.UnitTests.Cli;

/// <summary>
/// Verifies the on-demand trigger for the ingestion pipeline.
/// </summary>
/// <remarks>
/// The pipeline itself is tested elsewhere. What is under test here is that the
/// command reaches it with the instruction the operator typed, that a backfill
/// terminates, and that a command line the operator got wrong never reaches it
/// at all.
/// </remarks>
public sealed class IngestCommandsTests
{
    private static readonly DateTimeOffset Start = new(2021, 12, 27, 0, 0, 0, TimeSpan.Zero);
    private static readonly SourceCode Source = SourceCode.Create("VCI");

    [Fact]
    public async Task A_run_reaches_the_pipeline_with_what_was_typed()
    {
        var harness = new Harness();
        var instrument = harness.Instruments.Add("FPT");

        harness.Ingestion.Then(instruction => Succeeded(instruction, stored: 3));

        var code = await harness.RunAsync(
            "ingest", "run",
            "--instrument", "FPT",
            "--interval", "1d",
            "--source", "VCI",
            "--from", "2021-12-27");

        Assert.Equal(ExitCode.Ok, code);

        var instruction = Assert.Single(harness.Ingestion.Instructions);

        Assert.Equal(instrument.InstrumentId, instruction.InstrumentId);
        Assert.Equal(BarInterval.OneDay, instruction.Interval);
        Assert.Equal(Source, instruction.Source);
        Assert.Equal(Start, instruction.FromUtc);
        Assert.Null(instruction.ToUtc);
    }

    [Fact]
    public async Task A_date_becomes_midnight_utc_rather_than_the_operators_own_zone()
    {
        // A range boundary read in the shell's local zone would cover a
        // different set of sessions depending on where the operator was sitting,
        // and the run would record a range nobody asked for.
        var harness = new Harness();
        harness.Instruments.Add("FPT");
        harness.Ingestion.Then(instruction => Succeeded(instruction, stored: 1));

        await harness.RunAsync(
            "ingest", "run", "--instrument", "FPT", "--from", "2021-12-27");

        var instruction = Assert.Single(harness.Ingestion.Instructions);

        Assert.Equal(TimeSpan.Zero, instruction.FromUtc!.Value.Offset);
        Assert.Equal(new TimeOnly(0, 0), TimeOnly.FromDateTime(instruction.FromUtc.Value.DateTime));
    }

    [Fact]
    public async Task A_skipped_run_is_reported_and_exits_non_zero()
    {
        // The refusal a scheduled pass would only write to a table. A script
        // driving this has to be able to tell a stored bar from a refusal.
        var harness = new Harness();
        harness.Instruments.Add("FPT");

        harness.Ingestion.Then(instruction => Skipped(
            instruction,
            "'VCI' serves prices already adjusted for corporate actions, and the OneDay series "
                + "already holds bars from 'FILE', which serves raw prices."));

        var code = await harness.RunAsync(
            "ingest", "run", "--instrument", "FPT", "--source", "VCI");

        Assert.Equal(ExitCode.Refused, code);
        Assert.Contains("FILE", harness.Problems, StringComparison.Ordinal);
        Assert.Contains("VCI", harness.Problems, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unlisted_ticker_is_refused_before_the_pipeline_is_called()
    {
        var harness = new Harness();

        var code = await harness.RunAsync("ingest", "run", "--instrument", "NOPE");

        Assert.Equal(ExitCode.Refused, code);
        Assert.Empty(harness.Ingestion.Instructions);
        Assert.Contains("NOPE", harness.Problems, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_backfill_walks_to_the_end_of_its_range_one_window_at_a_time()
    {
        // Each pass covers what the source allows in one call (a hundred days
        // here) and the next starts where it stopped, until the end is reached.
        var harness = new Harness();
        harness.Instruments.Add("FPT");

        harness.Ingestion
            .Then(instruction => Succeeded(instruction, stored: 100))
            .Then(instruction => Succeeded(instruction, stored: 100))
            .Then(instruction => Succeeded(instruction, stored: 50));

        var code = await harness.RunAsync(
            "ingest", "backfill", "--instrument", "FPT", "--from", "2021-12-27", "--to", "2022-10-04");

        Assert.Equal(ExitCode.Ok, code);
        Assert.Equal(
            [Start, Start.AddDays(100), Start.AddDays(200)],
            harness.Ingestion.Instructions.Select(instruction => instruction.FromUtc));
        Assert.Contains("250 bars stored", harness.Result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_pass_starts_where_the_one_before_it_stopped_rather_than_at_the_checkpoint()
    {
        // The regression. Passes after the first used to leave the start open
        // and let the checkpoint decide. When the series already held later
        // bars - VN30 names backfilled for July 2026, then asked for August
        // 2024 onwards - the checkpoint sat at September 2026, the second pass
        // jumped straight to it, and twenty months were skipped with the run
        // reporting success: 45 bars stored where 480 were due.
        var harness = new Harness();
        harness.Instruments.Add("ACB");

        harness.Ingestion
            .Then(instruction => Succeeded(instruction, stored: 45))
            .Then(instruction => Succeeded(instruction, stored: 45))
            .Then(instruction => Skipped(instruction, "No period has finished since the last run."));

        await harness.RunAsync("ingest", "backfill", "--instrument", "ACB", "--from", "2021-12-27");

        Assert.All(
            harness.Ingestion.Instructions,
            instruction => Assert.NotNull(instruction.FromUtc));
        Assert.Equal(Start.AddDays(100), harness.Ingestion.Instructions[1].FromUtc);
    }

    [Fact]
    public async Task Without_an_end_a_backfill_stops_when_the_source_has_nothing_left_to_give()
    {
        // With no --to the range runs to the last finished period, and the pass
        // past it is skipped by the pipeline. That skip is how a completed
        // backfill ends, not a failure.
        var harness = new Harness();
        harness.Instruments.Add("FPT");

        harness.Ingestion
            .Then(instruction => Succeeded(instruction, stored: 10))
            .Then(instruction => Skipped(instruction, "No period has finished since the last run."));

        var code = await harness.RunAsync(
            "ingest", "backfill", "--instrument", "FPT", "--from", "2021-12-27");

        Assert.Equal(ExitCode.Ok, code);
        Assert.Equal(2, harness.Ingestion.Instructions.Count);
    }

    [Fact]
    public async Task A_window_the_source_returned_nothing_for_does_not_end_the_backfill()
    {
        // A long suspension is a window with no bars in it, not the end of the
        // series. Walking on is what keeps it from truncating a history there.
        var harness = new Harness();
        harness.Instruments.Add("FPT");

        harness.Ingestion
            .Then(instruction => Succeeded(instruction, stored: 0))
            .Then(instruction => Succeeded(instruction, stored: 30));

        var code = await harness.RunAsync(
            "ingest", "backfill", "--instrument", "FPT", "--from", "2021-12-27", "--to", "2022-06-01");

        Assert.Equal(ExitCode.Ok, code);
        Assert.Contains("30 bars stored", harness.Result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_backfill_stops_at_its_bound_and_says_it_did_not_finish()
    {
        var harness = new Harness();
        harness.Instruments.Add("FPT");

        harness.Ingestion
            .Then(instruction => Succeeded(instruction, stored: 10, from: Start))
            .Then(instruction => Succeeded(instruction, stored: 10, from: Start.AddDays(10)));

        var code = await harness.RunAsync(
            "ingest", "backfill",
            "--instrument", "FPT",
            "--from", "2021-12-27",
            "--to", "2023-12-31",
            "--max-passes", "2");

        Assert.Equal(ExitCode.Refused, code);
        Assert.Equal(2, harness.Ingestion.Instructions.Count);
        Assert.Contains("2 passes", harness.Problems, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_universe_membership_refuses_rather_than_backfilling_nothing()
    {
        // The honest answer, and the one that matters most. An unknown
        // membership treated as an empty one produces a universe backfill that
        // reports success, stores nothing, and looks sourced.
        var harness = new Harness();
        harness.Universes.DoesNotKnow(UniverseUnknownReason.NoCoverageDeclared);

        var code = await harness.RunAsync(
            "ingest", "backfill", "--universe", "VN30", "--from", "2021-12-27");

        Assert.Equal(ExitCode.Refused, code);
        Assert.Empty(harness.Ingestion.Instructions);
        Assert.Contains("NoCoverageDeclared", harness.Problems, StringComparison.Ordinal);
        Assert.Contains(
            "an unknown membership is not an empty one",
            harness.Problems,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_universe_is_read_as_of_the_day_the_backfill_starts()
    {
        // Not as of today. Backfilling today's constituents over an earlier
        // decade is the survivorship bias the universe model exists to remove,
        // and it is what this command would do by default if the date were not
        // stated.
        var harness = new Harness();
        var instrument = harness.Instruments.Add("FPT");

        harness.Universes.Knows(instrument.InstrumentId);
        harness.Ingestion
            .Then(instruction => Succeeded(instruction, stored: 5))
            .Then(instruction => Skipped(instruction, "No period has finished since the last run."));

        var code = await harness.RunAsync(
            "ingest", "backfill", "--universe", "VN30", "--from", "2021-12-27");

        Assert.Equal(ExitCode.Ok, code);
        Assert.Equal(1, harness.Universes.ReadCount);
        Assert.Equal(new DateOnly(2021, 12, 27), harness.Universes.LastAsOf);
    }

    [Fact]
    public async Task A_universe_naming_a_security_the_master_lacks_is_refused()
    {
        // The two imports disagree. A backfill quietly short by one constituent
        // is the kind of gap nobody finds later.
        var harness = new Harness();
        harness.Universes.Knows(InstrumentId.New());

        var code = await harness.RunAsync(
            "ingest", "backfill", "--universe", "VN30", "--from", "2021-12-27");

        Assert.Equal(ExitCode.Refused, code);
        Assert.Empty(harness.Ingestion.Instructions);
        Assert.Contains("does not hold", harness.Problems, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ingest", "run", "--form", "2021-12-27")]
    [InlineData("ingest", "run", "--instrument", "FPT", "--from", "27/12/2021")]
    [InlineData("ingest", "backfill", "--instrument", "FPT")]
    [InlineData("ingest", "backfill", "--from", "2021-12-27")]
    [InlineData("ingest", "backfill", "--instrument", "FPT", "--universe", "VN30", "--from", "2021-12-27")]
    [InlineData("ingest", "sync", "--instrument", "FPT")]
    public async Task A_command_line_the_operator_got_wrong_never_reaches_the_deployment(
        params string[] args)
    {
        // The whole reason every service arrives deferred. Resolved eagerly, a
        // typo is answered with four lines about a missing Postgres password
        // and the operator goes looking for a configuration problem that does
        // not exist.
        var harness = Harness.WithNothingConstructible();

        var code = await harness.RunAsync(args);

        Assert.Equal(ExitCode.Usage, code);
        Assert.NotEmpty(harness.Problems);
    }

    private static IngestionRun Succeeded(
        IngestionInstruction instruction,
        int stored,
        DateTimeOffset? from = null)
    {
        var fromUtc = instruction.FromUtc ?? from ?? Start;
        var run = Begin(instruction, fromUtc);

        run.Succeed(
            new IngestionCounts(stored, stored, 0, stored, 0),
            attempts: 1,
            RawBatchId.New(),
            fromUtc.AddDays(1));

        return run;
    }

    private static IngestionRun Skipped(IngestionInstruction instruction, string reason)
    {
        var run = Begin(instruction, instruction.FromUtc ?? Start);

        run.Skip(reason, Start.AddDays(1));

        return run;
    }

    private static IngestionRun Begin(IngestionInstruction instruction, DateTimeOffset fromUtc) =>
        IngestionRun.Start(
            instruction.Source ?? Source,
            instruction.InstrumentId,
            instruction.Interval,
            fromUtc,
            fromUtc.AddDays(100),
            fromUtc);

    /// <summary>Wires the command class over in-memory services.</summary>
    private sealed class Harness
    {
        private readonly IngestCommands _commands;
        private readonly RecordedOutput _output = new();

        public Harness()
            : this(constructible: true)
        {
        }

        private Harness(bool constructible)
        {
            Instruments = new FakeInstrumentResolver();
            Ingestion = new FakeIngestionService();
            Universes = new FakeUniverseCatalog();

            _commands = constructible
                ? new IngestCommands(
                    new Lazy<IMarketDataIngestionService>(() => Ingestion),
                    new Lazy<IInstrumentResolver>(() => Instruments),
                    new Lazy<IUniverseCatalog>(() => Universes),
                    _output.Output)
                : new IngestCommands(
                    Unreachable.Service<IMarketDataIngestionService>(),
                    Unreachable.Service<IInstrumentResolver>(),
                    Unreachable.Service<IUniverseCatalog>(),
                    _output.Output);
        }

        public FakeInstrumentResolver Instruments { get; }

        public FakeIngestionService Ingestion { get; }

        public FakeUniverseCatalog Universes { get; }

        public string Result => _output.Result;

        public string Problems => _output.Problems;

        /// <summary>
        /// A harness whose every service throws when constructed.
        /// </summary>
        /// <remarks>
        /// Asserts the ordering rather than the message: a refused command line
        /// must be answered before anything reaches for the deployment.
        /// </remarks>
        public static Harness WithNothingConstructible() => new(constructible: false);

        public async Task<int> RunAsync(params string[] args)
        {
            Assert.True(
                CommandArguments.TryParse(args, out var command, out var problem), problem);

            return await _commands.RunAsync(command, TestContext.Current.CancellationToken);
        }
    }
}
