using PersonalQuant.Application.Fundamentals;
using PersonalQuant.Domain.CorporateActions;
using PersonalQuant.Domain.Fundamentals;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.UnitTests.CorporateActions.Fakes;
using PersonalQuant.UnitTests.MarketData.Fakes;

namespace PersonalQuant.UnitTests.Fundamentals;

/// <summary>
/// Sprint 11.4 acceptance (ADR-038 §3, §4; QA verify of 7fe41ab, finding M
/// (a)/(b)), written by QA + QC before the fix: a filed reading supersedes a
/// vendor line only where it states the same line, not the whole period.
/// </summary>
/// <remarks>
/// <para>
/// 7a0155c drops every vendor line of a period as soon as any report filed
/// with values for that period is known (<c>FundamentalsReader</c>,
/// <c>filed</c> keyed by <c>report.Period</c>). Two things are wrong with
/// that. First, a half-year or annual report states income as
/// <see cref="PeriodSpan.YearToDate"/>, not the quarter alone. Dropping the
/// vendor's Quarter profit then leaves the quarter with no value, and the
/// trailing year silently falls back one quarter. Second, the next year's
/// comparative column states the period again as filed, but under the next
/// year's report period, so the vendor line is never dropped for it.
/// </para>
/// <para>
/// What the fix must deliver: a vendor line is superseded where a known filed
/// report states the same statement, period and span. How it is keyed beyond
/// that (for example by canonical item) is Dev's choice; these tests pin the
/// outcome. Every figure is invented.
/// </para>
/// </remarks>
public sealed class VendorLineKeySupersessionTests
{
    private const decimal Bn = 1_000_000_000m;
    private static readonly SourceCode Ids = SourceCode.Create("IDS");
    private static readonly SourceCode Vcs = SourceCode.Create("VCS");
    private static readonly FiscalQuarter Q1Of2026 = FiscalQuarter.Create(2026, 1);
    private static readonly FiscalQuarter Q2Of2026 = FiscalQuarter.Create(2026, 2);
    private static readonly FiscalQuarter Q4Of2026 = FiscalQuarter.Create(2026, 4);

    // --- (a) a half-year report filed with values --------------------------------

    [Fact]
    public async Task A_filed_half_year_keeps_the_vendor_quarter_profit_in_the_trailing_year()
    {
        // 2025Q3 + 2025Q4 (C200, 250bn each) + 2026Q1 (vendor 400bn) + 2026Q2
        // (vendor 300bn). The H1 states 700bn year to date, which is not Q2
        // alone, so it does not replace the vendor's Q2 quarter.
        var harness = HalfYearHarness();

        var ratios = await harness.RatiosAsync(new DateOnly(2026, 9, 1));

        Assert.Equal(
            [FiscalQuarter.Create(2025, 3), FiscalQuarter.Create(2025, 4), Q1Of2026, Q2Of2026],
            ratios.ProfitQuarters);
        Assert.Equal(1_200m * Bn, ratios.NetProfitTtm.Value);
    }

    [Fact]
    public async Task A_filed_half_year_supersedes_the_vendor_balance_sheet_of_its_quarter_end()
    {
        // The H1's balance sheet is at Q2's end, the same instant the vendor
        // stated: there the filed reading stands, and equity is 12,000bn, not
        // the vendor's 11,500bn.
        var harness = HalfYearHarness();

        var ratios = await harness.RatiosAsync(new DateOnly(2026, 9, 1));
        var served = await harness.ServedAsync(new DateOnly(2026, 9, 1));

        Assert.Equal(Q2Of2026, ratios.BalanceSheet);
        Assert.Equal(120_000m, ratios.BookValuePerShare.Value);
        Assert.DoesNotContain(served, value =>
            value.Period == Q2Of2026 && value.Statement == FinancialStatement.BalanceSheet && value.Basis == ValueBasis.Vendor);
        Assert.Contains(served, value =>
            value.Period == Q2Of2026 && value.Span == PeriodSpan.Quarter && value.LineCode == "isa22" && value.Basis == ValueBasis.Vendor);
    }

