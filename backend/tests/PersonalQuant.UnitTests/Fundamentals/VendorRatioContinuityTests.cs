using PersonalQuant.Application.Fundamentals;
using PersonalQuant.Domain.CorporateActions;
using PersonalQuant.Domain.Fundamentals;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.UnitTests.CorporateActions.Fakes;
using PersonalQuant.UnitTests.MarketData.Fakes;

namespace PersonalQuant.UnitTests.Fundamentals;

/// <summary>
/// Sprint 11.4 acceptance (ADR-038, review finding H1), written by QA + QC
/// before the fix: a fiscal-2026 vendor quarter must <em>extend</em> an
/// issuer's Circular 200 history, not replace it.
/// </summary>
/// <remarks>
/// <para>
/// ADR-038 exists because <c>size</c>, <c>value_ep</c>, <c>value_bp</c> and
/// <c>quality_roe</c> go stale for every non-bank from fiscal 2026. A vendor
/// value that makes them <em>absent</em> instead is worse than the gap it
/// fills. Today <see cref="StatementLines"/> reads one template only, that of
/// the latest balance sheet. Once 2026Q1 arrives on
/// <see cref="FinancialTemplate.NonFinancialCircular99"/>, the 2025 Circular
/// 200 quarters are dropped, so the trailing year has one quarter. The C99
/// form has no charter capital, so the share count is missing too.
/// </para>
/// <para>
/// The invented issuer files 2025Q1–Q4 on Circular 200, with ₫250bn of profit
/// to the parent a quarter, ₫1,000bn of charter capital (100m shares at par),
/// and equity to the parent of ₫7,000bn at 2025Q1 and ₫9,000bn at 2025Q4. Its
/// 2026Q1 IDS form is filed blank on 28 April 2026. A vendor read on 10 May
/// supplies ₫400bn of profit and equity of ₫11,000bn less ₫1,000bn of
/// minorities. It has no share capital and no corporate action. The close on
/// 1 June 2026 is ₫60,000.
/// </para>
/// <para>
/// What the fix must deliver: profit continuity across the change of form
/// (2025Q2–Q4 from C200, 2026Q1 from the vendor), the 2026Q1 vendor balance
/// sheet as the latest, and the share count carried from the latest charter
/// capital stated (2025Q4, C200), since the C99 lines state none. How that is
/// built is Dev's choice; these tests pin only the ratios.
/// </para>
/// </remarks>
public sealed class VendorRatioContinuityTests
{
    private static readonly SourceCode Ids = SourceCode.Create("IDS");
    private static readonly SourceCode Vcs = SourceCode.Create("VCS");
    private static readonly DateTimeOffset VendorFetch = new(2026, 5, 10, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly June = new(2026, 6, 1);
    private static readonly FiscalQuarter Q1Of2026 = FiscalQuarter.Create(2026, 1);

    [Fact]
    public async Task A_vendor_quarter_extends_the_circular_200_trailing_year()
    {
        var ratios = await new Harness().ReadAsync(June);

        Assert.Equal(
            [FiscalQuarter.Create(2025, 2), FiscalQuarter.Create(2025, 3), FiscalQuarter.Create(2025, 4), Q1Of2026],
            ratios.ProfitQuarters);
        Assert.Equal(1_150_000_000_000m, ratios.NetProfitTtm.Value);
    }

    [Fact]
    public async Task The_share_count_is_carried_from_the_latest_charter_capital_stated()
    {
        // The vendor states no charter capital (ADR-038 §5), and no action
        // changed the count: 2025Q4's 1,000bn at par is 100m shares.
        var ratios = await new Harness().ReadAsync(June);

        Assert.Equal(100_000_000m, ratios.Shares.Value);
        Assert.Equal(11_500m, ratios.EpsTtm.Value);
        Assert.Equal(5.2174m, decimal.Round(ratios.PriceToEarnings.Value!.Value, 4));
    }

    [Fact]
    public async Task The_vendor_balance_sheet_is_the_latest_and_prices_the_book()
    {
        // Equity to the parent bsa78 - bsa210 = 10,000bn, over 100m shares.
        // Falling back to 2025Q4's C200 sheet would give 90,000 and pass for
        // a fix, so the quarter is pinned too.
        var ratios = await new Harness().ReadAsync(June);

        Assert.Equal(Q1Of2026, ratios.BalanceSheet);
        Assert.Equal(100_000m, ratios.BookValuePerShare.Value);
        Assert.Equal(0.6m, ratios.PriceToBook.Value);
    }

    [Fact]
    public async Task Return_on_equity_spans_the_change_of_form()
    {
        // Closing equity 10,000bn (2026Q1, vendor), opening 7,000bn (2025Q1,
        // C200): 1,150 / 8,500.
        var ratios = await new Harness().ReadAsync(June);

        Assert.True(ratios.ReturnOnEquity.Value is not null, $"ROE absent: {ratios.ReturnOnEquity.Absent}");
        Assert.Equal(0.1353m, decimal.Round(ratios.ReturnOnEquity.Value!.Value, 4));
    }

    [Fact]
    public async Task Before_the_vendor_fetch_the_ratios_stay_on_2025_and_are_known()
    {
        // Guard, green today: after the blank IDS filing and before the fetch,
        // the blank report contributes nothing, and the 2025 ratios stand.
        var harness = new Harness();
        harness.Close(new DateOnly(2026, 5, 5), 60_000m);

        var ratios = await harness.ReadAsync(new DateOnly(2026, 5, 5));

        Assert.Equal(FiscalQuarter.Create(2025, 4), ratios.BalanceSheet);
        Assert.Equal(1_000_000_000_000m, ratios.NetProfitTtm.Value);
        Assert.Equal(100_000_000m, ratios.Shares.Value);
        Assert.Equal(90_000m, ratios.BookValuePerShare.Value);
    }

    private sealed class Harness
    {
        private readonly InstrumentId _instrument = InstrumentId.New();
        private readonly InMemoryFundamentals _fundamentals = new();
        private readonly FakeBarRepository _bars = new();
        private readonly FakeCorporateActionRepository _actions = new();
        private readonly FakeCorporateActionCoverageRepository _coverage = new();

        public Harness()
        {
            for (var quarter = 1; quarter <= 4; quarter++)
            {
                var period = FiscalQuarter.Create(2025, quarter);
                var report = Report(period, new DateOnly(2025, quarter * 3, 1).AddMonths(1).AddDays(24), FinancialTemplate.NonFinancialCircular200, valuesFiled: true);
                Filed(report, FinancialStatement.IncomeStatement, "61", period, PeriodSpan.Quarter, 250_000_000_000m);

                if (quarter is 1 or 4)
                {
                    Filed(report, FinancialStatement.BalanceSheet, "411a", period, PeriodSpan.Instant, 1_000_000_000_000m);
                    Filed(report, FinancialStatement.BalanceSheet, "400", period, PeriodSpan.Instant, quarter == 1 ? 8_000_000_000_000m : 10_000_000_000_000m);
                    Filed(report, FinancialStatement.BalanceSheet, "429", period, PeriodSpan.Instant, 1_000_000_000_000m);
                }
            }

            var blank = Report(Q1Of2026, new DateOnly(2026, 4, 28), FinancialTemplate.NonFinancialCircular99, valuesFiled: false);
            Vendor(blank, FinancialStatement.IncomeStatement, "isa22", PeriodSpan.Quarter, 400_000_000_000m);
            Vendor(blank, FinancialStatement.BalanceSheet, "bsa53", PeriodSpan.Instant, 30_000_000_000_000m);
            Vendor(blank, FinancialStatement.BalanceSheet, "bsa78", PeriodSpan.Instant, 11_000_000_000_000m);
            Vendor(blank, FinancialStatement.BalanceSheet, "bsa210", PeriodSpan.Instant, 1_000_000_000_000m);

            _coverage.Add(CorporateActionCoverage.Record(
                _instrument, new DateOnly(2024, 1, 1), new DateOnly(2026, 11, 1), "VSDC notices", VendorFetch));
            Close(June, 60_000m);
        }

        public void Close(DateOnly session, decimal close)
        {
            var at = new DateTimeOffset(session.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var bar = OhlcvBar.Record(
                _instrument, BarInterval.OneDay, at,
                Price.Create(close), Price.Create(close), Price.Create(close), Price.Create(close), 1_000, null, Ids, at.AddHours(9));
            _bars.AddRange([bar]);
            _bars.Revisions.Add(BarRevision.Snapshot(bar, at.AddHours(9)));
        }

        public Task<FundamentalRatios> ReadAsync(DateOnly session) =>
            new FundamentalRatioService(new FundamentalsReader(_fundamentals), _bars, _actions, _coverage)
                .ReadAsync(_instrument, session, null, TestContext.Current.CancellationToken);

        private FinancialReport Report(FiscalQuarter period, DateOnly disclosedOn, FinancialTemplate template, bool valuesFiled)
        {
            var report = FinancialReport.Record(
                _instrument, Ids, $"ids-{Guid.NewGuid():N}", FinancialReportKind.Quarterly, FinancialStatementScope.Consolidated,
                template, period, "Báo cáo tài chính hợp nhất", disclosedOn, null,
                FinancialReportTests.Vietnam, isCorrection: false, valuesFiled,
                new DateTimeOffset(disclosedOn.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero));
            _fundamentals.Add(report);
            return report;
        }

        private void Filed(FinancialReport report, FinancialStatement statement, string code, FiscalQuarter period, PeriodSpan span, decimal value) =>
            _fundamentals.Add(StatementLine.Observe(report, statement, code, period, span, value, ValueBasis.AsFiled, Ids, report.RecordedAtUtc));

        private void Vendor(FinancialReport report, FinancialStatement statement, string code, PeriodSpan span, decimal value) =>
            _fundamentals.Add(StatementLine.Observe(report, statement, code, Q1Of2026, span, value, ValueBasis.Vendor, Vcs, VendorFetch));
    }
}
