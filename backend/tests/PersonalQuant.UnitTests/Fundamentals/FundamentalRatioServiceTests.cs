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
/// ₫1,000 cash dividend went ex on 3 October 2022, and a ₫500 one on 1 October
/// 2021, which is the start of the issuer's recorded action history. The
/// close on 1 March 2023 is ₫60,000.
/// </para>
/// </remarks>
public sealed class FundamentalRatioServiceTests
{
    private static readonly DateTimeOffset Recorded = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);
    private static readonly SourceCode Ids = SourceCode.Create("IDS");
    private static readonly DateOnly March = new(2023, 3, 1);
    private static readonly FiscalQuarter Q3 = FiscalQuarter.Create(2022, 3);
    private static readonly FiscalQuarter Q4 = FiscalQuarter.Create(2022, 4);

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
        Assert.Equal(Q4, ratios.BalanceSheet);
        Assert.Equal([1, 2, 3, 4], ratios.ProfitQuarters.Select(quarter => quarter.Quarter));
        Assert.Empty(ratios.Findings);
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
    public async Task A_cancelled_action_counts_nowhere()
    {
        var harness = new Harness();
        harness.Bonus.Cancel("called off", Recorded);

        var ratios = await harness.ReadAsync(March);

        Assert.Equal(100_000_000m, ratios.Shares.Value);
        Assert.Equal(0.0167m, decimal.Round(ratios.DividendYield.Value!.Value, 4));
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
    public async Task A_restatement_changes_the_ratios_only_from_the_day_it_became_known()
    {
        // A correction filed on 10 March 2023 restates the fourth quarter's
        // profit from ₫250bn to ₫450bn.
        var harness = new Harness();
        var correction = harness.Report(Q4, new DateOnly(2023, 3, 10), isCorrection: true);
        harness.Line(correction, FinancialStatement.IncomeStatement, "61", Q4, PeriodSpan.Quarter, 450_000_000_000m);
        harness.Close(new DateOnly(2023, 3, 15), 60_000m);

        var before = await harness.ReadAsync(March);
        var after = await harness.ReadAsync(new DateOnly(2023, 3, 15));

        Assert.Equal(1_000_000_000_000m, before.NetProfitTtm.Value);
        Assert.Equal(1_200_000_000_000m, after.NetProfitTtm.Value);
        Assert.Equal(10_000m, after.EpsTtm.Value);
    }

    [Fact]
    public async Task A_missing_quarter_leaves_the_trailing_figures_absent_with_its_code()
    {
        var ratios = await new Harness(skipQuarter: 2).ReadAsync(March);

        Assert.Null(ratios.NetProfitTtm.Value);
        Assert.Equal(RatioAbsence.MissingQuarter, ratios.EpsTtm.Reason);
        Assert.Equal(RatioAbsence.MissingQuarter, ratios.PriceToEarnings.Reason);
        Assert.Equal(RatioAbsence.MissingQuarter, ratios.ReturnOnEquity.Reason);
        Assert.NotNull(ratios.PriceToBook.Value);
    }

    [Fact]
    public async Task Return_on_equity_needs_equity_at_both_ends_of_the_trailing_year()
    {
        var ratios = await new Harness(priorYearEquity: false).ReadAsync(March);

        Assert.Null(ratios.ReturnOnEquity.Value);
        Assert.Equal(RatioAbsence.EquityNotKnownAtBothEnds, ratios.ReturnOnEquity.Reason);
        Assert.NotNull(ratios.EpsTtm.Value);
    }

    [Fact]
    public async Task A_trailing_loss_has_an_EPS_and_no_earnings_multiple()
    {
        var harness = new Harness(quarterProfit: -250_000_000_000m);

        var ratios = await harness.ReadAsync(March);

        Assert.True(ratios.EpsTtm.Value < 0m);
        Assert.Null(ratios.PriceToEarnings.Value);
        Assert.Equal(RatioAbsence.TrailingLoss, ratios.PriceToEarnings.Reason);
    }

    [Fact]
    public async Task Without_a_close_on_the_session_the_price_ratios_are_absent_and_the_rest_stand()
    {
        var ratios = await new Harness().ReadAsync(new DateOnly(2023, 3, 2));

        Assert.Equal(RatioAbsence.NoClose, ratios.PriceToEarnings.Reason);
        Assert.Equal(RatioAbsence.NoClose, ratios.PriceToBook.Reason);
        Assert.Equal(RatioAbsence.NoClose, ratios.DividendYield.Reason);
        Assert.NotNull(ratios.EpsTtm.Value);
    }

    [Fact]
    public async Task The_bank_form_reads_its_own_codes()
    {
        // Capital 411, equity 500, profit after tax 21 less minority interest 22.
        var harness = new Harness(template: FinancialTemplate.Bank);

        var ratios = await harness.ReadAsync(March);

        Assert.Equal(120_000_000m, ratios.Shares.Value);
        Assert.Equal(1_000_000_000_000m, ratios.NetProfitTtm.Value);
        Assert.Equal(75_000m, ratios.BookValuePerShare.Value);
        Assert.Equal(0.125m, ratios.ReturnOnEquity.Value);
    }

    [Fact]
    public async Task An_issuer_with_no_consolidated_values_is_read_on_its_separate_statements()
    {
        var ratios = await new Harness(scope: FinancialStatementScope.Separate).ReadAsync(March);

        Assert.Equal(FinancialStatementScope.Separate, ratios.Scope);
        Assert.Equal(1_000_000_000_000m, ratios.NetProfitTtm.Value);
    }

    [Fact]
    public async Task Consolidated_statements_are_read_when_any_are_known_whatever_the_separate_ones_say()
    {
        var harness = new Harness();
        var separate = harness.Report(Q4, new DateOnly(2023, 1, 20), FinancialStatementScope.Separate);
        harness.Line(separate, FinancialStatement.BalanceSheet, "411a", Q4, PeriodSpan.Instant, 5_000_000_000_000m);

        var ratios = await harness.ReadAsync(March);

        Assert.Equal(FinancialStatementScope.Consolidated, ratios.Scope);
        Assert.Equal(120_000_000m, ratios.Shares.Value);
    }

    [Fact]
    public async Task An_action_not_yet_in_the_capital_is_applied_as_registered_late()
    {
        // The bonus went ex in February, but 2023Q1's capital still shows
        // ₫1,000bn: the increase was registered after the quarter closed.
        var harness = new Harness();
        var q1 = FiscalQuarter.Create(2023, 1);
        var report = harness.Report(q1, new DateOnly(2023, 4, 25));
        harness.Line(report, FinancialStatement.BalanceSheet, "411a", q1, PeriodSpan.Instant, 1_000_000_000_000m);
        harness.Line(report, FinancialStatement.IncomeStatement, "61", q1, PeriodSpan.Quarter, 250_000_000_000m);
        harness.Close(new DateOnly(2023, 5, 2), 60_000m);

        var ratios = await harness.ReadAsync(new DateOnly(2023, 5, 2));

        Assert.Equal(q1, ratios.BalanceSheet);
        Assert.Equal(120_000_000m, ratios.Shares.Value);
        Assert.Contains("registered late", Assert.Single(ratios.Findings), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_action_already_in_the_capital_is_not_applied_again()
    {
        var harness = new Harness();
        var q1 = FiscalQuarter.Create(2023, 1);
        var report = harness.Report(q1, new DateOnly(2023, 4, 25));
        harness.Line(report, FinancialStatement.BalanceSheet, "411a", q1, PeriodSpan.Instant, 1_200_000_000_000m);
        harness.Line(report, FinancialStatement.IncomeStatement, "61", q1, PeriodSpan.Quarter, 250_000_000_000m);
        harness.Close(new DateOnly(2023, 5, 2), 60_000m);

        var ratios = await harness.ReadAsync(new DateOnly(2023, 5, 2));

        Assert.Equal(120_000_000m, ratios.Shares.Value);
        Assert.Empty(ratios.Findings);
    }

    [Fact]
    public async Task Capital_that_already_shows_an_action_going_ex_later_does_not_count_it_twice()
    {
        // 2022Q4's capital is 1.2 times 2022Q3's with no action between: the
        // February bonus was registered before it went ex.
        var harness = new Harness(capitalQ4: 1_200_000_000_000m, capitalQ3: 1_000_000_000_000m);

        var ratios = await harness.ReadAsync(March);

        Assert.Equal(120_000_000m, ratios.Shares.Value);
        Assert.Equal(0, ratios.ActionsApplied);
        Assert.Contains("not applied twice", Assert.Single(ratios.Findings), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_capital_step_no_action_explains_is_reported_and_changes_nothing()
    {
        // A 1.2x action went ex in November, and capital rose 1.15x.
        var harness = new Harness(capitalQ4: 1_150_000_000_000m, capitalQ3: 1_000_000_000_000m);
        harness.AddBonus(new DateOnly(2022, 11, 1), 1.2m, new DateOnly(2022, 10, 15));
        harness.Close(new DateOnly(2023, 2, 10), 60_000m);

        var ratios = await harness.ReadAsync(new DateOnly(2023, 2, 10));

        Assert.Equal(115_000_000m, ratios.Shares.Value);
        Assert.Contains("Capital moved x1.15", Assert.Single(ratios.Findings), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reported_EPS_far_from_profit_per_par_share_is_a_finding()
    {
        // ₫250bn over 100m shares is ₫2,500 a share; the form says ₫25.
        var harness = new Harness(reportedEps: 25m);

        var ratios = await harness.ReadAsync(March);

        Assert.Contains("in doubt", Assert.Single(ratios.Findings), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reported_EPS_near_profit_per_par_share_finds_nothing()
    {
        // Below profit per share by the bonus and welfare fund, as issuers report it.
        var ratios = await new Harness(reportedEps: 2_200m).ReadAsync(March);

        Assert.Empty(ratios.Findings);
    }

    [Fact]
    public async Task Without_a_sweep_the_yield_is_absent_not_zero()
    {
        var ratios = await new Harness(swept: false).ReadAsync(March);

        Assert.Null(ratios.DividendYield.Value);
        Assert.Equal(RatioAbsence.DividendHistoryIncomplete, ratios.DividendYield.Reason);
    }

    [Fact]
    public async Task A_recorded_action_history_is_not_coverage()
    {
        // The issuer has actions back to 2021, which ADR-030's proxy took as
        // coverage. Without a sweep, it is not.
        var ratios = await new Harness(swept: false, history: true).ReadAsync(new DateOnly(2023, 3, 1));

        Assert.Equal(RatioAbsence.DividendHistoryIncomplete, ratios.DividendYield.Reason);
    }

    [Fact]
    public async Task A_sweep_that_starts_inside_the_trailing_year_does_not_cover_it()
    {
        var harness = new Harness(swept: false);
        harness.Sweep(new DateOnly(2022, 6, 1), new DateOnly(2026, 10, 18));

        var ratios = await harness.ReadAsync(March);

        Assert.Equal(RatioAbsence.DividendHistoryIncomplete, ratios.DividendYield.Reason);
    }

    [Fact]
    public async Task A_session_after_the_sweep_is_not_covered()
    {
        // Read on 2023-02-28: nothing that went ex after it is vouched for.
        var harness = new Harness(swept: false);
        harness.Sweep(new DateOnly(2021, 10, 1), new DateOnly(2023, 2, 28));

        var ratios = await harness.ReadAsync(March);

        Assert.Equal(RatioAbsence.DividendHistoryIncomplete, ratios.DividendYield.Reason);
    }

    [Fact]
    public async Task A_sweep_recorded_after_the_observation_instant_is_not_read()
    {
        var harness = new Harness(swept: false);
        harness.Close(March, 60_000m, recorded: new DateTimeOffset(2023, 3, 1, 9, 0, 0, TimeSpan.Zero));
        harness.Sweep(new DateOnly(2021, 10, 1), new DateOnly(2026, 10, 18), recorded: new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero));

        var before = await harness.ReadAsync(March, new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero));
        var after = await harness.ReadAsync(March, new DateTimeOffset(2026, 10, 22, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(RatioAbsence.DividendHistoryIncomplete, before.DividendYield.Reason);
        Assert.NotNull(after.DividendYield.Value);
    }

    [Fact]
    public async Task A_capital_step_with_no_action_near_it_is_a_finding_and_changes_nothing()
    {
        // ACB's case: capital rises 15% from 2022Q3 to 2022Q4 and no action
        // went ex in between. The bonus of February 2023 is after both.
        var harness = new Harness(capitalQ3: 869_565_217_391m);

        var ratios = await harness.ReadAsync(March);

        Assert.Equal(120_000_000m, ratios.Shares.Value);
        var finding = Assert.Single(ratios.Findings);
        Assert.Contains("Capital stepped +15.0% between 2022Q3 and 2022Q4", finding, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_capital_step_inside_the_tolerance_is_not_a_finding()
    {
        var ratios = await new Harness(capitalQ3: 990_000_000_000m).ReadAsync(March);

        Assert.Empty(ratios.Findings);
    }

    [Fact]
    public async Task A_capital_step_with_an_action_near_it_is_left_to_the_action_checks()
    {
        // A 15% bonus that went ex in 2022Q4 explains the same step: the
        // no-action finding does not fire, and neither does an ADR-030 one.
        var harness = new Harness(capitalQ3: 869_565_217_391m);
        harness.AddBonus(new DateOnly(2022, 11, 15), 1.15m, new DateOnly(2022, 11, 1));

        var ratios = await harness.ReadAsync(March);

        Assert.DoesNotContain(ratios.Findings, finding => finding.Contains("Capital stepped", StringComparison.Ordinal));
    }

    [Fact]
    public async Task VNM_swept_from_July_2023_has_a_yield_from_July_2024_until_the_sweep_date()
    {
        // VNM's cash dividends as transcribed from VSDC (corporate-actions.csv),
        // and its sweep: complete from 2023-07-01, read on 2026-10-21. VNM had
        // no share action, so every dividend is on today's share basis.
        var harness = new Harness(actions: false, swept: false);
        harness.Sweep(new DateOnly(2023, 7, 1), new DateOnly(2026, 10, 21));

        foreach (var (exDate, cash, announced) in new (DateOnly, decimal, DateOnly)[]
        {
            (new(2023, 8, 3), 2_450m, new(2023, 7, 17)),
            (new(2023, 12, 27), 500m, new(2023, 12, 13)),
            (new(2024, 3, 15), 900m, new(2024, 3, 7)),
            (new(2024, 9, 24), 2_450m, new(2024, 8, 30)),
            (new(2024, 12, 26), 500m, new(2024, 12, 10)),
            (new(2025, 5, 14), 2_000m, new(2025, 4, 29)),
            (new(2025, 10, 16), 2_850m, new(2025, 10, 2)),
            (new(2026, 6, 26), 1_850m, new(2026, 6, 17)),
        })
        {
            harness.Dividend(exDate, cash, announced);
        }

        foreach (var session in new DateOnly[] { new(2024, 6, 28), new(2024, 7, 1), new(2024, 9, 30), new(2026, 10, 21), new(2026, 10, 22) })
        {
            harness.Close(session, 70_000m);
        }

        // The trailing year of 2024-06-28 starts before 2023-07-01.
        Assert.Equal(RatioAbsence.DividendHistoryIncomplete, (await harness.ReadAsync(new DateOnly(2024, 6, 28))).DividendYield.Reason);

        // 2023-08-03, 2023-12-27 and 2024-03-15: 2,450 + 500 + 900 = 3,850.
        Assert.Equal(3_850m / 70_000m, (await harness.ReadAsync(new DateOnly(2024, 7, 1))).DividendYield.Value);

        // 2023-12-27, 2024-03-15 and 2024-09-24: 500 + 900 + 2,450 = 3,850.
        Assert.Equal(3_850m / 70_000m, (await harness.ReadAsync(new DateOnly(2024, 9, 30))).DividendYield.Value);

        // On the sweep's last day the year runs from 2025-10-21, so 2025-10-16
        // has dropped out and only 2026-06-26's 1,850 is in it.
        Assert.Equal(1_850m / 70_000m, (await harness.ReadAsync(new DateOnly(2026, 10, 21))).DividendYield.Value);

        // The day after the sweep is not vouched for.
        Assert.Equal(RatioAbsence.DividendHistoryIncomplete, (await harness.ReadAsync(new DateOnly(2026, 10, 22))).DividendYield.Reason);
    }

    [Fact]
    public async Task Inside_a_sweep_with_no_dividend_the_yield_is_zero()
    {
        var harness = new Harness();
        harness.Close(new DateOnly(2024, 3, 1), 60_000m);

        var ratios = await harness.ReadAsync(new DateOnly(2024, 3, 1));

        Assert.Equal(0m, ratios.DividendYield.Value);
    }

    [Fact]
    public async Task A_series_read_equals_one_read_per_session()
    {
        var harness = new Harness();
        harness.Close(new DateOnly(2023, 2, 10), 60_000m);
        harness.Close(new DateOnly(2024, 3, 1), 61_000m);
        DateOnly[] sessions = [new(2023, 2, 10), March, new(2023, 3, 2), new(2024, 3, 1)];

        var series = await harness.ReadSeriesAsync(sessions);

        Assert.Equal(sessions, series.Select(ratios => ratios.Session));

        for (var index = 0; index < sessions.Length; index++)
        {
            Assert.Equal(Render(await harness.ReadAsync(sessions[index])), Render(series[index]));
        }
    }

    /// <summary>Every field of a result, as text, so two results compare by value.</summary>
    private static string Render(FundamentalRatios ratios) =>
        string.Join(
            "|",
            ratios.Session,
            ratios.KnownAsOfUtc,
            ratios.Scope,
            string.Join(";", new[]
            {
                ratios.Close, ratios.Shares, ratios.NetProfitTtm, ratios.EpsTtm, ratios.BookValuePerShare,
                ratios.PriceToEarnings, ratios.PriceToBook, ratios.ReturnOnEquity, ratios.DividendYield,
            }.Select(value => $"{value.Value}/{value.Reason}/{value.Absent}")),
            ratios.BalanceSheet,
            string.Join(";", ratios.BalanceSheetReports.Select(id => id.Value)),
            string.Join(";", ratios.ProfitQuarters),
            ratios.ActionsApplied,
            ratios.ActionsWithheld,
            string.Join(";", ratios.Findings));

    [Fact]
    public async Task A_restatement_changes_the_sessions_after_it_and_none_before()
    {
        var harness = new Harness();
        var correction = harness.Report(Q4, new DateOnly(2023, 4, 20), isCorrection: true);
        harness.Line(correction, FinancialStatement.IncomeStatement, "61", Q4, PeriodSpan.Quarter, 450_000_000_000m);

        var before = await harness.ReadAsync(new DateOnly(2023, 4, 19));
        var after = await harness.ReadAsync(new DateOnly(2023, 4, 24));

        Assert.Equal(1_000_000_000_000m, before.NetProfitTtm.Value);
        Assert.Equal(1_200_000_000_000m, after.NetProfitTtm.Value);
    }

    [Fact]
    public async Task An_observation_instant_hides_a_report_filed_after_it()
    {
        // Everything else was recorded on 31 March 2023; the correction was
        // filed on 20 April and recorded the day after.
        var harness = new Harness(recorded: new DateTimeOffset(2023, 3, 31, 12, 0, 0, TimeSpan.Zero));
        var correction = harness.Report(
            Q4, new DateOnly(2023, 4, 20), isCorrection: true, recorded: new DateTimeOffset(2023, 4, 21, 3, 0, 0, TimeSpan.Zero));
        harness.Line(correction, FinancialStatement.IncomeStatement, "61", Q4, PeriodSpan.Quarter, 450_000_000_000m);

        var session = new DateOnly(2023, 5, 2);
        var asOfApril = new DateTimeOffset(2023, 4, 15, 0, 0, 0, TimeSpan.Zero);
        var earlier = await harness.ReadAsync(session, asOfApril);
        var later = await harness.ReadAsync(session, new DateTimeOffset(2023, 4, 25, 0, 0, 0, TimeSpan.Zero));
        var current = await harness.ReadAsync(session);

        Assert.Equal(1_000_000_000_000m, earlier.NetProfitTtm.Value);
        Assert.Equal(asOfApril, earlier.KnownAsOfUtc);
        Assert.Equal(1_200_000_000_000m, later.NetProfitTtm.Value);
        Assert.Equal(1_200_000_000_000m, current.NetProfitTtm.Value);
    }

    [Fact]
    public async Task An_observation_instant_reads_the_close_as_it_was_recorded()
    {
        var harness = new Harness();
        harness.Close(new DateOnly(2023, 3, 2), 61_000m, recorded: new DateTimeOffset(2023, 3, 2, 9, 0, 0, TimeSpan.Zero));

        var held = await harness.ReadAsync(new DateOnly(2023, 3, 2), new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero));
        var notYet = await harness.ReadAsync(March, new DateTimeOffset(2023, 3, 2, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(61_000m, held.Close.Value);
        Assert.Equal(RatioAbsence.NoClose, notYet.Close.Reason);
    }

    [Fact]
    public async Task An_action_announced_after_the_observation_instant_is_withheld()
    {
        var harness = new Harness(recorded: new DateTimeOffset(2023, 1, 31, 0, 0, 0, TimeSpan.Zero));
        harness.Close(March, 60_000m, recorded: new DateTimeOffset(2023, 3, 1, 9, 0, 0, TimeSpan.Zero));

        // The bonus was announced on 1 February; a dataset observed on 31
        // January cannot apply it, whatever session it reads.
        var ratios = await harness.ReadAsync(March, new DateTimeOffset(2023, 1, 31, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(100_000_000m, ratios.Shares.Value);
        Assert.Equal((0, 1), (ratios.ActionsApplied, ratios.ActionsWithheld));
    }

    [Fact]
    public async Task The_balance_sheet_names_the_reports_that_stated_it()
    {
        var harness = new Harness();

        var ratios = await harness.ReadAsync(March);

        Assert.Single(ratios.BalanceSheetReports);
    }

    [Fact]
    public async Task An_issuer_with_no_corporate_action_says_its_count_is_not_adjusted()
    {
        var ratios = await new Harness(actions: false, swept: false).ReadAsync(March);

        Assert.Equal(100_000_000m, ratios.Shares.Value);
        Assert.Contains(ratios.Findings, finding => finding.Contains("No corporate action is recorded", StringComparison.Ordinal));
        Assert.Equal(RatioAbsence.DividendHistoryIncomplete, ratios.DividendYield.Reason);
    }

    [Fact]
    public async Task A_swept_issuer_with_no_action_has_a_known_zero_yield()
    {
        // MSN's case: every notice read, and none paid anything.
        var ratios = await new Harness(actions: false).ReadAsync(March);

        Assert.Equal(0m, ratios.DividendYield.Value);
    }

    private sealed class Harness
    {
        private readonly InstrumentId _instrument = InstrumentId.New();
        private readonly InMemoryFundamentals _fundamentals = new();
        private readonly FakeBarRepository _bars = new();
        private readonly FakeCorporateActionRepository _actions = new();
        private readonly FakeCorporateActionCoverageRepository _coverage = new();
        private readonly FinancialTemplate _template;
        private readonly FinancialStatementScope _scope;
        private readonly DateTimeOffset _recorded;

        public Harness(
            decimal quarterProfit = 250_000_000_000m,
            bool bonusAnnounced = true,
            FinancialTemplate template = FinancialTemplate.NonFinancialCircular200,
            FinancialStatementScope scope = FinancialStatementScope.Consolidated,
            int? skipQuarter = null,
            bool priorYearEquity = true,
            decimal capitalQ4 = 1_000_000_000_000m,
            decimal? capitalQ3 = null,
            decimal? reportedEps = null,
            bool history = true,
            bool actions = true,
            DateTimeOffset? recorded = null,
            bool swept = true)
        {
            _recorded = recorded ?? Recorded;

            if (swept)
            {
                Sweep(new DateOnly(2021, 10, 1), new DateOnly(2026, 10, 18));
            }
            _template = template;
            _scope = scope;
            var bank = template == FinancialTemplate.Bank;

            for (var quarter = 1; quarter <= 4; quarter++)
            {
                if (quarter == skipQuarter)
                {
                    continue;
                }

                var period = FiscalQuarter.Create(2022, quarter);
                var report = Report(period, new DateOnly(2022, quarter * 3, 1).AddMonths(1).AddDays(24));

                if (bank)
                {
                    Line(report, FinancialStatement.IncomeStatement, "21", period, PeriodSpan.Quarter, quarterProfit + 50_000_000_000m);
                    Line(report, FinancialStatement.IncomeStatement, "22", period, PeriodSpan.Quarter, 50_000_000_000m);
                }
                else
                {
                    Line(report, FinancialStatement.IncomeStatement, "61", period, PeriodSpan.Quarter, quarterProfit);
                }

                if (reportedEps is { } eps)
                {
                    Line(report, FinancialStatement.IncomeStatement, bank ? "23" : "70", period, PeriodSpan.Quarter, eps);
                }

                if (quarter == 3 && capitalQ3 is { } third)
                {
                    Line(report, FinancialStatement.BalanceSheet, bank ? "411" : "411a", period, PeriodSpan.Instant, third);
                }

                if (quarter == 4)
                {
                    Line(report, FinancialStatement.BalanceSheet, bank ? "411" : "411a", period, PeriodSpan.Instant, capitalQ4);
                    Equity(report, period, 10_000_000_000_000m);

                    if (priorYearEquity)
                    {
                        Equity(report, period.PriorYearEnd, 8_000_000_000_000m);
                    }
                }
            }

            Close(March, 60_000m);

            if (!actions)
            {
                Bonus = null!;
                return;
            }

            Bonus = AddBonus(new DateOnly(2023, 2, 15), 1.2m, bonusAnnounced ? new DateOnly(2023, 2, 1) : null);

            Action(CorporateActionType.CashDividend, new DateOnly(2022, 10, 3), ratio: null, cash: 1_000m, new DateOnly(2022, 9, 15));

            if (history)
            {
                Action(CorporateActionType.CashDividend, new DateOnly(2021, 10, 1), ratio: null, cash: 500m, new DateOnly(2021, 9, 15));
            }
        }

        public CorporateAction Bonus { get; }

        public CorporateAction AddBonus(DateOnly exDate, decimal factor, DateOnly? announcedOn)
        {
            var bonus = Action(CorporateActionType.BonusShares, exDate, ratio: factor - 1m, cash: null, announcedOn);
            _actions.AddAdjustment(PriceAdjustment.For(
                bonus, AdjustmentFactor.Create(1m / factor, factor), Price.Create(72_000m), DataRules.AdjustmentVersion, Recorded));
            return bonus;
        }

        public void Close(DateOnly session, decimal close, DateTimeOffset? recorded = null)
        {
            var bar = OhlcvBar.Record(
                _instrument, BarInterval.OneDay, new DateTimeOffset(session.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                Price.Create(close), Price.Create(close), Price.Create(close), Price.Create(close), 1_000, null, Ids, recorded ?? _recorded);
            _bars.AddRange([bar]);
            _bars.Revisions.Add(BarRevision.Snapshot(bar, recorded ?? _recorded));
        }

        public Task<FundamentalRatios> ReadAsync(DateOnly session, DateTimeOffset? knownAsOf = null) =>
            Service().ReadAsync(_instrument, session, knownAsOf, TestContext.Current.CancellationToken);

        public Task<IReadOnlyList<FundamentalRatios>> ReadSeriesAsync(IReadOnlyList<DateOnly> sessions, DateTimeOffset? knownAsOf = null) =>
            Service().ReadSeriesAsync(_instrument, sessions, knownAsOf, TestContext.Current.CancellationToken);

        private FundamentalRatioService Service() =>
            new(new FundamentalsReader(_fundamentals), _bars, _actions, _coverage);

        public void Dividend(DateOnly exDate, decimal cash, DateOnly announcedOn) =>
            Action(CorporateActionType.CashDividend, exDate, ratio: null, cash, announcedOn);

        public void Sweep(DateOnly completeFrom, DateOnly sweptOn, DateTimeOffset? recorded = null) =>
            _coverage.Add(CorporateActionCoverage.Record(_instrument, completeFrom, sweptOn, "VSDC notices", recorded ?? _recorded));

        public FinancialReport Report(
            FiscalQuarter period,
            DateOnly disclosedOn,
            FinancialStatementScope? scope = null,
            bool isCorrection = false,
            DateTimeOffset? recorded = null)
        {
            var report = FinancialReport.Record(
                _instrument, Ids, $"ids-{Guid.NewGuid():N}", FinancialReportKind.Quarterly, scope ?? _scope,
                _template, period, "Báo cáo tài chính", disclosedOn, null,
                FinancialReportTests.Vietnam, isCorrection, valuesFiled: true, recorded ?? _recorded);
            _fundamentals.Add(report);
            return report;
        }

        public void Line(FinancialReport report, FinancialStatement statement, string code, FiscalQuarter period, PeriodSpan span, decimal value) =>
            _fundamentals.Add(StatementLine.Observe(report, statement, code, period, span, value, ValueBasis.AsFiled, Ids, report.RecordedAtUtc));

        private void Equity(FinancialReport report, FiscalQuarter period, decimal total)
        {
            // Bank 500 is equity to the parent; Circular 200 states 400 and
            // the minorities (429) to take from it.
            if (_template == FinancialTemplate.Bank)
            {
                Line(report, FinancialStatement.BalanceSheet, "500", period, PeriodSpan.Instant, total - 1_000_000_000_000m);
                return;
            }

            Line(report, FinancialStatement.BalanceSheet, "400", period, PeriodSpan.Instant, total);
            Line(report, FinancialStatement.BalanceSheet, "429", period, PeriodSpan.Instant, 1_000_000_000_000m);
        }

        private CorporateAction Action(CorporateActionType type, DateOnly exDate, decimal? ratio, decimal? cash, DateOnly? announcedOn)
        {
            var action = CorporateAction.Record(_instrument, type, exDate, ratio, cash, Ids, Recorded);
            action.Schedule(null, null, announcedOn, Recorded);
            _actions.Add(action);
            return action;
        }
    }
}