    // --- (a) an annual report filed with values ----------------------------------

    [Fact]
    public async Task A_filed_annual_report_keeps_the_vendor_fourth_quarter_in_the_trailing_year()
    {
        // Vendor 2026Q1..Q4 = 400, 300, 200, 100bn; the audited annual (Period
        // Q4) states 1,000bn for the year. The trailing year is 2026Q1..Q4.
        var harness = new Harness();
        harness.Circular200Year(2025);

        foreach (var (quarter, profit) in new[] { (1, 400m), (2, 300m), (3, 200m), (4, 100m) })
        {
            harness.VendorQuarter(FiscalQuarter.Create(2026, quarter), profit * Bn, 11_000m * Bn);
        }

        var annual = harness.Filed(Q4Of2026, new DateOnly(2027, 3, 20), FinancialReportKind.AnnualAudited);
        harness.Line(annual, FinancialStatement.IncomeStatement, "61", Q4Of2026, PeriodSpan.YearToDate, 1_000m * Bn);
        harness.Line(annual, FinancialStatement.BalanceSheet, "400", Q4Of2026, PeriodSpan.Instant, 13_000m * Bn);
        harness.Line(annual, FinancialStatement.BalanceSheet, "411a", Q4Of2026, PeriodSpan.Instant, 1_000m * Bn);

        var ratios = await harness.RatiosAsync(new DateOnly(2027, 4, 1));

        Assert.Equal(
            [Q1Of2026, Q2Of2026, FiscalQuarter.Create(2026, 3), Q4Of2026],
            ratios.ProfitQuarters);
        Assert.Equal(1_000m * Bn, ratios.NetProfitTtm.Value);
        Assert.Equal(Q4Of2026, ratios.BalanceSheet);
    }

    // --- (b) the next year's comparative -------------------------------------------

    [Fact]
    public async Task The_next_years_comparative_supersedes_the_vendor_line_in_the_served_set()
    {
        // 2027Q1 is filed with values and its prior-year column restates
        // 2026Q1's profit as 420bn. From then on 2026Q1's quarter profit is
        // served once, as filed; the vendor's 400bn is not served beside it.
        var harness = ComparativeHarness();

        var served = await harness.ServedAsync(new DateOnly(2027, 5, 2));

        var q1Profit = served
            .Where(value => value.Period == Q1Of2026 && value.Statement == FinancialStatement.IncomeStatement && value.Span == PeriodSpan.Quarter)
            .ToList();
        var single = Assert.Single(q1Profit);
        Assert.Equal((ValueBasis.AsFiled, 420m * Bn), (single.Basis, single.Value));
    }

    [Fact]
    public async Task Before_the_comparative_is_filed_the_vendor_line_still_stands()
    {
        // Guard: the supersession is point-in-time. On 20 April 2027 the 2027Q1
        // form (filed 28 April) is not known, so 2026Q1 is the vendor's.
        var harness = ComparativeHarness();

        var served = await harness.ServedAsync(new DateOnly(2027, 4, 20));

        var single = Assert.Single(served, value =>
            value.Period == Q1Of2026 && value.Statement == FinancialStatement.IncomeStatement && value.Span == PeriodSpan.Quarter);
        Assert.Equal((ValueBasis.Vendor, 400m * Bn), (single.Basis, single.Value));
    }

    [Fact]
    public async Task A_row_whose_only_vendor_value_was_superseded_by_a_comparative_is_not_built_on_a_vendor()
    {
        // The row reads 2027Q1's balance sheet and capital, 2026Q2..2027Q1's
        // profit (all filed), and 2026Q1 as the year-earlier quarter. 2026Q1's
        // only vendor value is the profit the comparative superseded, so the
        // row is not counted in vendor_rows.
        var harness = ComparativeHarness();

        var ratios = await harness.RatiosAsync(new DateOnly(2027, 5, 2));

        Assert.Equal(FiscalQuarter.Create(2027, 1), ratios.BalanceSheet);
        Assert.Equal(
            [Q2Of2026, FiscalQuarter.Create(2026, 3), Q4Of2026, FiscalQuarter.Create(2027, 1)],
            ratios.ProfitQuarters);
        Assert.False(ratios.BuiltOnVendor);
    }

