using PersonalQuant.Application.CorporateActions;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Cli.CommandLine;
using PersonalQuant.Cli.Commands;
using PersonalQuant.Domain.CorporateActions;
using PersonalQuant.UnitTests.Cli.Fakes;

namespace PersonalQuant.UnitTests.Cli;

/// <summary>
/// Verifies the verb that re-examines an instrument's factors on request.
/// </summary>
/// <remarks>
/// The gap the U3 reproduction found. The engine only ran for instruments an
/// import had just moved, so a backfill that restated a reference close, or
/// brought in the ex-date's bar, or raised the breach an action explains,
/// changed nothing until somebody touched an action. The workaround was to
/// ingest before recording, which is an order nobody can keep.
/// </remarks>
public sealed class AdjustCommandsTests
{
    [Fact]
    public async Task Recompute_re_examines_everything_for_the_named_instrument()
    {
        // Changed would be the import's scope, and the import's scope is
        // exactly what could not see a backfill.
        var harness = new Harness();
        var instrument = harness.Instruments.Add("FPT");

        var code = await harness.RunAsync("adjust", "recompute", "--instrument", "FPT");

        Assert.Equal(ExitCode.Ok, code);

        var run = Assert.Single(harness.Adjustments.Runs);
        Assert.Equal(instrument.InstrumentId, run.Instrument);
        Assert.Equal(RecomputeScope.Everything, run.Scope);
    }

    [Fact]
    public async Task What_the_run_did_is_reported_in_full()
    {
        var harness = new Harness();
        harness.Instruments.Add("FPT");
        harness.Adjustments.Then(id => new AdjustmentRun(id, 2, 1, 1, 0, 1, 0, []));

        await harness.RunAsync("adjust", "recompute", "--instrument", "FPT");

        Assert.Contains("FPT", harness.Result, StringComparison.Ordinal);
        Assert.Contains("2 actions", harness.Result, StringComparison.Ordinal);
        Assert.Contains("1 computed", harness.Result, StringComparison.Ordinal);
        Assert.Contains("1 unchanged", harness.Result, StringComparison.Ordinal);
        Assert.Contains("1 finding explained", harness.Result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_instrument_with_no_actions_says_so()
    {
        var harness = new Harness();
        harness.Instruments.Add("FPT");

        var code = await harness.RunAsync("adjust", "recompute", "--instrument", "FPT");

        Assert.Equal(ExitCode.Ok, code);
        Assert.Contains("no corporate actions", harness.Result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_rejected_action_is_listed_and_the_run_is_reported_as_refused()
    {
        // A factor that could not be computed leaves the series unadjusted for
        // an event the system knows about. Exit 0 would let a script carry on
        // as though it had been.
        var harness = new Harness();
        harness.Instruments.Add("FPT");
        harness.Adjustments.Then(id => new AdjustmentRun(
            id, 1, 0, 0, 0, 0, 0,
            [
                new AdjustmentRejection(
                    CorporateActionId.New(),
                    CorporateActionType.CashDividend,
                    new DateOnly(2016, 5, 27),
                    "No daily bar is stored before the ex-date."),
            ]));

        var code = await harness.RunAsync("adjust", "recompute", "--instrument", "FPT");

        Assert.Equal(ExitCode.Refused, code);
        Assert.Contains("2016-05-27", harness.Result, StringComparison.Ordinal);
        Assert.Contains("CashDividend", harness.Result, StringComparison.Ordinal);
        Assert.Contains("No daily bar", harness.Result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_finding_raised_is_pointed_at_the_listing_that_shows_it()
    {
        var harness = new Harness();
        harness.Instruments.Add("FPT");
        harness.Adjustments.Then(id => new AdjustmentRun(id, 1, 1, 0, 0, 0, 1, []));

        var code = await harness.RunAsync("adjust", "recompute", "--instrument", "FPT");

        Assert.Equal(ExitCode.Ok, code);
        Assert.Contains("1 finding raised", harness.Result, StringComparison.Ordinal);
        Assert.Contains("pqt quality list --instrument FPT", harness.Result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_ticker_is_refused_without_running_the_engine()
    {
        var harness = new Harness();

        var code = await harness.RunAsync("adjust", "recompute", "--instrument", "NOPE");

        Assert.Equal(ExitCode.Refused, code);
        Assert.Empty(harness.Adjustments.Runs);
        Assert.Contains("NOPE", harness.Problems, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("adjust", "recompute")]
    [InlineData("adjust", "recompute", "--instrument", "FPT", "--scope", "changed")]
    [InlineData("adjust", "apply", "--instrument", "FPT")]
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
        private readonly AdjustCommands _commands;
        private readonly RecordedOutput _output = new();

        public Harness()
            : this(constructible: true)
        {
        }

        private Harness(bool constructible)
        {
            Instruments = new FakeInstrumentResolver();
            Adjustments = new FakePriceAdjustmentService();

            _commands = constructible
                ? new AdjustCommands(
                    new Lazy<IPriceAdjustmentService>(() => Adjustments),
                    new Lazy<IInstrumentResolver>(() => Instruments),
                    _output.Output)
                : new AdjustCommands(
                    Unreachable.Service<IPriceAdjustmentService>(),
                    Unreachable.Service<IInstrumentResolver>(),
                    _output.Output);
        }

        public FakeInstrumentResolver Instruments { get; }

        public FakePriceAdjustmentService Adjustments { get; }

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
