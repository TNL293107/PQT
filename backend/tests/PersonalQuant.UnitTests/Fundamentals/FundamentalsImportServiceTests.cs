using Microsoft.Extensions.Logging.Abstractions;
using PersonalQuant.Application.Fundamentals;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Domain.Currencies;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Fundamentals;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.UnitTests.Instruments.Fakes;
using PersonalQuant.UnitTests.MarketData.Fakes;

namespace PersonalQuant.UnitTests.Fundamentals;

/// <summary>
/// Verifies that an import records a filing once, with its values and its
/// payload, reads it again without duplicating anything, supersedes a changed
/// value, and refuses what it cannot place.
/// </summary>
public sealed class FundamentalsImportServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);
    private static readonly SourceCode Ids = SourceCode.Create("IDS");

    [Fact]
    public async Task A_new_filing_is_recorded_with_its_values_its_date_and_its_payload()
    {
        var harness = new Harness(Filings.Quarterly());

        var report = await harness.ImportAsync();

        var recorded = Assert.Single(harness.Store.Reports);
        Assert.Equal((1, 0, 0), (report.ReportsRecorded, report.ReportsWithoutValues, report.Rejections.Count));
        Assert.Equal(new DateTimeOffset(2022, 4, 22, 17, 0, 0, TimeSpan.Zero), recorded.KnownFromUtc);
        Assert.Equal(report.LinesRecorded, harness.Store.Lines.Count);
        Assert.Equal(8, harness.Store.Lines.Count);
        Assert.All(harness.Store.Lines, line => Assert.Equal(ValueBasis.AsFiled, line.Basis));
        Assert.Single(harness.Store.Payloads);
    }

    [Fact]
    public async Task Reading_the_same_filing_again_records_nothing_new()
    {
        var harness = new Harness(Filings.Quarterly());
        await harness.ImportAsync();

        var again = await harness.ImportAsync();

        Assert.Equal((0, 1, 0, 0), (again.ReportsRecorded, again.ReportsAlreadyHeld, again.LinesRecorded, again.LinesRestated));
        Assert.Single(harness.Store.Reports);
        Assert.Single(harness.Store.Payloads);
    }

    [Fact]
    public async Task A_changed_value_supersedes_its_reading_and_keeps_the_old_one()
    {
        var harness = new Harness(Filings.Quarterly());
        await harness.ImportAsync();

        harness.Supply(Filings.Quarterly(tables: Filings.NonFinancialTables(revenue: "1,001")) with { RawPayload = "{\"changed\":true}" });
        var again = await harness.ImportAsync();

        var readings = harness.Store.Lines
            .Where(line => line.LineCode == "10" && line.Span == PeriodSpan.Quarter && line.Period.Year == 2022)
            .OrderBy(line => line.Revision)
            .Select(line => (line.Value, line.IsCurrent))
            .ToList();

        Assert.Equal(1, again.LinesRestated);
        Assert.Equal([(1_000m, false), (1_001m, true)], readings);
        Assert.Equal(2, harness.Store.Payloads.Count);
    }

    [Fact]
    public async Task An_empty_form_is_recorded_as_disclosed_without_values()
    {
        var empty = new[] { Filings.Balance(("270", "0", "0"), ("440", "0", "0")) };
        var harness = new Harness(Filings.Quarterly(year: 2026, tables: empty));

        var report = await harness.ImportAsync();

        var recorded = Assert.Single(harness.Store.Reports);
        Assert.Equal((0, 1), (report.ReportsRecorded, report.ReportsWithoutValues));
        Assert.False(recorded.ValuesFiled);
        Assert.Empty(harness.Store.Lines);
    }

    [Fact]
    public async Task Values_appearing_on_a_report_recorded_without_any_are_refused()
    {
        var empty = new[] { Filings.Balance(("270", "0", "0"), ("440", "0", "0")) };
        var harness = new Harness(Filings.Quarterly(year: 2026, tables: empty));
        await harness.ImportAsync();

        harness.Supply(Filings.Quarterly(year: 2026));
        var again = await harness.ImportAsync();

        Assert.Equal(FilingRejectionReason.ValuesAppeared, Assert.Single(again.Rejections).Reason);
        Assert.Empty(harness.Store.Lines);
    }

    [Fact]
    public async Task A_refused_filing_leaves_nothing_behind_and_the_rest_are_recorded()
    {
        var harness = new Harness(
            Filings.Annual(2022, new DateOnly(2023, 1, 30), "hợp nhất năm 2022"),
            Filings.Quarterly(reference: "FPT-good"));

        var report = await harness.ImportAsync();

        Assert.Equal(FilingRejectionReason.ImplausibleDate, Assert.Single(report.Rejections).Reason);
        Assert.Equal("FPT-good", Assert.Single(harness.Store.Reports).SourceReference);
        Assert.Single(harness.Store.Payloads);
    }

    [Fact]
    public async Task An_unknown_ticker_is_refused_and_the_ticker_filter_is_honoured()
    {
        var harness = new Harness(Filings.Quarterly(ticker: "NOPE"), Filings.Quarterly(ticker: "FPT"));

        var filtered = await harness.ImportAsync("NOPE");

        Assert.Equal(1, filtered.FilingsRead);
        Assert.Equal(FilingRejectionReason.UnknownInstrument, Assert.Single(filtered.Rejections).Reason);
        Assert.Empty(harness.Store.Reports);
    }

    [Fact]
    public async Task An_import_names_its_provider_and_reads_no_other() =>
        await Assert.ThrowsAsync<ArgumentException>(
            () => new Harness().Service.ImportAsync(SourceCode.Create("VCI"), [], TestContext.Current.CancellationToken));

    private sealed class Harness
    {
        private readonly ScriptedProvider _provider = new();

        public Harness(params ProviderFiling[] filings)
        {
            var exchanges = new InMemoryExchanges();
            var venue = exchanges.Add("HOSE", Now);
            var instrument = Instrument.Register(
                venue, Ticker.Create("FPT"), "FPT Corporation", AssetType.Equity, CurrencyCode.Vnd, Now);

            _provider.Filings = filings;
            Service = new FundamentalsImportService(
                [_provider],
                new FptResolver(instrument.Id),
                exchanges,
                Store,
                new FakeUnitOfWork(),
                Clock,
                NullLogger<FundamentalsImportService>.Instance);
        }

        public InMemoryFundamentals Store { get; } = new();

        public FakeClock Clock { get; } = new(Now);

        public FundamentalsImportService Service { get; }

        /// <summary>Supplies other filings, read a day later.</summary>
        public void Supply(params ProviderFiling[] filings)
        {
            _provider.Filings = filings;
            Clock.UtcNow = Clock.UtcNow.AddDays(1);
        }

        public Task<FundamentalsImportReport> ImportAsync(params string[] tickers) =>
            Service.ImportAsync(Ids, tickers, TestContext.Current.CancellationToken);
    }

    private sealed class ScriptedProvider : IFundamentalsProvider
    {
        public IReadOnlyList<ProviderFiling> Filings { get; set; } = [];

        public SourceCode Code => Ids;

        public ValueBasis Basis => ValueBasis.AsFiled;

        public Task<IReadOnlyList<ProviderFiling>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Filings);
    }

    private sealed class FptResolver(InstrumentId fpt) : IInstrumentResolver
    {
        public Task<InstrumentResolution> ResolveAsync(
            string? symbol,
            ExchangeCode? exchange = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(symbol == "FPT"
                ? InstrumentResolution.Resolved(symbol, new InstrumentSearchResult(
                    fpt, Ticker.Create("FPT"), "FPT Corporation", AssetType.Equity, ExchangeCode.Create("HOSE"),
                    CurrencyCode.Vnd, InstrumentStatus.Listed, null))
                : InstrumentResolution.NotFound(symbol ?? string.Empty));

        public Task<InstrumentSearchResult?> FindByIdAsync(InstrumentId id, CancellationToken cancellationToken = default) =>
            Task.FromResult<InstrumentSearchResult?>(null);
    }
}