    /// <summary>C200 2025; vendor 2026Q1 and Q2; an H1 filed on 20 August 2026 with values.</summary>
    private static Harness HalfYearHarness()
    {
        var harness = new Harness();
        harness.Circular200Year(2025);
        harness.VendorQuarter(Q1Of2026, 400m * Bn, 11_000m * Bn);
        harness.VendorQuarter(Q2Of2026, 300m * Bn, 11_500m * Bn);

        var h1 = harness.Filed(Q2Of2026, new DateOnly(2026, 8, 20), FinancialReportKind.SemiAnnualReviewed);
        harness.Line(h1, FinancialStatement.IncomeStatement, "61", Q2Of2026, PeriodSpan.YearToDate, 700m * Bn);
        harness.Line(h1, FinancialStatement.BalanceSheet, "400", Q2Of2026, PeriodSpan.Instant, 12_000m * Bn);
        harness.Line(h1, FinancialStatement.BalanceSheet, "411a", Q2Of2026, PeriodSpan.Instant, 1_000m * Bn);
        return harness;
    }

    /// <summary>
    /// C200 2025; a vendor 2026Q1 profit only (400bn); 2026Q2..Q4 filed with
    /// values; 2027Q1 filed on 28 April 2027 with a comparative 2026Q1 of 420bn.
    /// </summary>
    private static Harness ComparativeHarness()
    {
        var harness = new Harness();
        harness.Circular200Year(2025);
        harness.VendorQuarter(Q1Of2026, 400m * Bn, equity: null);

        for (var quarter = 2; quarter <= 4; quarter++)
        {
            var period = FiscalQuarter.Create(2026, quarter);
            var report = harness.Filed(period, new DateOnly(2026, quarter * 3, 1).AddMonths(1).AddDays(24), FinancialReportKind.Quarterly);
            harness.Line(report, FinancialStatement.IncomeStatement, "61", period, PeriodSpan.Quarter, 300m * Bn);
            harness.Line(report, FinancialStatement.BalanceSheet, "400", period, PeriodSpan.Instant, 12_000m * Bn);
            harness.Line(report, FinancialStatement.BalanceSheet, "411a", period, PeriodSpan.Instant, 1_000m * Bn);
        }

        var q1Of2027 = FiscalQuarter.Create(2027, 1);
        var next = harness.Filed(q1Of2027, new DateOnly(2027, 4, 28), FinancialReportKind.Quarterly);
        harness.Line(next, FinancialStatement.IncomeStatement, "61", q1Of2027, PeriodSpan.Quarter, 450m * Bn);
        harness.Line(next, FinancialStatement.IncomeStatement, "61", Q1Of2026, PeriodSpan.Quarter, 420m * Bn);
        harness.Line(next, FinancialStatement.BalanceSheet, "400", q1Of2027, PeriodSpan.Instant, 13_000m * Bn);
        harness.Line(next, FinancialStatement.BalanceSheet, "411a", q1Of2027, PeriodSpan.Instant, 1_000m * Bn);
        return harness;
    }

    private sealed class Harness
    {
        private static readonly DateTimeOffset Fetch = new(2026, 8, 10, 3, 0, 0, TimeSpan.Zero);

        private readonly InstrumentId _instrument = InstrumentId.New();
        private readonly InMemoryFundamentals _fundamentals = new();
        private readonly FakeBarRepository _bars = new();
        private readonly FakeCorporateActionCoverageRepository _coverage = new();

        public Harness() =>
            _coverage.Add(CorporateActionCoverage.Record(
                _instrument, new DateOnly(2024, 1, 1), new DateOnly(2027, 12, 1), "VSDC notices", Fetch));

