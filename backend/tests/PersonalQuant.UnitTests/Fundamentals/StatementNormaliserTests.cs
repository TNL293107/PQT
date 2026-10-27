using PersonalQuant.Application.Fundamentals;
using PersonalQuant.Domain.Fundamentals;

namespace PersonalQuant.UnitTests.Fundamentals;

/// <summary>
/// Verifies that each column lands in its period and span, that a blank is
/// not a zero, and that a heading or tab the importer does not know is
/// refused rather than guessed.
/// </summary>
public sealed class StatementNormaliserTests
{
    private static readonly FiscalQuarter Q1 = FiscalQuarter.Create(2022, 1);

    [Fact]
    public void A_quarterly_report_states_the_quarter_the_year_to_date_and_both_a_year_earlier()
    {
        var values = Values(Filings.Quarterly());

        Assert.Equal(1_000m, values[(FinancialStatement.IncomeStatement, "10", Q1, PeriodSpan.Quarter)]);
        Assert.Equal(800m, values[(FinancialStatement.IncomeStatement, "10", Q1.YearEarlier, PeriodSpan.Quarter)]);
        Assert.Equal(1_000m, values[(FinancialStatement.IncomeStatement, "10", Q1, PeriodSpan.YearToDate)]);
        Assert.Equal(800m, values[(FinancialStatement.IncomeStatement, "10", Q1.YearEarlier, PeriodSpan.YearToDate)]);
    }

    [Fact]
    public void The_opening_balance_is_last_years_close()
    {
        var values = Values(Filings.Quarterly());

        Assert.Equal(5_000m, values[(FinancialStatement.BalanceSheet, "270", Q1, PeriodSpan.Instant)]);
        Assert.Equal(4_500m, values[(FinancialStatement.BalanceSheet, "270", Q1.PriorYearEnd, PeriodSpan.Instant)]);
    }

    [Fact]
    public void On_an_annual_report_this_period_is_the_year_and_the_blank_columns_are_no_value()
    {
        var filing = Filings.Annual(tables: Filings.NonFinancialTables(revenueToDate: "", revenueToDateLastYear: ""));

        var values = Values(filing);
        var fy = FiscalQuarter.Create(2021, 4);

        Assert.Equal(1_000m, values[(FinancialStatement.IncomeStatement, "10", fy, PeriodSpan.YearToDate)]);
        Assert.Equal(800m, values[(FinancialStatement.IncomeStatement, "10", fy.YearEarlier, PeriodSpan.YearToDate)]);
        Assert.DoesNotContain(values.Keys, key => key.Item4 == PeriodSpan.Quarter);
    }

    [Fact]
    public void An_all_zero_statement_was_not_filed_and_leaves_no_zeros()
    {
        var tables = Filings.NonFinancialTables().Append(
            new ProviderStatementTable("LCTT-TT", ["Lũy kế từ đầu năm đến cuối kỳ này Năm nay"], [Filings.Row("20", "0")]));

        var values = Values(Filings.Quarterly(tables: [.. tables]));

        Assert.DoesNotContain(values.Keys, key => key.Item1 == FinancialStatement.CashFlowDirect);
    }

    [Fact]
    public void A_form_left_empty_has_no_values_and_from_2026_is_a_Circular_99_report()
    {
        var empty = new[]
        {
            Filings.Balance(("270", "0", "0"), ("440", "0", "0")),
            new ProviderStatementTable("KQKD", Filings.IncomeColumns, [Filings.Row("10", "0", "0", "0", "0")]),
        };

        var filed = Normalise(Filings.Quarterly(year: 2026, tables: empty));

        Assert.Empty(filed.Values);
        Assert.Equal(FinancialTemplate.NonFinancialCircular99, filed.Template);
    }

