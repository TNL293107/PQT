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
/// Sprint 11.4 acceptance (ADR-038 §1, §3, §4), written by QA + QC before the
/// code: how a vendor filing joins the IDS record it is allowed to fill.
/// </summary>
/// <remarks>
/// <para>
/// The IDS side is the importer as it is today: a fiscal-2026 non-bank form is
/// filed blank, so the report is recorded with <c>ValuesFiled = false</c> and no
/// lines (<see cref="FundamentalsImportServiceTests"/>). A vendor import then
/// names the vendor provider. The vendor filing is built in the shape the
/// Vietcap provider returns (one quarter, Vietcap's field names as codes), with
/// invented values.
/// </para>
/// <para>
/// Pins two names ADR-038 implies but does not give, both
/// <see cref="FilingRejectionReason"/>: <c>NoDisclosureRecord</c>, the finding
/// for a vendor period with no IDS report of that type, and <c>AsFiledHeld</c>,
/// for a vendor filing whose report already holds values filed on IDS.
/// </para>
/// </remarks>
public sealed class VendorJoinTests
{
    private static readonly DateTimeOffset IdsRead = new(2026, 5, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset VendorRead = new(2026, 11, 8, 3, 0, 0, TimeSpan.Zero);
    private static readonly SourceCode Ids = SourceCode.Create("IDS");
    private static readonly SourceCode Vcs = SourceCode.Create("VCS");

    // --- (b) the join ----------------------------------------------------------

    [Fact]
    public async Task A_vendor_quarter_fills_the_blank_ids_report_of_the_same_quarter()
    {
        var harness = new Harness();
        await harness.ImportIdsAsync(BlankQuarterly(2026, 1));

        var report = await harness.ImportVendorAsync(Vendor(2026, 1));

        var ids = Assert.Single(harness.Store.Reports);
        Assert.Empty(report.Rejections);
        // The form was blank, and stays recorded as blank.
        Assert.False(ids.ValuesFiled);
        Assert.NotEmpty(harness.Store.Lines);
        Assert.All(harness.Store.Lines, line =>
        {
            Assert.Equal(ids.Id, line.ReportId);
            Assert.Equal(ValueBasis.Vendor, line.Basis);
            Assert.Equal(Vcs, line.Source);
        });
    }

    [Fact]
    public async Task A_vendor_quarter_with_no_ids_report_is_a_finding_and_records_nothing()
    {
        var harness = new Harness();

        var report = await harness.ImportVendorAsync(Vendor(2026, 1));

        Assert.Equal(FilingRejectionReason.NoDisclosureRecord, Assert.Single(report.Rejections).Reason);
        Assert.Empty(harness.Store.Reports);
        Assert.Empty(harness.Store.Lines);
    }

    [Fact]
    public async Task A_vendor_quarter_is_never_attached_to_a_neighbouring_quarter()
    {
        // IDS holds Q1 only; a vendor Q2 must not fall onto it.
        var harness = new Harness();
        await harness.ImportIdsAsync(BlankQuarterly(2026, 1));

        var report = await harness.ImportVendorAsync(Vendor(2026, 2));

        Assert.Equal(FilingRejectionReason.NoDisclosureRecord, Assert.Single(report.Rejections).Reason);
        Assert.Empty(harness.Store.Lines);
    }

    [Fact]
    public async Task A_vendor_quarter_is_never_attached_to_the_half_year_of_the_same_months()
    {
        // IDS holds the H1 report only. Vietcap has no half-year row, and a Q2
        // is not an H1: a different period type, so a finding.
        var harness = new Harness();
        await harness.ImportIdsAsync(BlankHalfYear(2026));

        var report = await harness.ImportVendorAsync(Vendor(2026, 2));

        Assert.Equal(FilingRejectionReason.NoDisclosureRecord, Assert.Single(report.Rejections).Reason);
        Assert.Empty(harness.Store.Lines);
    }

    [Fact]
    public async Task A_vendor_quarter_never_attaches_to_a_separate_report()
    {
        // Vietcap is consolidated only; a separate IDS report is another scope.
        var harness = new Harness();
        await harness.ImportIdsAsync(BlankQuarterly(2026, 1, name: "Báo cáo tài chính Riêng - Quý"));

        var report = await harness.ImportVendorAsync(Vendor(2026, 1));

        Assert.Equal(FilingRejectionReason.NoDisclosureRecord, Assert.Single(report.Rejections).Reason);
        Assert.Empty(harness.Store.Lines);
    }

    // --- (c) when a vendor value is known ---------------------------------------

    [Fact]
    public async Task A_vendor_value_is_known_from_the_later_of_the_ids_date_and_the_fetch()
    {
        var harness = new Harness();
        await harness.ImportIdsAsync(BlankQuarterly(2026, 1));
        var ids = Assert.Single(harness.Store.Reports);

        await harness.ImportVendorAsync(Vendor(2026, 1));

        // The fetch (2026-11-08) is after the IDS filing (2026-04-28): the
        // values are known from the fetch, and never from Vietcap's publicDate.
        Assert.True(VendorRead > ids.KnownFromUtc);
        Assert.NotEmpty(harness.Store.Lines);
        Assert.All(harness.Store.Lines, line => Assert.Equal(VendorRead, line.ObservedFromUtc));
    }

    // --- (d) a vendor value never overwrites an as_filed one --------------------

    [Fact]
    public async Task A_vendor_filing_never_overwrites_a_report_filed_with_values()
    {
        // A filled IDS form (fiscal 2025): its as_filed values stand.
        var harness = new Harness();
        await harness.ImportIdsAsync(Filings.Quarterly(year: 2025, quarter: 1, ticker: "AAA"));
        var filed = harness.Store.Lines.Where(line => line.IsCurrent).ToList();
        Assert.NotEmpty(filed);

        var report = await harness.ImportVendorAsync(Vendor(2025, 1));

        // Refused for the right reason, not because the codes were unreadable:
        // the report already holds as_filed values, which a vendor never
        // overwrites (ADR-038 §4).
        Assert.Equal(FilingRejectionReason.AsFiledHeld, Assert.Single(report.Rejections).Reason);
        Assert.Equal(0, report.LinesRestated);
        Assert.All(harness.Store.Lines.Where(line => line.IsCurrent), line => Assert.Equal(ValueBasis.AsFiled, line.Basis));
        Assert.Equal(filed.Count, harness.Store.Lines.Count(line => line.IsCurrent));
    }

    private static ProviderFiling BlankQuarterly(int year, int quarter, string name = "Báo cáo tài chính Hợp nhất - Quý") =>
        Filings.Quarterly(
                year: year,
                quarter: quarter,
                name: name,
                ticker: "AAA",
                tables: [Filings.Balance(("270", "0", "0"), ("440", "0", "0"))]) with
        {
            SubmittedOn = new DateOnly(year, quarter * 3 + 1, 28),
        };

    private static ProviderFiling BlankHalfYear(int year) =>
        Filings.Quarterly(
                year: year,
                quarter: 2,
                name: "Báo cáo tài chính Hợp nhất bán niên",
                ticker: "AAA",
                reference: $"AAA-{year}H1",
                tables: [Filings.Balance(("270", "0", "0"), ("440", "0", "0"))]) with
        {
            SubmittedOn = new DateOnly(year, 8, 20),
        };

    /// <summary>A vendor quarter in the shape the Vietcap provider returns; values invented.</summary>
    private static ProviderFiling Vendor(int year, int quarter) =>
        new(
            $"VCS-AAA-{year}Q{quarter}",
            "AAA",
            "Báo cáo tài chính Hợp nhất - Quý",
            $"Vietcap quý {quarter} năm {year}",
            DateOnly.FromDateTime(VendorRead.UtcDateTime),
            VendorRead,
            year,
            quarter,
            [
                new ProviderStatementTable("INCOME_STATEMENT", ["value"],
                    [new ProviderStatementRow("Net sales", "isa3", ["12345678901230"]),
                     new ProviderStatementRow("Profit attributable to the parent", "isa22", ["1111111111111"])]),
                new ProviderStatementTable("BALANCE_SHEET", ["value"],
                    [new ProviderStatementRow("Total assets", "bsa53", ["73997673121789"]),
                     new ProviderStatementRow("Owner's equity", "bsa78", ["37896654696942"])]),
            ],
            "{\"invented\":true}",
            "application/json",
            VendorRead);

    private sealed class Harness
    {
        private readonly ScriptedProvider _ids = new(Ids, ValueBasis.AsFiled);
        private readonly ScriptedProvider _vendor = new(Vcs, ValueBasis.Vendor);

        public Harness()
        {
            var exchanges = new InMemoryExchanges();
            var venue = exchanges.Add("HOSE", IdsRead);
            var instrument = Instrument.Register(
                venue, Ticker.Create("AAA"), "AAA Corporation", AssetType.Equity, CurrencyCode.Vnd, IdsRead);

            Service = new FundamentalsImportService(
                [_ids, _vendor],
                new Resolver(instrument.Id),
                exchanges,
                Store,
                new FakeUnitOfWork(),
                Clock,
                NullLogger<FundamentalsImportService>.Instance);
        }

        public InMemoryFundamentals Store { get; } = new();

        public FakeClock Clock { get; } = new(IdsRead);

        public FundamentalsImportService Service { get; }

        public Task<FundamentalsImportReport> ImportIdsAsync(params ProviderFiling[] filings)
        {
            _ids.Filings = filings;
            Clock.UtcNow = IdsRead;
            return Service.ImportAsync(Ids, [], TestContext.Current.CancellationToken);
        }

        public Task<FundamentalsImportReport> ImportVendorAsync(params ProviderFiling[] filings)
        {
            _vendor.Filings = filings;
            Clock.UtcNow = VendorRead;
            return Service.ImportAsync(Vcs, [], TestContext.Current.CancellationToken);
        }
    }

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
