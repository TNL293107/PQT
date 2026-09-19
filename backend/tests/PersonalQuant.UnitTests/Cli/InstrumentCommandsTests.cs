using PersonalQuant.Application.Instruments;
using PersonalQuant.Cli.CommandLine;
using PersonalQuant.Cli.Commands;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.UnitTests.Cli.Fakes;

namespace PersonalQuant.UnitTests.Cli;

/// <summary>
/// Verifies the surface a venue transfer is stated on.
/// </summary>
public sealed class InstrumentCommandsTests
{
    [Fact]
    public async Task A_transfer_names_both_venues_and_succeeds()
    {
        var harness = new Harness();
        var bsr = harness.Instruments.Add("BSR", "UPCOM");
        harness.Transfers.Answer = InstrumentTransferOutcome.Transferred;

        var code = await harness.RunAsync("instrument", "transfer", "--instrument", "BSR", "--to", "HOSE");

        Assert.Equal(ExitCode.Ok, code);
        Assert.Equal((bsr.InstrumentId, "HOSE"), Assert.Single(harness.Transfers.Calls));
        Assert.Contains("from UPCOM to HOSE", harness.Result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_ticker_taken_on_the_target_is_refused()
    {
        var harness = new Harness();
        harness.Instruments.Add("BSR", "UPCOM");
        harness.Transfers.Answer = InstrumentTransferOutcome.TickerTaken;

        var code = await harness.RunAsync("instrument", "transfer", "--instrument", "BSR", "--to", "HOSE");

        Assert.Equal(ExitCode.Refused, code);
        Assert.Contains("decide which is wrong", harness.Problems, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("instrument", "transfer", "--instrument", "BSR")]
    [InlineData("instrument", "transfer", "--to", "HOSE")]
    [InlineData("instrument", "transfer", "--instrument", "BSR", "--to", "HO SE")]
    [InlineData("instrument", "rename", "--instrument", "BSR")]
    public async Task A_command_line_the_operator_got_wrong_never_reaches_the_deployment(
        params string[] args)
    {
        var harness = Harness.WithNothingConstructible();

        var code = await harness.RunAsync(args);

        Assert.Equal(ExitCode.Usage, code);
        Assert.NotEmpty(harness.Problems);
    }

    private sealed class FakeTransfers : IInstrumentTransferService
    {
        public InstrumentTransferOutcome Answer { get; set; }

        public List<(InstrumentId Instrument, string To)> Calls { get; } = [];

        public Task<InstrumentTransfer> TransferAsync(
            InstrumentId instrumentId,
            ExchangeCode destination,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((instrumentId, destination.Value));

            return Task.FromResult(new InstrumentTransfer(Answer, ExchangeCode.Create("UPCOM"), destination));
        }
    }

    private sealed class Harness
    {
        private readonly InstrumentCommands _commands;
        private readonly RecordedOutput _output = new();

        public Harness()
            : this(constructible: true)
        {
        }

        private Harness(bool constructible)
        {
            _commands = constructible
                ? new InstrumentCommands(
                    new Lazy<IInstrumentTransferService>(() => Transfers),
                    new Lazy<IInstrumentResolver>(() => Instruments),
                    _output.Output)
                : new InstrumentCommands(
                    Unreachable.Service<IInstrumentTransferService>(),
                    Unreachable.Service<IInstrumentResolver>(),
                    _output.Output);
        }

        public FakeInstrumentResolver Instruments { get; } = new();

        public FakeTransfers Transfers { get; } = new();

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
