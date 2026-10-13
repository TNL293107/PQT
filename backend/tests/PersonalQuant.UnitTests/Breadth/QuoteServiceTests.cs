using PersonalQuant.Application.Exchanges;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Application.Quotes;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.UnitTests.Instruments.Fakes;
using PersonalQuant.UnitTests.MarketData.Fakes;
using static PersonalQuant.UnitTests.Breadth.QuoteValues;

namespace PersonalQuant.UnitTests.Breadth;

/// <summary>
/// Verifies the quote breadth counts and a watchlist shows: close against the
/// exchange's reference, on one session for the whole set.
/// </summary>
public sealed class QuoteServiceTests
{
    private static readonly DateOnly Tuesday = new(2026, 9, 29);
    private static readonly DateOnly Wednesday = new(2026, 9, 30);

    /// <summary>FPT's two entitlements of 27 May 2016, combined.</summary>
    private const decimal Fpt2016 = 0.8512585812m;

    [Fact]
    public async Task A_stock_dividend_reference_is_rounded_to_the_tick_and_a_close_at_it_is_unchanged()
    {
        // 47,500 restated is 40,434.78, a price HOSE cannot quote; it publishes
        // 40,450. A close there did not move, and unrounded it would read +0.04%.
        var harness = new Harness();
        var fpt = harness.Instrument("FPT");
        harness.Closes(fpt, (Tuesday, 47_500m), (Wednesday, 40_450m));
        harness.Reader.Add(Factor(fpt, Wednesday, Fpt2016));

        var quote = Assert.Single((await harness.QuotedAsync(Wednesday)).Quotes);

        Assert.Equal(47_500m, quote.RawPreviousClose);
        Assert.Equal(40_450m, quote.Reference);
        Assert.Equal(PriceMove.Unchanged, quote.Move);
        Assert.Equal(0m, quote.Change);
        Assert.True(quote.AdjustedForAction);
    }

    [Fact]
    public async Task HNX_rounds_a_reference_to_a_hundred_dong()
    {
        var harness = new Harness();
        var shs = harness.Instrument("SHS", harness.Venue("HNX"));
        harness.Closes(shs, (Tuesday, 15_000m), (Wednesday, 13_000m));
        harness.Reader.Add(Factor(shs, Wednesday, 1m / 1.15m));

        var quote = Assert.Single((await harness.QuotedAsync(Wednesday)).Quotes);

        // 15,000 / 1.15 = 13,043.48 → 13,000, where it closed.
        Assert.Equal(13_000m, quote.Reference);
        Assert.Equal(PriceMove.Unchanged, quote.Move);
    }

    [Theory]
    [InlineData(AssetType.Etf)]
    [InlineData(AssetType.Index)]
    public async Task Only_an_equity_s_reference_is_rounded(AssetType assetType)
    {
        // An ETF quotes on another step and an index is not quoted at all; an
        // exact restatement is a smaller error than a rounding nobody does.
        var harness = new Harness();
        var other = harness.Instrument("OTHER", assetType: assetType);
        harness.Closes(other, (Tuesday, 47_500m), (Wednesday, 40_450m));
        harness.Reader.Add(Factor(other, Wednesday, Fpt2016));

        var quote = Assert.Single((await harness.QuotedAsync(Wednesday)).Quotes);

        Assert.Equal(47_500m * Fpt2016, quote.Reference);
        Assert.Equal(PriceMove.Advancing, quote.Move);
    }

    [Fact]
    public async Task On_an_ordinary_day_the_reference_is_the_close_as_printed()
    {
        // An index level is not on any tick, and nothing restated it.
        var harness = new Harness();
        var index = harness.Instrument("VNINDEX", assetType: AssetType.Index);
        harness.Closes(index, (Tuesday, 1_683.27m), (Wednesday, 1_690.04m));

        var quote = Assert.Single((await harness.QuotedAsync(Wednesday)).Quotes);

        Assert.Equal(1_683.27m, quote.Reference);
        Assert.False(quote.AdjustedForAction);
    }

