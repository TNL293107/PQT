using PersonalQuant.Application.Fundamentals;
using PersonalQuant.Domain.CorporateActions;
using PersonalQuant.Domain.Fundamentals;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.UnitTests.CorporateActions.Fakes;
using PersonalQuant.UnitTests.MarketData.Fakes;

namespace PersonalQuant.UnitTests.Fundamentals;

/// <summary>
/// Verifies the point-in-time ratios of ADR-030 on an invented issuer.
/// </summary>
/// <remarks>
/// <para>The issuer reports each quarter of 2022 on the Circular 200 form:</para>
/// <list type="bullet">
/// <item><description>profit to the parent of ₫250bn a quarter;</description></item>
/// <item><description>₫1,000bn of charter capital at the end of 2022, which is 100m shares at par;</description></item>
/// <item><description>equity of ₫10,000bn less ₫1,000bn of minorities at the end of 2022, and ₫8,000bn less ₫1,000bn a year earlier.</description></item>
/// </list>
/// <para>
/// A 20% bonus issue goes ex on 15 February 2023, announced on 1 February. A
/// ₫1,000 cash dividend went ex on 3 October 2022. The close on 1 March 2023
/// is ₫60,000.
/// </para>
/// </remarks>
public sealed class FundamentalRatioServiceTests
{
    private static readonly DateTimeOffset Recorded = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);
    private static readonly SourceCode Ids = SourceCode.Create("IDS");
    private static readonly DateOnly March = new(2023, 3, 1);

    [Fact]
    public async Task Every_ratio_uses_the_share_count_adjusted_to_the_session()
    {
        var harness = new Harness();

        var ratios = await harness.ReadAsync(March);

        Assert.Equal(120_000_000m, ratios.Shares.Value);
        Assert.Equal(1_000_000_000_000m, ratios.NetProfitTtm.Value);
        Assert.Equal(8_333.33m, decimal.Round(ratios.EpsTtm.Value!.Value, 2));
        Assert.Equal(75_000m, ratios.BookValuePerShare.Value);
        Assert.Equal(7.2m, decimal.Round(ratios.PriceToEarnings.Value!.Value, 4));
        Assert.Equal(0.8m, ratios.PriceToBook.Value);
        Assert.Equal(0.125m, ratios.ReturnOnEquity.Value);
        Assert.Equal((1, 0), (ratios.ActionsApplied, ratios.ActionsWithheld));
        Assert.Equal(FiscalQuarter.Create(2022, 4), ratios.BalanceSheet);
        Assert.Equal([1, 2, 3, 4], ratios.ProfitQuarters.Select(quarter => quarter.Quarter));
    }

    [Fact]
    public async Task Before_the_bonus_issue_goes_ex_the_count_is_the_balance_sheets()
    {
        var harness = new Harness();
        harness.Close(new DateOnly(2023, 2, 10), 60_000m);

        var ratios = await harness.ReadAsync(new DateOnly(2023, 2, 10));

        Assert.Equal(100_000_000m, ratios.Shares.Value);
        Assert.Equal(90_000m, ratios.BookValuePerShare.Value);
    }

    [Fact]
    public async Task A_dividend_paid_before_a_bonus_issue_is_restated_on_the_new_share_basis()
    {
        var ratios = await new Harness().ReadAsync(March);

        // ₫1,000 per old share is ₫833.33 per share after a 1.2-for-1 bonus.
        Assert.Equal(0.0139m, decimal.Round(ratios.DividendYield.Value!.Value, 4));
    }

    [Fact]
    public async Task An_action_not_yet_announced_is_withheld_from_the_count()
    {
        var harness = new Harness(bonusAnnounced: false);

        var ratios = await harness.ReadAsync(March);

        Assert.Equal(100_000_000m, ratios.Shares.Value);
        Assert.Equal((0, 1), (ratios.ActionsApplied, ratios.ActionsWithheld));
    }

    [Fact]
    public async Task A_report_filed_after_the_session_does_not_count()
    {
        // The fourth quarter is filed on 25 January 2023. On 20 January only
        // the first three quarters are known, which is not a trailing year.
        var harness = new Harness();
        harness.Close(new DateOnly(2023, 1, 20), 60_000m);

        var ratios = await harness.ReadAsync(new DateOnly(2023, 1, 20));

        Assert.Null(ratios.EpsTtm.Value);
        Assert.Contains("four consecutive quarters", ratios.EpsTtm.Absent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_trailing_loss_has_an_EPS_and_no_earnings_multiple()
    {
        var harness = new Harness(quarterProfit: -250_000_000_000m);

        var ratios = await harness.ReadAsync(March);

        Assert.True(ratios.EpsTtm.Value < 0m);
        Assert.Null(ratios.PriceToEarnings.Value);
        Assert.Contains("loss", ratios.PriceToEarnings.Absent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_close_on_the_session_the_price_ratios_are_absent_and_the_rest_stand()
    {
        var ratios = await new Harness().ReadAsync(new DateOnly(2023, 3, 2));

        Assert.Null(ratios.PriceToEarnings.Value);
        Assert.Null(ratios.PriceToBook.Value);
        Assert.NotNull(ratios.EpsTtm.Value);
    }

    private sealed class Harness
    {
        private readonly InstrumentId _instrument = InstrumentId.New();
        private readonly InMemoryFundamentals _fundamentals = new();
        private readonly FakeBarRepository _bars = new();
        private readonly FakeCorporateActionRepository _actions = new();

        public Harness(decimal quarterProfit = 250_000_000_000m, bool bonusAnnounced = true)
        {
            DateOnly? announced = bonusAnnounced ? new DateOnly(2023, 2, 1) : null;

            for (var quarter = 1; quarter <= 4; quarter++)
            {
                var period = FiscalQuarter.Create(2022, quarter);
                var report = Report(period, new DateOnly(2022, quarter * 3, 1).AddMonths(1).AddDays(24));
                Line(report, FinancialStatement.IncomeStatement, "61", period, PeriodSpan.Quarter, quarterProfit);

                if (quarter == 4)
                {
                    Line(report, FinancialStatement.BalanceSheet, "411a", period, PeriodSpan.Instant, 1_000_000_000_000m);
                    Line(report, FinancialStatement.BalanceSheet, "400", period, PeriodSpan.Instant, 10_000_000_000_000m);
                    Line(report, FinancialStatement.BalanceSheet, "429", period, PeriodSpan.Instant, 1_000_000_000_000m);
                    Line(report, FinancialStatement.BalanceSheet, "400", period.PriorYearEnd, PeriodSpan.Instant, 8_000_000_000_000m);
                    Line(report, FinancialStatement.BalanceSheet, "429", period.PriorYearEnd, PeriodSpan.Instant, 1_000_000_000_000m);
                }
            }

            var bonus = Action(CorporateActionType.BonusShares, new DateOnly(2023, 2, 15), ratio: 0.2m, cash: null, announced);
            _actions.AddAdjustment(PriceAdjustment.For(
                bonus, AdjustmentFactor.Create(1m / 1.2m, 1.2m), Price.Create(72_000m), DataRules.AdjustmentVersion, Recorded));

            Action(CorporateActionType.CashDividend, new DateOnly(2022, 10, 3), ratio: null, cash: 1_000m, new DateOnly(2022, 9, 15));

            Close(March, 60_000m);
        }

        public void Close(DateOnly session, decimal close) =>
            _bars.AddRange([OhlcvBar.Record(
                _instrument, BarInterval.OneDay, new DateTimeOffset(session.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                Price.Create(close), Price.Create(close), Price.Create(close), Price.Create(close), 1_000, null, Ids, Recorded)]);

        public Task<FundamentalRatios> ReadAsync(DateOnly session) =>
            new FundamentalRatioService(new FundamentalsReader(_fundamentals), _bars, _actions)
                .ReadAsync(_instrument, session, TestContext.Current.CancellationToken);

        private CorporateAction Action(CorporateActionType type, DateOnly exDate, decimal? ratio, decimal? cash, DateOnly? announcedOn)
        {
            var action = CorporateAction.Record(_instrument, type, exDate, ratio, cash, Ids, Recorded);
            action.Schedule(null, null, announcedOn, Recorded);
            _actions.Add(action);
            return action;
        }

        private FinancialReport Report(FiscalQuarter period, DateOnly disclosedOn)
        {
            var report = FinancialReport.Record(
                _instrument, Ids, $"ids-{Guid.NewGuid():N}", FinancialReportKind.Quarterly, FinancialStatementScope.Consolidated,
                FinancialTemplate.NonFinancialCircular200, period, "Báo cáo tài chính hợp nhất", disclosedOn, null,
                FinancialReportTests.Vietnam, isCorrection: false, valuesFiled: true, Recorded);
            _fundamentals.Add(report);
            return report;
        }

        private void Line(FinancialReport report, FinancialStatement statement, string code, FiscalQuarter period, PeriodSpan span, decimal value) =>
            _fundamentals.Add(StatementLine.Observe(report, statement, code, period, span, value, ValueBasis.AsFiled, Ids, Recorded));
    }
}