    [Fact]
    public void A_bank_form_is_recognised_by_its_codes()
    {
        var tables = new[] { Filings.Balance(("300", "9,000", "8,000"), ("500", "900", "800"), ("800", "9,000", "8,000")) };

        Assert.Equal(FinancialTemplate.Bank, Normalise(Filings.Quarterly(tables: tables)).Template);
    }

    [Fact]
    public void Parentheses_are_a_negative_value()
    {
        var tables = new[]
        {
            Filings.Balance(("270", "5,000", "4,500"), ("440", "5,000", "4,500")),
            new ProviderStatementTable("KQKD", Filings.IncomeColumns, [Filings.Row("22", "(1,234)", "-56", "", "")]),
        };

        var values = Values(Filings.Quarterly(tables: tables));

        Assert.Equal((-1_234m, -56m), (
            values[(FinancialStatement.IncomeStatement, "22", Q1, PeriodSpan.Quarter)],
            values[(FinancialStatement.IncomeStatement, "22", Q1.YearEarlier, PeriodSpan.Quarter)]));
    }

    [Theory]
    [InlineData("KQKD", "Quý trước", "1")]
    [InlineData("TMBCTC", "Kỳ này Năm nay", "1")]
    [InlineData("KQKD", "Kỳ này Năm nay", "1,000.12345")]
    [InlineData("KQKD", "Kỳ này Năm nay", "n/a")]
    public void A_tab_heading_or_cell_the_importer_cannot_read_refuses_the_filing(string tab, string heading, string cell)
    {
        var tables = new[]
        {
            Filings.Balance(("270", "5,000", "4,500"), ("440", "5,000", "4,500")),
            new ProviderStatementTable(tab, [heading], [Filings.Row("10", cell)]),
        };

        Assert.Equal(FilingRejectionReason.Unreadable, Refusal(Filings.Quarterly(tables: tables)));
    }

    [Fact]
    public void One_line_stated_twice_with_different_values_refuses_the_filing()
    {
        var tables = new[]
        {
            Filings.Balance(("270", "5,000", "4,500"), ("270", "5,001", "4,500"), ("440", "5,000", "4,500")),
        };

        Assert.Equal(FilingRejectionReason.ConflictingValues, Refusal(Filings.Quarterly(tables: tables)));
    }

    [Fact]
    public void Two_tabs_holding_the_same_table_refuse_the_filing()
    {
        // The income statement's rows captured again under the cash flow's
        // name, as the slow portal produced in the 6.5 collection.
        var income = new ProviderStatementTable("KQKD", ["Lũy kế từ đầu năm đến cuối kỳ này Năm nay"], [Filings.Row("10", "1,000")]);
        var tables = new[]
        {
            Filings.Balance(("270", "5,000", "4,500"), ("440", "5,000", "4,500")),
            income,
            income with { Name = "LCTT-TT" },
        };

        Assert.Equal(FilingRejectionReason.Unreadable, Refusal(Filings.Quarterly(tables: tables)));
    }

    [Fact]
    public void A_balance_sheet_matching_no_known_form_refuses_the_filing() =>
        Assert.Equal(
            FilingRejectionReason.Unreadable,
            Refusal(Filings.Quarterly(tables: [Filings.Balance(("100", "5,000", "4,500"))])));

    [Fact]
    public void A_form_typed_in_millions_is_read_in_dong_and_its_earnings_per_share_as_typed()
    {
        // VPL's form: ₫17,933bn of capital typed as 17,933,004, and EPS in đồng.
        var tables = new[]
        {
            Filings.Balance(("270", "76,896,069", "44,072,730"), ("440", "76,896,069", "44,072,730"), ("411a", "17,933,004", "15,041,382")),
            new ProviderStatementTable("KQKD", Filings.IncomeColumns, [Filings.Row("60", "408,275", "", "", ""), Filings.Row("70", "145", "", "", "")]),
        };

        var normalised = Normalise(Filings.Quarterly(tables: tables));
        var values = normalised.Values.ToDictionary(value => (value.Statement, value.LineCode, value.Period, value.Span), value => value.Value);

        Assert.Equal(1_000_000m, normalised.Unit);
        Assert.Equal(17_933_004_000_000m, values[(FinancialStatement.BalanceSheet, "411a", Q1, PeriodSpan.Instant)]);
        Assert.Equal(408_275_000_000m, values[(FinancialStatement.IncomeStatement, "60", Q1, PeriodSpan.Quarter)]);
        Assert.Equal(145m, values[(FinancialStatement.IncomeStatement, "70", Q1, PeriodSpan.Quarter)]);
    }

