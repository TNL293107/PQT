using PersonalQuant.Application.Instruments;
using PersonalQuant.Domain.Currencies;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.UnitTests.Instruments.Fakes;
using PersonalQuant.UnitTests.MarketData.Fakes;

namespace PersonalQuant.UnitTests.Instruments;

/// <summary>
/// Verifies the deliberate act that moves a security to the venue it now
/// lists on.
/// </summary>
/// <remarks>
/// BSR moved from UPCOM to HOSE and joined VN30 there, while the master still
/// held it on UPCOM. A source that does not serve UPCOM then refused it, and the
/// venue's band judged its prices by the wrong rule. The import cannot correct
/// that on its own: the only thing linking the old listing to the new one is the
/// ticker, which is the weakest identity the master has.
/// </remarks>
public sealed class InstrumentTransferServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_transfer_moves_the_instrument_and_keeps_its_identity()
    {
        var harness = new Harness();
        var bsr = harness.List("BSR", harness.Upcom);

        // Act
        var result = await harness.TransferAsync(bsr.Id, "HOSE");

        // Assert
        Assert.Equal(InstrumentTransferOutcome.Transferred, result.Outcome);
        Assert.Equal("UPCOM", result.From?.Value);
        Assert.Equal(harness.Hose, bsr.ExchangeId);
        Assert.Equal(Now, bsr.UpdatedAtUtc);
        Assert.Equal(1, harness.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Transferring_to_the_venue_it_is_already_on_changes_nothing()
    {
        var harness = new Harness();
        var fpt = harness.List("FPT", harness.Hose);

        var result = await harness.TransferAsync(fpt.Id, "HOSE");

        Assert.Equal(InstrumentTransferOutcome.AlreadyThere, result.Outcome);
        Assert.Equal(0, harness.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task A_venue_the_system_does_not_hold_is_refused()
    {
        var harness = new Harness();
        var bsr = harness.List("BSR", harness.Upcom);

        var result = await harness.TransferAsync(bsr.Id, "NYSE");

        Assert.Equal(InstrumentTransferOutcome.UnknownExchange, result.Outcome);
        Assert.Equal(harness.Upcom, bsr.ExchangeId);
    }

    [Fact]
    public async Task A_ticker_another_security_holds_on_the_target_is_refused()
    {
        // Two active instruments under one ticker on one venue is exactly the
        // ambiguity the master exists to prevent. The operator has to say which
        // record is wrong; a transfer cannot decide it.
        var harness = new Harness();
        var upcom = harness.List("BSR", harness.Upcom);
        harness.List("BSR", harness.Hose);

        var result = await harness.TransferAsync(upcom.Id, "HOSE");

        Assert.Equal(InstrumentTransferOutcome.TickerTaken, result.Outcome);
        Assert.Equal(harness.Upcom, upcom.ExchangeId);
        Assert.Equal(0, harness.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task A_delisted_instrument_is_refused()
    {
        var harness = new Harness();
        var gone = harness.List("OLD", harness.Upcom);
        gone.Delist(new DateOnly(2026, 1, 5), Now);

        var result = await harness.TransferAsync(gone.Id, "HOSE");

        Assert.Equal(InstrumentTransferOutcome.Delisted, result.Outcome);
        Assert.Equal(harness.Upcom, gone.ExchangeId);
    }

    [Fact]
    public async Task An_unknown_instrument_is_refused()
    {
        var harness = new Harness();

        var result = await harness.TransferAsync(InstrumentId.New(), "HOSE");

        Assert.Equal(InstrumentTransferOutcome.UnknownInstrument, result.Outcome);
    }

    private sealed class Harness
    {
        private readonly InstrumentTransferService _service;
        private readonly InMemoryInstrumentMaster _master = new();

        public Harness()
        {
            var exchanges = new InMemoryExchanges();
            Hose = exchanges.Add("HOSE", Now.AddYears(-1), 7m);
            Upcom = exchanges.Add("UPCOM", Now.AddYears(-1), 15m);

            _service = new InstrumentTransferService(
                _master, exchanges, UnitOfWork, new FakeClock(Now));
        }

        public ExchangeId Hose { get; }

        public ExchangeId Upcom { get; }

        public FakeUnitOfWork UnitOfWork { get; } = new();

        public Instrument List(string ticker, ExchangeId venue)
        {
            var instrument = Instrument.Register(
                venue,
                Ticker.Create(ticker),
                $"{ticker} Company",
                AssetType.Equity,
                CurrencyCode.Vnd,
                Now.AddYears(-1));

            instrument.List(Now.AddYears(-1));
            _master.Seed(instrument);
            return instrument;
        }

        public Task<InstrumentTransfer> TransferAsync(InstrumentId id, string venue) =>
            _service.TransferAsync(
                id, ExchangeCode.Create(venue), TestContext.Current.CancellationToken);
    }
}
