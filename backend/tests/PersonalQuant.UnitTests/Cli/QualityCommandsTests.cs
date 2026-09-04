using PersonalQuant.Application.Instruments;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Cli.CommandLine;
using PersonalQuant.Cli.Commands;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.UnitTests.Cli.Fakes;

namespace PersonalQuant.UnitTests.Cli;

/// <summary>
/// Verifies the surface a person closes a finding on.
/// </summary>
/// <remarks>
/// The gap this group fills is specific. A finding stays open until something
/// accounts for it and the consistency score decays while it does, but the only
/// caller able to close one was Phase 4 matching a price-limit breach to a
/// corporate action. A calendar that named a session the exchange had moved
/// produced a finding nobody could close, on a series that would have been
/// scored as suspect indefinitely.
/// </remarks>
public sealed class QualityCommandsTests
{
    private static readonly DateTimeOffset Session = new(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task An_open_finding_is_listed_with_the_identifier_that_closes_it()
    {
        var harness = new Harness();
        var instrument = harness.Instruments.Add("FPT");

        var finding = harness.Quality.Add(
            instrument.InstrumentId,
            DataQualityIssueKind.MissingSession,
            Session,
            "The calendar expects a session and no source has a bar for one.");

        var code = await harness.RunAsync("quality", "list", "--instrument", "FPT");

        Assert.Equal(ExitCode.Ok, code);
        Assert.Contains(finding.Id.Value.ToString(), harness.Result, StringComparison.Ordinal);
        Assert.Contains("MissingSession", harness.Result, StringComparison.Ordinal);
        Assert.Contains("2026-01-02", harness.Result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_series_with_nothing_open_says_so_rather_than_printing_an_empty_table()
    {
        var harness = new Harness();
        harness.Instruments.Add("FPT");

        var code = await harness.RunAsync("quality", "list", "--instrument", "FPT");

        Assert.Equal(ExitCode.Ok, code);
        Assert.Contains("no open findings", harness.Result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Explaining_a_finding_closes_it_and_records_what_accounts_for_it()
    {
        var harness = new Harness();
        var instrument = harness.Instruments.Add("FPT");

        var finding = harness.Quality.Add(
            instrument.InstrumentId,
            DataQualityIssueKind.MissingSession,
            Session,
            "The calendar expects a session and no source has a bar for one.");

        var code = await harness.RunAsync(
            "quality", "resolve", finding.Id.Value.ToString(),
            "--explained",
            "--reason", "2 January 2026 was swapped to Saturday 10 January by decree.");

        Assert.Equal(ExitCode.Ok, code);
        Assert.Equal(DataQualityIssueStatus.Explained, finding.Status);
        Assert.False(finding.IsOpen);
        Assert.Contains("decree", harness.Result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_closed_finding_no_longer_appears_in_the_listing()
    {
        var harness = new Harness();
        var instrument = harness.Instruments.Add("FPT");

        var finding = harness.Quality.Add(
            instrument.InstrumentId, DataQualityIssueKind.MissingSession, Session, "missing");

        await harness.RunAsync(
            "quality", "resolve", finding.Id.Value.ToString(),
            "--explained", "--reason", "moved by decree");

        var code = await harness.RunAsync("quality", "list", "--instrument", "FPT");

        Assert.Equal(ExitCode.Ok, code);
        Assert.Contains("no open findings", harness.Result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Closing_a_finding_twice_is_refused_rather_than_quietly_repeated()
    {
        // The audit trail is the reason the finding exists. Overwriting the
        // first resolution with a second would erase who accounted for it and
        // when.
        var harness = new Harness();
        var instrument = harness.Instruments.Add("FPT");

        var finding = harness.Quality.Add(
            instrument.InstrumentId, DataQualityIssueKind.MissingSession, Session, "missing");

        await harness.RunAsync(
            "quality", "resolve", finding.Id.Value.ToString(),
            "--explained", "--reason", "moved by decree");

        var code = await harness.RunAsync(
            "quality", "resolve", finding.Id.Value.ToString(),
            "--dismissed", "--reason", "actually nothing was there");

        Assert.Equal(ExitCode.Refused, code);
        Assert.Equal(DataQualityIssueStatus.Explained, finding.Status);
    }

    [Fact]
    public async Task A_finding_that_does_not_exist_is_refused_and_named()
    {
        var harness = new Harness();
        var missing = Guid.CreateVersion7();

        var code = await harness.RunAsync(
            "quality", "resolve", missing.ToString(), "--dismissed", "--reason", "nothing there");

        Assert.Equal(ExitCode.Refused, code);
        Assert.Contains(missing.ToString(), harness.Problems, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--explained")]
    [InlineData("--dismissed")]
    public async Task Closing_a_finding_needs_a_reason(string outcome)
    {
        var harness = Harness.WithNothingConstructible();

        var code = await harness.RunAsync(
            "quality", "resolve", Guid.CreateVersion7().ToString(), outcome);

        Assert.Equal(ExitCode.Usage, code);
        Assert.Contains("--reason", harness.Problems, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Neither_outcome_is_refused_because_they_are_opposite_claims()
    {
        // Explained says the discontinuity was real and something accounts for
        // it; dismissed says there was nothing there. A default would pick one
        // of two opposite statements about the data on the operator's behalf.
        var harness = Harness.WithNothingConstructible();

        var code = await harness.RunAsync(
            "quality", "resolve", Guid.CreateVersion7().ToString(), "--reason", "because");

        Assert.Equal(ExitCode.Usage, code);
        Assert.Contains("--explained", harness.Problems, StringComparison.Ordinal);
        Assert.Contains("--dismissed", harness.Problems, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Both_outcomes_at_once_is_refused()
    {
        var harness = Harness.WithNothingConstructible();

        var code = await harness.RunAsync(
            "quality", "resolve", Guid.CreateVersion7().ToString(),
            "--explained", "--dismissed", "--reason", "because");

        Assert.Equal(ExitCode.Usage, code);
    }

    [Fact]
    public async Task A_status_the_service_cannot_answer_is_refused_rather_than_ignored()
    {
        // Listing the open findings under a heading that says closed is worse
        // than refusing: the operator reads a set that answers a question they
        // did not ask.
        var harness = Harness.WithNothingConstructible();

        var code = await harness.RunAsync(
            "quality", "list", "--instrument", "FPT", "--status", "closed");

        Assert.Equal(ExitCode.Usage, code);
        Assert.Contains("Only open findings", harness.Problems, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("quality", "list")]
    [InlineData("quality", "list", "--instrument", "FPT", "--limit", "0")]
    [InlineData("quality", "resolve", "not-a-guid", "--explained", "--reason", "x")]
    [InlineData("quality", "close", "--instrument", "FPT")]
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
        private readonly QualityCommands _commands;
        private readonly RecordedOutput _output = new();

        public Harness()
            : this(constructible: true)
        {
        }

        private Harness(bool constructible)
        {
            Instruments = new FakeInstrumentResolver();
            Quality = new FakeDataQualityService();

            _commands = constructible
                ? new QualityCommands(
                    new Lazy<IDataQualityService>(() => Quality),
                    new Lazy<IInstrumentResolver>(() => Instruments),
                    _output.Output)
                : new QualityCommands(
                    Unreachable.Service<IDataQualityService>(),
                    Unreachable.Service<IInstrumentResolver>(),
                    _output.Output);
        }

        public FakeInstrumentResolver Instruments { get; }

        public FakeDataQualityService Quality { get; }

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