    [Fact]
    public void A_form_typed_in_dong_is_left_as_typed()
    {
        var tables = new[] { Filings.Balance(("270", "5,000", "4,500"), ("440", "5,000", "4,500"), ("411a", "14,813,301,220,000", "")) };

        Assert.Equal(1m, Normalise(Filings.Quarterly(tables: tables)).Unit);
    }

    [Fact]
    public void The_unit_is_read_from_total_capital_when_the_common_share_line_is_wrong()
    {
        // VNM 2025: 411 is ₫20,900bn, 411a a stray ₫157.9bn.
        var tables = new[]
        {
            Filings.Balance(("270", "5,000", "4,500"), ("440", "5,000", "4,500"), ("411", "20,899,554,450,000", ""), ("411a", "157,903,902,450", "")),
        };

        Assert.Equal(1m, Normalise(Filings.Quarterly(tables: tables)).Unit);
    }

    [Fact]
    public void Earnings_per_share_stated_a_million_times_over_is_dropped_and_said_so()
    {
        var tables = new[]
        {
            Filings.Balance(("270", "5,000", "4,500"), ("440", "5,000", "4,500")),
            new ProviderStatementTable("KQKD", Filings.IncomeColumns, [Filings.Row("10", "1,000", "", "", ""), Filings.Row("70", "1,057,000,000", "", "", "")]),
        };

        var normalised = Normalise(Filings.Quarterly(tables: tables));

        Assert.DoesNotContain(normalised.Values, value => value.LineCode == "70");
        Assert.Contains(normalised.Values, value => value.LineCode == "10");
        Assert.Contains("1057000000", Assert.Single(normalised.Dropped!), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("GAS-20220429-eafecbfc6049", 2022, 1)]
    [InlineData("GAS-20221028-fe110add5b3f", 2022, 3)]
    [InlineData("GAS-20230130-cb4708826d28", 2022, 4)]
    [InlineData("GAS-20230428-6245c9318c1e", 2023, 1)]
    [InlineData("GAS-20240426-ba25b2a787c4", 2024, 1)]
    public void A_GAS_form_with_its_income_lines_one_row_low_has_no_profit_to_the_parent_rather_than_zero(
        string reference, int year, int quarter)
    {
        // The shape of GAS's five shifted forms (sprint 8.3, section 5): 61 is
        // 0, 62 holds the profit to the parent (₫3,428.7bn in 2022Q1), 70 the
        // non-controlling interest (60 − 62 to the đồng) and 71 the basic EPS.
        // The last-year column is filed correctly and stays.
        var period = FiscalQuarter.Create(year, quarter);
        var tables = new[]
        {
            Filings.Balance(("270", "5,000", "4,500"), ("440", "5,000", "4,500"), ("411", "22,967,185,500,000", "")),
            new ProviderStatementTable(
                "KQKD",
                Filings.IncomeColumns,
                [
                    Filings.Row("60", "3,495,373,770,718", "2,000,000,000,000", "", ""),
                    Filings.Row("61", "0", "1,950,000,000,000", "", ""),
                    Filings.Row("62", "3,428,700,000,000", "50,000,000,000", "", ""),
                    Filings.Row("70", "66,673,770,718", "1,019", "", ""),
                    Filings.Row("71", "1,791", "1,019", "", ""),
                ]),
        };

        var normalised = Normalise(Filings.Quarterly(year, quarter, ticker: "GAS", reference: reference, tables: tables));
        var values = normalised.Values.ToDictionary(value => (value.Statement, value.LineCode, value.Period, value.Span), value => value.Value);

        foreach (var code in new[] { "61", "62", "70", "71" })
        {
            Assert.False(values.ContainsKey((FinancialStatement.IncomeStatement, code, period, PeriodSpan.Quarter)), code);
        }

        Assert.Equal(3_495_373_770_718m, values[(FinancialStatement.IncomeStatement, "60", period, PeriodSpan.Quarter)]);
        Assert.Equal(1_950_000_000_000m, values[(FinancialStatement.IncomeStatement, "61", period.YearEarlier, PeriodSpan.Quarter)]);
        Assert.Contains("one row low", Assert.Single(normalised.Dropped!), StringComparison.Ordinal);
    }

    [Fact]
    public void A_true_zero_profit_to_the_parent_is_kept()
    {
        // 61 = 0 alone is not the shift: 70 must also equal 60 less 62.
        var tables = new[]
        {
            Filings.Balance(("270", "5,000", "4,500"), ("440", "5,000", "4,500")),
            new ProviderStatementTable(
                "KQKD",
                Filings.IncomeColumns,
                [Filings.Row("60", "10", "", "", ""), Filings.Row("61", "0", "", "", ""), Filings.Row("62", "10", "", "", ""), Filings.Row("70", "0", "", "", "")]),
        };

        var normalised = Normalise(Filings.Quarterly(tables: tables));

        Assert.Contains(normalised.Values, value => value is { LineCode: "61", Value: 0m });
        Assert.Empty(normalised.Dropped!);
    }

    [Theory]
    [InlineData("100", "60", "40")]
    [InlineData("0", "0", "100")]
    public void A_column_that_only_looks_shifted_is_kept(string line61, string line62, string line70)
    {
        // 61 = 60 - 62 filed honestly with 70 equal to the minority by chance;
        // and a true zero profit to the parent with no minority (62 = 0).
        var tables = new[]
        {
            Filings.Balance(("270", "5,000", "4,500"), ("440", "5,000", "4,500")),
            new ProviderStatementTable(
                "KQKD",
                Filings.IncomeColumns,
                [Filings.Row("60", "100", "", "", ""), Filings.Row("61", line61, "", "", ""), Filings.Row("62", line62, "", "", ""), Filings.Row("70", line70, "", "", "")]),
        };

        var normalised = Normalise(Filings.Quarterly(tables: tables));

        Assert.Contains(normalised.Values, value => value.LineCode == "61");
        Assert.Contains(normalised.Values, value => value.LineCode == "70");
        Assert.Empty(normalised.Dropped!);
    }

    [Fact]
    public void Charter_capital_that_fits_no_unit_refuses_the_filing() =>
        Assert.Equal(
            FilingRejectionReason.UnknownUnit,
            Refusal(Filings.Quarterly(tables: [Filings.Balance(("270", "5,000", "4,500"), ("440", "5,000", "4,500"), ("411a", "5,000", ""))])));

    private static NormalisedFiling Normalise(ProviderFiling filing)
    {
        var classification = FilingClassifier.Classify(filing, out _)!;
        var normalised = StatementNormaliser.Normalise(filing, classification, out var rejection);

        Assert.Null(rejection);
        return normalised!;
    }

    private static Dictionary<(FinancialStatement, string, FiscalQuarter, PeriodSpan), decimal> Values(ProviderFiling filing) =>
        Normalise(filing).Values.ToDictionary(value => (value.Statement, value.LineCode, value.Period, value.Span), value => value.Value);

    private static FilingRejectionReason Refusal(ProviderFiling filing)
    {
        var classification = FilingClassifier.Classify(filing, out _)!;

        Assert.Null(StatementNormaliser.Normalise(filing, classification, out var rejection));
        return rejection!.Reason;
    }
}