        public void Circular200Year(int year)
        {
            for (var quarter = 1; quarter <= 4; quarter++)
            {
                var period = FiscalQuarter.Create(year, quarter);
                var report = Filed(period, new DateOnly(year, quarter * 3, 1).AddMonths(1).AddDays(24), FinancialReportKind.Quarterly);
                Line(report, FinancialStatement.IncomeStatement, "61", period, PeriodSpan.Quarter, 250m * Bn);
                Line(report, FinancialStatement.BalanceSheet, "411a", period, PeriodSpan.Instant, 1_000m * Bn);
                Line(report, FinancialStatement.BalanceSheet, "400", period, PeriodSpan.Instant, 10_000m * Bn);
            }
        }

        /// <summary>A blank fiscal-2026 report filled by a vendor fetch on 10 August 2026.</summary>
        public void VendorQuarter(FiscalQuarter period, decimal profit, decimal? equity)
        {
            var blank = Record(period, new DateOnly(period.Year, period.Quarter * 3, 1).AddMonths(1).AddDays(27), FinancialReportKind.Quarterly,
                FinancialTemplate.NonFinancialCircular99, valuesFiled: false);
            var fetched = blank.RecordedAtUtc > Fetch ? blank.RecordedAtUtc.AddDays(1) : Fetch;
            _fundamentals.Add(StatementLine.Observe(
                blank, FinancialStatement.IncomeStatement, "isa22", period, PeriodSpan.Quarter, profit, ValueBasis.Vendor, Vcs, fetched));

            if (equity is { } total)
            {
                _fundamentals.Add(StatementLine.Observe(
                    blank, FinancialStatement.BalanceSheet, "bsa78", period, PeriodSpan.Instant, total, ValueBasis.Vendor, Vcs, fetched));
            }
        }

        public FinancialReport Filed(FiscalQuarter period, DateOnly disclosedOn, FinancialReportKind kind) =>
            Record(period, disclosedOn, kind, FinancialTemplate.NonFinancialCircular200, valuesFiled: true);

        public void Line(FinancialReport report, FinancialStatement statement, string code, FiscalQuarter period, PeriodSpan span, decimal value) =>
            _fundamentals.Add(StatementLine.Observe(report, statement, code, period, span, value, ValueBasis.AsFiled, Ids, report.RecordedAtUtc));

        public async Task<FundamentalRatios> RatiosAsync(DateOnly session)
        {
            Close(session);
            return await new FundamentalRatioService(
                    new FundamentalsReader(_fundamentals), _bars, new FakeCorporateActionRepository(), _coverage)
                .ReadAsync(_instrument, session, null, TestContext.Current.CancellationToken);
        }

        public async Task<IReadOnlyList<FundamentalValue>> ServedAsync(DateOnly session)
        {
            var read = await new FundamentalsReader(_fundamentals).ReadAsync(
                _instrument, FinancialStatementScope.Consolidated, new DateTimeOffset(session.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                null, TestContext.Current.CancellationToken);
            return read.Values;
        }

        private FinancialReport Record(FiscalQuarter period, DateOnly disclosedOn, FinancialReportKind kind, FinancialTemplate template, bool valuesFiled)
        {
            var report = FinancialReport.Record(
                _instrument, Ids, $"ids-{Guid.NewGuid():N}", kind, FinancialStatementScope.Consolidated,
                template, period, "Báo cáo tài chính hợp nhất", disclosedOn, null,
                FinancialReportTests.Vietnam, isCorrection: false, valuesFiled,
                new DateTimeOffset(disclosedOn.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero));
            _fundamentals.Add(report);
            return report;
        }

        private void Close(DateOnly session)
        {
            var at = new DateTimeOffset(session.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var bar = OhlcvBar.Record(
                _instrument, BarInterval.OneDay, at,
                Price.Create(60_000m), Price.Create(60_000m), Price.Create(60_000m), Price.Create(60_000m), 1_000, null, Ids, at.AddHours(9));
            _bars.AddRange([bar]);
            _bars.Revisions.Add(BarRevision.Snapshot(bar, at.AddHours(9)));
        }
    }
}
