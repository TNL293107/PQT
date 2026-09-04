using PersonalQuant.Application.Instruments;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Cli.CommandLine;
using PersonalQuant.Cli.Commands;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.UnitTests.Cli.Fakes;
using PersonalQuant.UnitTests.MarketData.Fakes;

namespace PersonalQuant.UnitTests.Cli;

/// <summary>
/// Verifies what the provider group renders, and what it refuses to need.
/// </summary>
public sealed class ProviderCommandsTests
{
    private static readonly SourceCode File = SourceCode.Create("FILE");
    private static readonly SourceCode Vendor = SourceCode.Create("VCI");

    [Fact]
    public async Task Listing_with_no_source_registered_says_so_rather_than_printing_nothing()
    {
        // A deployment pointed at no source ingests nothing and records skipped
        // runs saying so. An empty table would look like a rendering failure.
        var harness = new Harness();

        var code = await harness.RunAsync("provider", "list");

        Assert.Equal(ExitCode.Ok, code);
        Assert.Contains("No market data source is registered.", harness.Result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Listing_never_constructs_the_instrument_repository()
    {
        // The regression this exists for: resolving the repository eagerly
        // validates the database options, so asking a host what sources it
        // holds failed on a host with no database configured — which is exactly
        // when the question is worth asking.
        var harness = new Harness(Provider(File, adjustsAtSource: false));

        var code = await harness.RunAsync("provider", "list");

        Assert.Equal(ExitCode.Ok, code);
        Assert.Equal(0, harness.Instruments.ResolveCount);
    }

    [Fact]
    public async Task An_unstated_coverage_floor_renders_as_unknown_and_never_as_unbounded()
    {
        var harness = new Harness(Provider(File, adjustsAtSource: false));

        await harness.RunAsync("provider", "list");

        Assert.Contains("unknown", harness.Result, StringComparison.Ordinal);
        Assert.DoesNotContain("unbounded", harness.Result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_unrestricted_venue_set_renders_as_any_rather_than_as_blank()
    {
        // Empty is a claim: a directory of CSV files genuinely has no venue
        // restriction. Rendering it blank would make it indistinguishable from
        // a vendor that never said, which is the collapse the capability record
        // exists to prevent.
        var harness = new Harness(Provider(File, adjustsAtSource: false));

        await harness.RunAsync("provider", "list");

        Assert.Contains("any", harness.Result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Showing_a_source_adjusted_provider_states_what_that_means()
    {
        var harness = new Harness(Provider(Vendor, adjustsAtSource: true));

        var code = await harness.RunAsync("provider", "show", "VCI");

        Assert.Equal(ExitCode.Ok, code);
        Assert.Contains("Adjusted at source        yes", harness.Result, StringComparison.Ordinal);
        Assert.Contains("already adjusted", harness.Result, StringComparison.Ordinal);
        Assert.Equal(0, harness.Instruments.ResolveCount);
    }

    [Fact]
    public async Task Showing_an_unregistered_code_names_what_is_registered()
    {
        var harness = new Harness(
            Provider(File, adjustsAtSource: false),
            Provider(Vendor, adjustsAtSource: true));

        var code = await harness.RunAsync("provider", "show", "NOPE");

        Assert.Equal(ExitCode.Refused, code);
        Assert.Contains("FILE", harness.Problems, StringComparison.Ordinal);
        Assert.Contains("VCI", harness.Problems, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Checking_a_source_that_cannot_serve_the_resolution_names_the_dimension()
    {
        var daily = Provider(
            Vendor,
            adjustsAtSource: true,
            intervals: new HashSet<BarInterval> { BarInterval.OneDay });

        var harness = new Harness(daily);
        harness.Instruments.Add("FPT");

        var code = await harness.RunAsync(
            "provider", "check", "VCI", "--instrument", "FPT", "--interval", "1m");

        Assert.Equal(ExitCode.Refused, code);
        Assert.Contains("Incapable", harness.Result, StringComparison.Ordinal);
        Assert.Contains("OneMinute", harness.Result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Checking_a_source_that_can_serve_it_reports_the_selection()
    {
        var harness = new Harness(Provider(Vendor, adjustsAtSource: true));
        harness.Instruments.Add("FPT");

        var code = await harness.RunAsync(
            "provider", "check", "VCI", "--instrument", "FPT", "--interval", "1d");

        Assert.Equal(ExitCode.Ok, code);
        Assert.Contains("Selected", harness.Result, StringComparison.Ordinal);
        Assert.Contains("FPT on HOSE", harness.Result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Checking_a_source_with_no_stated_floor_says_the_reach_is_unknown()
    {
        // Not that the range is fine. A source that never declared how far back
        // it holds cannot promise 2015, and saying nothing would let the
        // operator read silence as confirmation.
        var harness = new Harness(Provider(Vendor, adjustsAtSource: true));
        harness.Instruments.Add("FPT");

        await harness.RunAsync(
            "provider", "check", "VCI", "--instrument", "FPT", "--from", "2015-01-01");

        Assert.Contains("unknown", harness.Result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Checking_an_unlisted_ticker_is_refused_before_any_selection()
    {
        var harness = new Harness(Provider(Vendor, adjustsAtSource: true));

        var code = await harness.RunAsync(
            "provider", "check", "VCI", "--instrument", "NOSUCH");

        Assert.Equal(ExitCode.Refused, code);
        Assert.Contains("NOSUCH", harness.Problems, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_verb_the_group_does_not_have_is_a_usage_error()
    {
        var harness = new Harness();

        var code = await harness.RunAsync("provider", "describe");

        Assert.Equal(ExitCode.Usage, code);
        Assert.Contains("list, show or check", harness.Problems, StringComparison.Ordinal);
    }

    private static ScriptedProvider Provider(
        SourceCode code,
        bool adjustsAtSource,
        IReadOnlySet<BarInterval>? intervals = null,
        VolumeBasis volumeBasis = VolumeBasis.Unspecified) =>
        new(code, _ => throw new NotSupportedException("The provider group calls no source."))
        {
            Capability = TestCapability.For(
                code,
                intervals,
                exchanges: code == SourceCode.Create("VCI")
                    ? new HashSet<ExchangeCode> { ExchangeCode.Create("HOSE") }
                    : null,
                adjustsPricesAtSource: adjustsAtSource,
                volumeBasis: volumeBasis),
        };

    [Fact]
    public async Task Showing_a_source_states_which_trades_its_volume_counts()
    {
        // Vietnamese venues run two books. A volume that counts only continuous
        // matching understates traded size by however much went through as a
        // negotiated block, and the number looks identical either way — so a
        // liquidity screen built on it means something different depending on a
        // fact nothing was previously recording.
        var harness = new Harness(
            Provider(Vendor, adjustsAtSource: true, volumeBasis: VolumeBasis.MatchedOrders));

        var code = await harness.RunAsync("provider", "show", "VCI");

        Assert.Equal(ExitCode.Ok, code);
        Assert.Contains("matched orders only", harness.Result, StringComparison.Ordinal);
        Assert.Contains("excludes negotiated blocks", harness.Result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_source_that_never_stated_a_volume_basis_renders_as_unknown()
    {
        // Silence is not a claim that everything is counted. A directory of CSV
        // files exported by somebody else genuinely does not know, and reading
        // that as "all trades" is how an unstated basis becomes an assumed one.
        var harness = new Harness(Provider(File, adjustsAtSource: false));

        await harness.RunAsync("provider", "show", "FILE");

        Assert.Contains("Volume counts", harness.Result, StringComparison.Ordinal);
        Assert.Contains("unknown", harness.Result, StringComparison.Ordinal);
    }

    /// <summary>
    /// Wires the real registry and command class over a fake resolver.
    /// </summary>
    private sealed class Harness
    {
        private readonly ProviderCommands _commands;
        private readonly RecordedOutput _output = new();

        public Harness(params ScriptedProvider[] providers)
        {
            Instruments = new FakeInstrumentResolver();

            _commands = new ProviderCommands(
                new MarketDataProviderRegistry(providers),
                new Lazy<IInstrumentResolver>(() => Instruments),
                _output.Output);
        }

        public FakeInstrumentResolver Instruments { get; }

        public string Result => _output.Result;

        public string Problems => _output.Problems;

        public async Task<int> RunAsync(params string[] args)
        {
            Assert.True(
                CommandArguments.TryParse(args, out var command, out var problem), problem);

            return await _commands.RunAsync(command, TestContext.Current.CancellationToken);
        }
    }
}
