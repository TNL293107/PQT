using PersonalQuant.Application.Instruments;
using PersonalQuant.Application.Terminal;
using PersonalQuant.Cli.CommandLine;
using PersonalQuant.Cli.Commands;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.UnitTests.Cli.Fakes;

namespace PersonalQuant.UnitTests.Cli;

/// <summary>Verifies the operator's watchlists from the shell.</summary>
public sealed class WatchlistCommandsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Tickers_are_stored_as_the_instruments_they_name()
    {
        var harness = new Harness();
        var fpt = harness.Instruments.Add("FPT");
        var vcb = harness.Instruments.Add("VCB");

        var code = await harness.RunAsync("watchlist", "add", "core", "fpt", "VCB");

        Assert.Equal(ExitCode.Ok, code);
        Assert.Equal([fpt.InstrumentId, vcb.InstrumentId], harness.Watchlists.Added);
        Assert.Contains("FPT: added to 'core'", harness.Result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_ticker_changes_nothing_even_after_a_known_one()
    {
        // Resolved first, written after: half a command must not take effect.
        var harness = new Harness();
        harness.Instruments.Add("FPT");

        var code = await harness.RunAsync("watchlist", "add", "core", "FPT", "NOPE");

        Assert.Equal(ExitCode.Refused, code);
        Assert.Empty(harness.Watchlists.Added);
        Assert.Contains("nothing was changed", harness.Problems, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refusal_from_the_service_is_reported_with_a_non_zero_exit()
    {
        var harness = new Harness();
        harness.Watchlists.Refuse(WatchlistOutcome.NameTaken, "A watchlist named 'core' already exists.");

        var code = await harness.RunAsync("watchlist", "create", "core");

        Assert.Equal(ExitCode.Refused, code);
        Assert.Contains("already exists", harness.Problems, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("add", "core")]
    [InlineData("rename", "core")]
    [InlineData("show")]
    [InlineData("frobnicate")]
    public async Task A_command_line_missing_its_operands_is_a_usage_error(params string[] rest)
    {
        var harness = new Harness();

        var code = await harness.RunAsync(["watchlist", .. rest]);

        Assert.Equal(ExitCode.Usage, code);
        Assert.Empty(harness.Watchlists.Added);
    }

    [Fact]
    public async Task Showing_a_list_prints_its_instruments_in_order()
    {
        var harness = new Harness();
        harness.Watchlists.Hold(new WatchlistView("core", Now, Now,
        [
            new WatchlistItem(InstrumentId.New(), "FPT", "FPT Corporation", "HOSE", InstrumentStatus.Listed, 0, Now),
            new WatchlistItem(InstrumentId.New(), "VCB", "Vietcombank", "HOSE", InstrumentStatus.Suspended, 1, Now),
        ]));

        var code = await harness.RunAsync("watchlist", "show", "core");

        Assert.Equal(ExitCode.Ok, code);
        Assert.True(
            harness.Result.IndexOf("FPT", StringComparison.Ordinal) < harness.Result.IndexOf("VCB", StringComparison.Ordinal));
        Assert.Contains("Suspended", harness.Result, StringComparison.Ordinal);
    }

    private sealed class Harness
    {
        private readonly WatchlistCommands _commands;
        private readonly RecordedOutput _output = new();

        public Harness() =>
            _commands = new WatchlistCommands(
                new Lazy<IWatchlistService>(() => Watchlists),
                new Lazy<IInstrumentResolver>(() => Instruments),
                _output.Output);

        public FakeInstrumentResolver Instruments { get; } = new();

        public FakeWatchlistService Watchlists { get; } = new();

        public string Result => _output.Result;

        public string Problems => _output.Problems;

        public async Task<int> RunAsync(params string[] args)
        {
            Assert.True(CommandArguments.TryParse(args, out var command, out var problem), problem);

            return await _commands.RunAsync(command, TestContext.Current.CancellationToken);
        }
    }

    private sealed class FakeWatchlistService : IWatchlistService
    {
        private WatchlistView _held = new("core", Now, Now, []);
        private WatchlistChange? _refusal;

        public List<InstrumentId> Added { get; } = [];

        public void Hold(WatchlistView view) => _held = view;

        public void Refuse(WatchlistOutcome outcome, string message) =>
            _refusal = new WatchlistChange(outcome, message, null);

        public Task<IReadOnlyList<WatchlistSummary>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WatchlistSummary>>([new WatchlistSummary(_held.Name, _held.Items.Count, Now, Now)]);

        public Task<WatchlistView?> FindAsync(string name, CancellationToken cancellationToken = default) =>
            Task.FromResult<WatchlistView?>(name == _held.Name ? _held : null);

        public Task<WatchlistChange> CreateAsync(string name, CancellationToken cancellationToken = default) =>
            Task.FromResult(_refusal ?? Changed());

        public Task<WatchlistChange> RenameAsync(string name, string newName, CancellationToken cancellationToken = default) =>
            Task.FromResult(_refusal ?? Changed());

        public Task<WatchlistChange> DeleteAsync(string name, CancellationToken cancellationToken = default) =>
            Task.FromResult(_refusal ?? Changed());

        public Task<WatchlistChange> AddAsync(string name, InstrumentId instrumentId, CancellationToken cancellationToken = default)
        {
            if (_refusal is not null)
            {
                return Task.FromResult(_refusal);
            }

            Added.Add(instrumentId);

            return Task.FromResult(Changed());
        }

        public Task<WatchlistChange> RemoveAsync(string name, InstrumentId instrumentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_refusal ?? Changed());

        private WatchlistChange Changed() => new(WatchlistOutcome.Changed, null, _held);
    }
}
