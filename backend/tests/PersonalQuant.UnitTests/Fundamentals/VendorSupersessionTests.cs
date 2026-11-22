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
/// Sprint 11.4 acceptance (ADR-038 §4, review finding H2), written by QA + QC
/// before the fix: when IDS later files values for a period a vendor filled,
/// the as_filed reading supersedes the vendor one through the real import.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="VendorReadTests.An_as_filed_reading_arriving_later_supersedes_the_vendor_line"/>
/// proves the domain primitive by calling <c>Restate</c> on the vendor line
/// itself. The importer never does that. A fiscal-2026 IDS form filed
/// <em>with</em> values is classified as a new report on the Circular 200
/// form (<c>StatementNormaliser.TemplateOf</c>). The vendor lines stay on
/// the blank Circular 99 report, and the reader serves both, because its
/// key includes the template.
/// </para>
/// <para>
/// These tests drive the three steps through <see cref="FundamentalsImportService"/>:
/// a blank IDS form, then a vendor join, then an IDS correction carrying
/// values. They pin only what a read serves, not how the fix gets there
/// (restating the vendor lines, or the reader dropping a blank report's
/// vendor lines once a filed report of the same period is known). Values are
/// invented.
/// </para>
/// </remarks>
public sealed class VendorSupersessionTests
{
    private static readonly DateTimeOffset IdsRead = new(2026, 5, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset VendorRead = new(2026, 11, 8, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset CorrectionRead = new(2026, 11, 21, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset AfterCorrection = new(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly SourceCode Ids = SourceCode.Create("IDS");
    private static readonly SourceCode Vcs = SourceCode.Create("VCS");
    private static readonly FiscalQuarter Q1 = FiscalQuarter.Create(2026, 1);

    [Fact]
    public async Task Before_the_correction_the_vendor_values_are_served()
    {
        // Sanity, green today: the join worked, so the later tests are about
        // supersession and not about a vendor value that never arrived.
        var harness = await Harness.ArrangeAsync(withCorrection: false);

        var read = await harness.ReadAsync(AfterCorrection);

        Assert.NotEmpty(read.Values);
        Assert.All(read.Values.Where(value => value.Period == Q1), value => Assert.Equal(ValueBasis.Vendor, value.Basis));
    }

    [Fact]
    public async Task After_an_ids_correction_with_values_no_vendor_value_of_that_period_is_served()
    {
        var harness = await Harness.ArrangeAsync(withCorrection: true);

        var read = await harness.ReadAsync(AfterCorrection);

        var period = read.Values.Where(value => value.Period == Q1).ToList();
        Assert.NotEmpty(period);
        Assert.DoesNotContain(period, value => value.Basis == ValueBasis.Vendor);
        Assert.All(period, value => Assert.Equal(ValueBasis.AsFiled, value.Basis));
    }

    [Fact]
    public async Task After_the_correction_the_period_is_read_on_one_form_only()
    {
        // Two templates for one period is what lets StatementLines pick a
        // form by input order. After the correction, exactly one is served.
        var harness = await Harness.ArrangeAsync(withCorrection: true);

        var read = await harness.ReadAsync(AfterCorrection);

        var templates = read.Values.Where(value => value.Period == Q1).Select(value => value.Template).Distinct().ToList();
        Assert.Equal(FinancialTemplate.NonFinancialCircular200, Assert.Single(templates));
    }

    [Fact]
    public async Task Between_the_vendor_fetch_and_the_correction_the_vendor_values_still_stand()
    {
        // The supersession is point-in-time: a read known before the
        // correction was filed (20 November) still sees the vendor values.
        var harness = await Harness.ArrangeAsync(withCorrection: true);

        var read = await harness.ReadAsync(new DateTimeOffset(2026, 11, 15, 0, 0, 0, TimeSpan.Zero));

        var period = read.Values.Where(value => value.Period == Q1).ToList();
        Assert.NotEmpty(period);
        Assert.All(period, value => Assert.Equal(ValueBasis.Vendor, value.Basis));
    }

    private sealed class Harness
    {
        private readonly ScriptedProvider _ids = new(Ids, ValueBasis.AsFiled);
        private readonly ScriptedProvider _vendor = new(Vcs, ValueBasis.Vendor);
        private readonly InstrumentId _instrument;
        private readonly FakeClock _clock = new(IdsRead);
        private readonly FundamentalsImportService _service;

        private Harness()
        {
            var exchanges = new InMemoryExchanges();
            var venue = exchanges.Add("HOSE", IdsRead);
            var instrument = Instrument.Register(
                venue, Ticker.Create("AAA"), "AAA Corporation", AssetType.Equity, CurrencyCode.Vnd, IdsRead);
            _instrument = instrument.Id;

            _service = new FundamentalsImportService(
                [_ids, _vendor],
                new Resolver(instrument.Id),
                exchanges,
                Store,
                new FakeUnitOfWork(),
                _clock,
                NullLogger<FundamentalsImportService>.Instance);
        }

        public InMemoryFundamentals Store { get; } = new();

        public static async Task<Harness> ArrangeAsync(bool withCorrection)
        {
            var harness = new Harness();

            var blank = await harness.ImportAsync(harness._ids, Ids, IdsRead, BlankQuarterly());
            Assert.Empty(blank.Rejections);

            var joined = await harness.ImportAsync(harness._vendor, Vcs, VendorRead, VendorQuarter());
            Assert.Empty(joined.Rejections);

            if (withCorrection)
            {
                var corrected = await harness.ImportAsync(harness._ids, Ids, CorrectionRead, FilledCorrection());
                Assert.Empty(corrected.Rejections);
            }

            return harness;
        }

        public Task<FundamentalsAsOf> ReadAsync(DateTimeOffset knownAsOf) =>
            new FundamentalsReader(Store).ReadAsync(
                _instrument, FinancialStatementScope.Consolidated, knownAsOf, null, TestContext.Current.CancellationToken);

        private Task<FundamentalsImportReport> ImportAsync(
            ScriptedProvider provider, SourceCode source, DateTimeOffset at, params ProviderFiling[] filings)
        {
            provider.Filings = filings;
            _clock.UtcNow = at;
            return _service.ImportAsync(source, [], TestContext.Current.CancellationToken);
        }
    }

    private static ProviderFiling BlankQuarterly() =>
        Filings.Quarterly(
                year: 2026,
                quarter: 1,
                ticker: "AAA",
                tables: [Filings.Balance(("270", "0", "0"), ("440", "0", "0"))]) with
        {
            SubmittedOn = new DateOnly(2026, 4, 28),
        };

    /// <summary>The same quarter re-filed on IDS with its Circular 200 form filled (invented values).</summary>
    private static ProviderFiling FilledCorrection() =>
        Filings.Quarterly(
                year: 2026,
                quarter: 1,
                name: "Báo cáo tài chính Hợp nhất - Quý (đính chính)",
                ticker: "AAA",
                reference: "AAA-2026Q1-correction") with
        {
            SubmittedOn = new DateOnly(2026, 11, 20),
        };

    /// <summary>A vendor quarter in the shape the Vietcap provider returns; values invented.</summary>
    private static ProviderFiling VendorQuarter() =>
        new(
            "VCS-AAA-2026Q1",
            "AAA",
            "Báo cáo tài chính Hợp nhất - Quý",
            "Vietcap quý 1 năm 2026",
            DateOnly.FromDateTime(VendorRead.UtcDateTime),
            VendorRead,
            2026,
            1,
            [
                new ProviderStatementTable("INCOME_STATEMENT", ["value"],
                    [new ProviderStatementRow("Net sales", "isa3", ["12345678901230"]),
                     new ProviderStatementRow("Profit attributable to the parent", "isa22", ["1111111111111"])]),
                new ProviderStatementTable("BALANCE_SHEET", ["value"],
                    [new ProviderStatementRow("Total assets", "bsa53", ["74000000000000"]),
                     new ProviderStatementRow("Owner's equity", "bsa78", ["38000000000000"])]),
            ],
            "{\"invented\":true}",
            "application/json",
            VendorRead);

    private sealed class ScriptedProvider(SourceCode code, ValueBasis basis) : IFundamentalsProvider
    {
        public IReadOnlyList<ProviderFiling> Filings { get; set; } = [];

        public SourceCode Code => code;

        public ValueBasis Basis => basis;

        public Task<IReadOnlyList<ProviderFiling>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Filings);
    }

    private sealed class Resolver(InstrumentId id) : IInstrumentResolver
    {
        public Task<InstrumentResolution> ResolveAsync(
            string? symbol,
            ExchangeCode? exchange = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(symbol == "AAA"
                ? InstrumentResolution.Resolved(symbol, new InstrumentSearchResult(
                    id, Ticker.Create("AAA"), "AAA Corporation", AssetType.Equity, ExchangeCode.Create("HOSE"),
                    CurrencyCode.Vnd, InstrumentStatus.Listed, null))
                : InstrumentResolution.NotFound(symbol ?? string.Empty));

        public Task<InstrumentSearchResult?> FindByIdAsync(InstrumentId id, CancellationToken cancellationToken = default) =>
            Task.FromResult<InstrumentSearchResult?>(null);
    }
}
