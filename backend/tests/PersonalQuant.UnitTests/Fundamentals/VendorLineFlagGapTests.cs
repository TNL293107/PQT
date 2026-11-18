using PersonalQuant.Application.Fundamentals;
using PersonalQuant.Domain.Fundamentals;

namespace PersonalQuant.UnitTests.Fundamentals;

/// <summary>
/// QA + QC, test gaps from the PR #26 gate (Tester's survivors d and e): how
/// <see cref="StatementLines"/> carries the per-value vendor flag. Every value
/// invented.
/// </summary>
public sealed class VendorLineFlagGapTests
{
    private static readonly FiscalQuarter Q1 = FiscalQuarter.Create(2026, 1);

    [Fact]
    public void A_filed_line_read_after_a_vendor_line_of_the_same_key_clears_the_vendor_flag()
    {
        // The same quarter's profit, first a vendor's Circular 99 reading, then
        // the issuer's filed Circular 200 line under the same key. The filed
        // value wins, and with it the flag must go: it is no longer a vendor's
        // (survivor d). Profit is read through the latest balance sheet's form,
        // so a filed balance sheet keeps the lines on the Circular 200 family.
        var lines = StatementLines.From(
            [
                Value(FinancialTemplate.NonFinancialCircular200, FinancialStatement.BalanceSheet, "400", PeriodSpan.Instant, 9_000_000_000_000m, ValueBasis.AsFiled),
                Value(FinancialTemplate.NonFinancialCircular200, FinancialStatement.IncomeStatement, "61", PeriodSpan.Quarter, 300_000_000_000m, ValueBasis.Vendor),
                Value(FinancialTemplate.NonFinancialCircular200, FinancialStatement.IncomeStatement, "61", PeriodSpan.Quarter, 300_000_000_000m, ValueBasis.AsFiled),
            ],
            FinancialStatementScope.Consolidated);

        // One quarter is not a trailing year, so TrailingProfit is absent here;
        // its flag still says what it read (ADR-038), which is the point tested.
        var (profit, _) = lines.TrailingProfit();

        Assert.Equal(300_000_000_000m, lines.QuarterProfit(Q1));
        Assert.False(profit.FromVendor, "a filed line read over a vendor line of the same key kept the vendor flag.");
    }

    [Fact]
    public void Equity_is_flagged_vendor_when_only_the_minority_interest_is_a_vendor_value()
    {
        // No filed equity, so equity to the parent is the vendor's bsa78 less
        // bsa210. Here the whole (bsa78) is filed and only the minority (bsa210)
        // is a vendor's: the result still rests on a vendor value (survivor e).
        var lines = StatementLines.From(
            [
                Value(FinancialTemplate.NonFinancialCircular99, FinancialStatement.BalanceSheet, "bsa78", PeriodSpan.Instant, 38_000_000_000_000m, ValueBasis.AsFiled),
                Value(FinancialTemplate.NonFinancialCircular99, FinancialStatement.BalanceSheet, "bsa210", PeriodSpan.Instant, 100_000_000_000m, ValueBasis.Vendor),
            ],
            FinancialStatementScope.Consolidated);

        Assert.Equal(37_900_000_000_000m, lines.EquityToParent(Q1));
        Assert.True(lines.EquityIsVendor(Q1), "equity ignored a vendor minority interest when the whole was not a vendor's.");
    }

    private static FundamentalValue Value(
        FinancialTemplate template,
        FinancialStatement statement,
        string code,
        PeriodSpan span,
        decimal value,
        ValueBasis basis) =>
        new(
            template,
            statement,
            code,
            Q1,
            span,
            value,
            basis,
            FinancialReportId.New(),
            FinancialReportKind.Quarterly,
            DateTimeOffset.UnixEpoch,
            Restated: false);
}
