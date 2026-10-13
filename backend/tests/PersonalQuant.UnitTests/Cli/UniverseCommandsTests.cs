using PersonalQuant.Application.Breadth;
using PersonalQuant.Application.Universes;
using PersonalQuant.Cli.CommandLine;
using PersonalQuant.Cli.Commands;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.Universes;
using PersonalQuant.UnitTests.Cli.Fakes;

namespace PersonalQuant.UnitTests.Cli;

/// <summary>
/// Verifies that the universe verbs print what the service counted, in a shape
/// that survives a pipe: direction in text, unknowns as <c>unknown</c>.
/// </summary>
public sealed class UniverseCommandsTests
{
    private static readonly DateOnly Session = new(2026, 9, 30);
    private static readonly DateOnly Previous = new(2026, 9, 29);

    [Fact]
    public async Task Breadth_prints_the_summary_line_then_members_by_change()
    {
        var harness = new Harness();
        harness.Breadth.Answer = BreadthResult.Computed(new MarketBreadth(
            BreadthScope.Of(UniverseCode.Create("VN30")),
            Session,
            SessionWasDefaulted: false,
            [new BreadthVenue(ExchangeCode.Create("HOSE"), Previous)],
            [
                Member("LOW", 100m, 97m, BreadthCategory.Declining, 300),
                Member("HIGH", 100m, 104m, BreadthCategory.Advancing, 500),
                Member("NIL", null, null, BreadthCategory.NoData, null),
                Member("FLAT", 50m, 50m, BreadthCategory.Unchanged, 70),
            ]));

        var code = await harness.RunAsync("universe", "breadth", "vn30", "--on", "2026-09-30");

        Assert.Equal(ExitCode.Ok, code);
        Assert.Equal((UniverseCode.Create("VN30"), (DateOnly?)Session), harness.Breadth.Asked);

        var lines = harness.Result.Split(Environment.NewLine);
        Assert.Equal("VN30 2026-09-30  ▲ 1  ▼ 1  = 1  no data 1", lines[0]);
        Assert.Contains("volume  ▲ 500  ▼ 300  = 70", harness.Result, StringComparison.Ordinal);
        Assert.Contains("against HOSE 2026-09-29", harness.Result, StringComparison.Ordinal);

        var order = new[] { "HIGH", "FLAT", "LOW", "NIL" }
            .Select(ticker => harness.Result.IndexOf($" {ticker} ", StringComparison.Ordinal))
            .ToList();
        Assert.All(order, index => Assert.True(index > 0));
        Assert.Equal(order.Order(), order);

        Assert.Contains("+4.00%", harness.Result, StringComparison.Ordinal);
        Assert.Contains("-3.00%", harness.Result, StringComparison.Ordinal);
        Assert.Contains("no bar on the session", harness.Result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_restated_previous_close_is_explained_on_its_row()
    {
        var harness = new Harness();
        harness.Breadth.Answer = BreadthResult.Computed(new MarketBreadth(
            BreadthScope.Of(UniverseCode.Create("VN30")),
            Session,
            SessionWasDefaulted: true,
            [new BreadthVenue(ExchangeCode.Create("HOSE"), Previous)],
            [
                new BreadthMember(
                    InstrumentId.New(), Ticker.Create("DIV"), ExchangeCode.Create("HOSE"), Previous,
                    25_000m, 24_000m, 24_000m, 0m, 10, BreadthCategory.Unchanged),
            ]));

        await harness.RunAsync("universe", "breadth", "VN30");

        Assert.Null(harness.Breadth.Asked.Session);
        Assert.Contains("ex-date: previous close 25,000 restated", harness.Result, StringComparison.Ordinal);
        Assert.Contains("the latest session any member has a bar for", harness.Result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refusal_goes_to_standard_error_with_its_reason()
    {
        var harness = new Harness();
        harness.Breadth.Answer = BreadthResult.Refused(
            BreadthOutcome.NotATradingDay, BreadthScope.Of(UniverseCode.Create("VN30")), Session, "2026-09-30 is not a trading day on HOSE.");

        var code = await harness.RunAsync("universe", "breadth", "VN30", "--on", "2026-09-30");

        Assert.Equal(ExitCode.Refused, code);
        Assert.Contains("not a trading day", harness.Problems, StringComparison.Ordinal);
        Assert.Equal(string.Empty, harness.Result);
    }

    [Fact]
    public async Task List_prints_unknown_rather_than_zero_for_a_count_the_claim_does_not_reach()
    {
        var harness = new Harness();
        harness.Directory.Answer = new UniverseListing(
            new DateOnly(2026, 10, 11),
            [
                new UniverseSummary(
                    UniverseCode.Create("OLD"), "Lapsed", UniverseKind.Custom,
                    MembershipCoverage.Create(new DateOnly(2024, 1, 1), new DateOnly(2025, 1, 1)), null),
                new UniverseSummary(
                    UniverseCode.Create("VN30"), "VN30 Index", UniverseKind.Index,
                    MembershipCoverage.Create(new DateOnly(2026, 1, 2), null), 30),
                new UniverseSummary(
                    UniverseCode.Create("RAW"), "Unsourced", UniverseKind.Index, null, null),
            ]);

        var code = await harness.RunAsync("universe", "list");

        Assert.Equal(ExitCode.Ok, code);
        Assert.Contains("MEMBERS 2026-10-11", harness.Result, StringComparison.Ordinal);
        Assert.Contains("2024-01-01 → 2024-12-31", harness.Result, StringComparison.Ordinal);
        Assert.Contains("2026-01-02 →", harness.Result, StringComparison.Ordinal);
        Assert.Contains("none declared", harness.Result, StringComparison.Ordinal);
        Assert.Contains(Output.Unknown, harness.Result, StringComparison.Ordinal);
        Assert.Contains("30", harness.Result, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("universe", "breadth")]
    [InlineData("universe", "breadth", "VN30", "KHT")]
    [InlineData("universe", "breadth", "BAD!")]
    [InlineData("universe", "breadth", "VN30", "--on", "30/09/2026")]
    [InlineData("universe", "breadth", "VN30", "--from", "2026-09-30")]
    [InlineData("universe", "list", "VN30")]
    [InlineData("universe", "show", "VN30")]
    public async Task A_command_line_the_operator_got_wrong_never_reaches_the_deployment(
        params string[] args)
    {
        var harness = Harness.WithNothingConstructible();

        var code = await harness.RunAsync(args);

        Assert.Equal(ExitCode.Usage, code);
        Assert.NotEmpty(harness.Problems);
    }

    private static BreadthMember Member(
        string ticker,
        decimal? previous,
        decimal? close,
        BreadthCategory category,
        long? volume) =>
        new(
            InstrumentId.New(),
            Ticker.Create(ticker),
            ExchangeCode.Create("HOSE"),
            Previous,
            previous,
            close is null ? null : previous,
            close,
            close is { } c && previous is { } p ? (c / p) - 1m : null,
            volume,
            category);

    private sealed class FakeDirectory : IUniverseDirectory
    {
        public UniverseListing Answer { get; set; } = new(new DateOnly(2026, 10, 11), []);

        public Task<UniverseListing> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Answer);
    }

    private sealed class FakeBreadth : IMarketBreadthService
    {
        public BreadthResult Answer { get; set; } = BreadthResult.Refused(
            BreadthOutcome.UnknownUniverse, BreadthScope.Of(UniverseCode.Create("NONE")), null, "No such universe.");

        public (UniverseCode? Code, DateOnly? Session) Asked { get; private set; }

        public (ExchangeCode? Venue, DateOnly? Session) AskedVenue { get; private set; }

        public Task<BreadthResult> ComputeAsync(
            UniverseCode code,
            DateOnly? session,
            CancellationToken cancellationToken = default)
        {
            Asked = (code, session);
            return Task.FromResult(Answer);
        }

        public Task<BreadthResult> ComputeForVenueAsync(
            ExchangeCode venue,
            DateOnly? session,
            CancellationToken cancellationToken = default)
        {
            AskedVenue = (venue, session);
            return Task.FromResult(Answer);
        }
    }

    /// <summary>Wires the command class over in-memory services.</summary>
    private sealed class Harness
    {
        private readonly UniverseCommands _commands;
        private readonly RecordedOutput _output = new();

        public Harness()
            : this(constructible: true)
        {
        }

        private Harness(bool constructible)
        {
            _commands = constructible
                ? new UniverseCommands(
                    new Lazy<IUniverseDirectory>(() => Directory),
                    new Lazy<IMarketBreadthService>(() => Breadth),
                    _output.Output)
                : new UniverseCommands(
                    Unreachable.Service<IUniverseDirectory>(),
                    Unreachable.Service<IMarketBreadthService>(),
                    _output.Output);
        }

        public FakeDirectory Directory { get; } = new();

        public FakeBreadth Breadth { get; } = new();

        public string Result => _output.Result;

        public string Problems => _output.Problems;

        public static Harness WithNothingConstructible() => new(constructible: false);

        public async Task<int> RunAsync(params string[] args)
        {
            Assert.True(
                CommandArguments.TryParse(args, out var command, out var problem), problem);

            return await _commands.RunAsync(command, TestContext.Current.CancellationToken);
        }
    }
}
