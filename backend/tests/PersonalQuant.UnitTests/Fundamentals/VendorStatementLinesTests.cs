using PersonalQuant.Application.Fundamentals;
using PersonalQuant.Domain.Fundamentals;

namespace PersonalQuant.UnitTests.Fundamentals;

/// <summary>
/// Sprint 11.4 acceptance (ADR-038 §5 and its verification record), written by
/// QA + QC before the code: how vendor lines on a fiscal-2026 non-bank report
/// are read as the canonical items.
/// </summary>
/// <remarks>
/// ADR-038 §5 maps the canonical items once in <see cref="StatementLines"/> for
/// the <see cref="FinancialTemplate.NonFinancialCircular99"/> template, keyed by
/// the code verification 1 chose: Vietcap's own field names. Today
/// <see cref="StatementLines.From"/> reads only the C200 and bank templates, so
/// every item below is null until the mapping exists. Values are invented.
/// </remarks>
public sealed class VendorStatementLinesTests
{
    private static readonly FiscalQuarter Q1 = FiscalQuarter.Create(2026, 1);

    [Fact]
    public void Profit_to_the_parent_is_read_from_isa22()
    {
        var lines = Lines((FinancialStatement.IncomeStatement, "isa22", PeriodSpan.Quarter, 1_111_111_111_111m));

        Assert.Equal(1_111_111_111_111m, lines.QuarterProfit(Q1));
    }

    [Fact]
    public void Equity_to_the_parent_is_bsa78_less_the_minority_interest_bsa210()
    {
        // bsa78 is owner's equity including minorities (like IDS line 400), and
        // bsa210 the minority interest (like 429): the parent's share is the
        // difference, as the C200 mapping does with 400 - 429.
        var lines = Lines(
            (FinancialStatement.BalanceSheet, "bsa78", PeriodSpan.Instant, 37_896_654_696_942m),
            (FinancialStatement.BalanceSheet, "bsa210", PeriodSpan.Instant, 100_000_000_000m));

        Assert.Equal(37_796_654_696_942m, lines.EquityToParent(Q1));
    }

    [Fact]
    public void A_vendor_report_s_eps_is_never_read_from_isa23()
    {
        // ADR-038: EPS is unreliable on both sides, so isa23 is not used and
        // EPS comes from profit and the share count. Even if a vendor EPS line
        // were stored, ReportedEps must not return it.
        var lines = Lines(
            (FinancialStatement.IncomeStatement, "isa22", PeriodSpan.Quarter, 1_111_111_111_111m),
            (FinancialStatement.IncomeStatement, "isa23", PeriodSpan.Quarter, 1_478m));

        // The vendor template is read (profit is there), and still no EPS.
        Assert.Equal(1_111_111_111_111m, lines.QuarterProfit(Q1));
        Assert.Null(lines.ReportedEps(Q1));
    }

    [Fact]
    public void An_unmapped_item_is_absent_never_zero()
    {
        // Only total assets is present: profit and equity are absent, not 0.
        var lines = Lines((FinancialStatement.BalanceSheet, "bsa53", PeriodSpan.Instant, 73_997_673_121_789m));

        // The template is recognised (its balance sheet is the latest held)...
        Assert.Equal(Q1, lines.LatestBalanceSheet());
        // ...and what it does not carry is absent.
        Assert.Null(lines.QuarterProfit(Q1));
        Assert.Null(lines.EquityToParent(Q1));
    }

    private static StatementLines Lines(params (FinancialStatement Statement, string Code, PeriodSpan Span, decimal Value)[] values) =>
        StatementLines.From(
            [
                .. values.Select(line => new FundamentalValue(
                    FinancialTemplate.NonFinancialCircular99,
                    line.Statement,
                    line.Code,
                    Q1,
                    line.Span,
                    line.Value,
                    ValueBasis.Vendor,
                    FinancialReportId.New(),
                    FinancialReportKind.Quarterly,
                    DateTimeOffset.UnixEpoch,
                    Restated: false)),
            ],
            FinancialStatementScope.Consolidated);
}
