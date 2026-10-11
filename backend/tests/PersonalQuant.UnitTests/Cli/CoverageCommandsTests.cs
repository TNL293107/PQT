using PersonalQuant.Application.Coverage;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Application.Universes;
using PersonalQuant.Cli.CommandLine;
using PersonalQuant.Cli.Commands;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.Domain.Universes;
using PersonalQuant.UnitTests.Cli.Fakes;

namespace PersonalQuant.UnitTests.Cli;

/// <summary>
/// Verifies <c>pqt coverage</c>: how a security is named, and that the four
/// states never print alike.
/// </summary>
/// <remarks>
/// The rendering is the part under test here; the state decisions have their
/// own tests against the application query. What this pins is that a terminal
/// reader can tell "PQT cannot say" from "PQT knows it holds none" without a
/// colour, and that an unmeasured count is never printed as zero.
/// </remarks>
public sealed class CoverageCommandsTests
{
    [Fact]
    public async Task Each_state_prints_its_own_mark_and_word()
    {
        var harness = new Harness();
        var instrument = harness.Instruments.Add("FPT");
        harness.Coverage.Holds(Sample(instrument.InstrumentId));

        var code = await harness.RunAsync("coverage", "FPT");

        Assert.Equal(ExitCode.Ok, code);
        Assert.Contains("✓ held", harness.Result, StringComparison.Ordinal);
        Assert.Contains("✗ not held", harness.Result, StringComparison.Ordinal);
        Assert.Contains("? not measured", harness.Result, StringComparison.Ordinal);
        Assert.Contains("— not collected", harness.Result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_universe_that_declared_nothing_is_never_printed_as_not_held()
    {
        var harness = new Harness();
        var instrument = harness.Instruments.Add("FPT");
        harness.Coverage.Holds(Sample(instrument.InstrumentId));

        await harness.RunAsync("coverage", "FPT");

        var unknown = Line(harness.Result, "Universe VNMID");
        var absent = Line(harness.Result, "Universe VN100");

        Assert.Contains("? not measured", unknown, StringComparison.Ordinal);
        Assert.DoesNotContain("not held", unknown, StringComparison.Ordinal);
        Assert.Contains("✗ not held", absent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_sessions_print_as_a_count_when_measured_and_as_unmeasured_otherwise()
    {
        var harness = new Harness();
        var instrument = harness.Instruments.Add("FPT");
        harness.Coverage.Holds(Sample(instrument.InstrumentId));

        await harness.RunAsync("coverage", "FPT");

        Assert.Contains("1 of 2,901 sessions missing", Line(harness.Result, "Price 1d"), StringComparison.Ordinal);
        Assert.Contains("daily series only", harness.Result, StringComparison.Ordinal);
        Assert.DoesNotContain("0 of", Line(harness.Result, "Price 1h"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_held_daily_series_prints_its_extent_sources_and_last_attempt()
    {
        var harness = new Harness();
        var instrument = harness.Instruments.Add("FPT");
        harness.Coverage.Holds(Sample(instrument.InstrumentId));

        await harness.RunAsync("coverage", "FPT");

        var daily = Line(harness.Result, "Price 1d");
        Assert.Contains("2015-01-02 → 2026-08-28", daily, StringComparison.Ordinal);
        Assert.Contains("2,900 bars", daily, StringComparison.Ordinal);
        Assert.Contains("VNX", daily, StringComparison.Ordinal);
        Assert.Contains("1 open finding", daily, StringComparison.Ordinal);
        Assert.Contains("last attempt 2026-08-29 VNX Succeeded", daily, StringComparison.Ordinal);
        Assert.Contains("Phase 6", Line(harness.Result, "Fundamentals"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_instrument_identifier_is_read_without_resolving_a_ticker()
    {
        var harness = new Harness();
        var id = InstrumentId.New();
        harness.Coverage.Holds(Sample(id));

        var code = await harness.RunAsync("coverage", id.Value.ToString());

        Assert.Equal(ExitCode.Ok, code);
        Assert.Equal(0, harness.Instruments.ResolveCount);
    }

    [Fact]
    public async Task An_unknown_ticker_is_refused_and_named()
    {
        var harness = new Harness();

        var code = await harness.RunAsync("coverage", "ZZZ");

        Assert.Equal(ExitCode.Refused, code);
        Assert.Contains("ZZZ", harness.Problems, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_identifier_is_refused()
    {
        var harness = new Harness();
        var id = Guid.CreateVersion7();

        var code = await harness.RunAsync("coverage", id.ToString());

        Assert.Equal(ExitCode.Refused, code);
        Assert.Contains(id.ToString(), harness.Problems, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("coverage", "Hoa", "Phat")]
    [InlineData("coverage", "FPT", "--interval", "1d")]
    [InlineData("coverage", "FPT", "--exchange", "!")]
    public async Task A_malformed_line_is_refused_before_anything_is_constructed(params string[] args)
    {
        var harness = Harness.WithNothingConstructible();

        var code = await harness.RunAsync(args);

        Assert.Equal(ExitCode.Usage, code);
        Assert.NotEmpty(harness.Problems);
    }

    private static string Line(string output, string dataset) =>
        output.Split('\n').Single(line => line.StartsWith(dataset, StringComparison.Ordinal));

    private static InstrumentCoverage Sample(InstrumentId id)
    {
        var vnx = SourceCode.Create("VNX");

        return new InstrumentCoverage(
            id,
            Ticker.Create("FPT"),
            "FPT Corporation",
            ExchangeCode.Create("HOSE"),
            [
                new PriceCoverage(
                    BarInterval.OneDay,
                    CoverageState.Held,
                    new DateOnly(2015, 1, 2),
                    new DateOnly(2026, 8, 28),
                    2_900,
                    [vnx],
                    1,
                    new IngestionAttempt(
                        BarInterval.OneDay,
                        vnx,
                        new DateTimeOffset(2026, 8, 29, 1, 0, 0, TimeSpan.Zero),
                        new DateTimeOffset(2026, 8, 29, 1, 1, 0, TimeSpan.Zero),
                        IngestionOutcome.Succeeded),
                    2_901,
                    1,
                    null),
                new PriceCoverage(
                    BarInterval.OneHour,
                    CoverageState.Held,
                    new DateOnly(2026, 8, 3),
                    new DateOnly(2026, 8, 7),
                    30,
                    [vnx],
                    0,
                    null,
                    null,
                    null,
                    "Missing sessions are measured for the daily series only."),
                new PriceCoverage(
                    BarInterval.FiveMinutes,
                    CoverageState.NotCollected,
                    null,
                    null,
                    0,
                    [],
                    0,
                    null,
                    null,
                    null,
                    "No registered source serves 5m bars for HOSE Equity."),
            ],
            new CorporateActionCoverage(
                CoverageState.NotHeld, 0, 0, 0, null, null, "No corporate action is recorded for this instrument."),
            [
                new UniverseMembershipCoverage(
                    UniverseCode.Create("VN100"),
                    "VN100 Index",
                    CoverageState.NotHeld,
                    new DateOnly(2024, 1, 1),
                    null,
                    [],
                    "Not a member at any time from 2024-01-01 onward, the window VN100 declares; outside it, unknown."),
                new UniverseMembershipCoverage(
                    UniverseCode.Create("VN30"),
                    "VN30 Index",
                    CoverageState.Held,
                    new DateOnly(2024, 1, 1),
                    null,
                    [new UniverseSpell(id, new DateOnly(2024, 1, 2), null, null)],
                    "VN30 claims to know its membership from 2024-01-01 onward."),
                new UniverseMembershipCoverage(
                    UniverseCode.Create("VNMID"),
                    "VNMID Index",
                    CoverageState.NotMeasured,
                    null,
                    null,
                    [],
                    "VNMID declares no coverage, so the absence of a spell says nothing about membership."),
            ],
            new DatasetCoverage(CoverageState.NotCollected, "Phase 6"));
    }

    /// <summary>Answers with whatever coverage a test declared, keyed by instrument.</summary>
    private sealed class FakeCoverageService : IInstrumentCoverageService
    {
        private readonly Dictionary<InstrumentId, InstrumentCoverage> _coverage = [];

        public void Holds(InstrumentCoverage coverage) => _coverage[coverage.InstrumentId] = coverage;

        public Task<InstrumentCoverage?> DescribeAsync(
            InstrumentId instrumentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_coverage.GetValueOrDefault(instrumentId));
    }

    private sealed class Harness
    {
        private readonly CoverageCommands _commands;
        private readonly RecordedOutput _output = new();

        public Harness()
            : this(constructible: true)
        {
        }

        private Harness(bool constructible)
        {
            _commands = constructible
                ? new CoverageCommands(
                    new Lazy<IInstrumentCoverageService>(() => Coverage),
                    new Lazy<IInstrumentResolver>(() => Instruments),
                    _output.Output)
                : new CoverageCommands(
                    Unreachable.Service<IInstrumentCoverageService>(),
                    Unreachable.Service<IInstrumentResolver>(),
                    _output.Output);
        }

        public FakeInstrumentResolver Instruments { get; } = new();

        public FakeCoverageService Coverage { get; } = new();

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