    [Fact]
    public async Task Without_a_session_the_latest_one_any_of_them_traded_is_quoted_for_all()
    {
        // One session for the set. The suspended name is no data that day, not
        // its last close from an older one shown beside today's.
        var harness = new Harness();
        var active = harness.Instrument("ACT");
        var suspended = harness.Instrument("SUS");
        harness.Closes(active, (Tuesday, 10_000m), (Wednesday, 10_100m));
        harness.Closes(suspended, (new DateOnly(2026, 9, 28), 20_000m), (Tuesday, 20_500m));

        var board = await harness.QuoteAsync(null);

        Assert.Equal(QuoteOutcome.Quoted, board.Outcome);
        Assert.Equal(Wednesday, board.Session);
        Assert.True(board.SessionWasDefaulted);
        Assert.Equal(PriceMove.Advancing, board.Quotes.Single(quote => quote.Ticker.Value == "ACT").Move);
        Assert.Equal(PriceMove.NoData, board.Quotes.Single(quote => quote.Ticker.Value == "SUS").Move);
    }

    [Fact]
    public async Task Without_a_session_and_without_recent_bars_the_answer_is_no_data()
    {
        var harness = new Harness();
        var stale = harness.Instrument("OLD");
        harness.Closes(stale, (new DateOnly(2026, 8, 3), 10_000m));

        var board = await harness.QuoteAsync(null);

        Assert.Equal(QuoteOutcome.NoData, board.Outcome);
        Assert.Empty(board.Quotes);
    }

    [Fact]
    public async Task Nothing_to_quote_is_answered_as_an_empty_board()
    {
        var board = await new Harness().QuoteAsync(null, []);

        Assert.Equal(QuoteOutcome.Quoted, board.Outcome);
        Assert.Null(board.Session);
        Assert.Empty(board.Quotes);
    }

    [Fact]
    public async Task A_day_the_venue_did_not_trade_is_refused()
    {
        var harness = new Harness();
        harness.Instrument("AAA");

        var board = await harness.QuoteAsync(new DateOnly(2026, 10, 3));

        Assert.Equal(QuoteOutcome.NotATradingDay, board.Outcome);
        Assert.Contains("HOSE", board.Detail, StringComparison.Ordinal);
    }

    /// <summary>One venue and the service over real calendar logic.</summary>
    private sealed class Harness
    {
        private static readonly DateTimeOffset Recorded = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        private static readonly DateTimeOffset Now = new(2026, 10, 3, 2, 0, 0, TimeSpan.Zero);

        private readonly InMemoryExchanges _exchanges = new();
        private readonly List<InstrumentId> _instruments = [];

        public Harness()
        {
            Hose = Venue("HOSE");
            Service = new QuoteService(
                new TradingCalendar(_exchanges), Reader, new MarketDataProviderRegistry([]), new FakeClock(Now));
        }

        public ExchangeId Hose { get; }

        public FakeQuoteReader Reader { get; } = new();

        public QuoteService Service { get; }

        public ExchangeId Venue(string code)
        {
            var id = _exchanges.Add(code, Recorded);
            _exchanges.DeclareCoverage(id, new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1));
            return id;
        }

        public InstrumentId Instrument(string ticker, ExchangeId? venue = null, AssetType assetType = AssetType.Equity)
        {
            var instrument = QuoteValues.Instrument(ticker, venue ?? Hose, assetType);
            Reader.Add(instrument);
            _instruments.Add(instrument.InstrumentId);
            return instrument.InstrumentId;
        }

        public void Closes(InstrumentId id, params (DateOnly Session, decimal Close)[] closes)
        {
            foreach (var (session, close) in closes)
            {
                Reader.Add(Close(id, session, close));
            }
        }

        public Task<QuoteBoard> QuoteAsync(DateOnly? session, IReadOnlyCollection<InstrumentId>? instruments = null) =>
            Service.QuoteAsync(instruments ?? _instruments, session, TestContext.Current.CancellationToken);

        public async Task<QuoteBoard> QuotedAsync(DateOnly session)
        {
            var board = await QuoteAsync(session);

            Assert.True(board.Outcome == QuoteOutcome.Quoted, board.Detail);
            return board;
        }
    }
}
