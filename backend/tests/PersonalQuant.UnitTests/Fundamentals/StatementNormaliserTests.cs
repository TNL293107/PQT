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
    public void A_balance_sheet_matching_no_known_form_refuses_the_filing() =>
        Assert.Equal(
            FilingRejectionReason.Unreadable,
            Refusal(Filings.Quarterly(tables: [Filings.Balance(("100", "5,000", "4,500"))])));

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
