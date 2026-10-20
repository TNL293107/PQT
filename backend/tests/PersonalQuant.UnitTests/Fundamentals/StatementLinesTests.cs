using PersonalQuant.Application.Fundamentals;
using PersonalQuant.Domain.Fundamentals;

namespace PersonalQuant.UnitTests.Fundamentals;

/// <summary>
/// Verifies which charter-capital line a share count is read from on the
/// Circular 200 form.
/// </summary>
public sealed class StatementLinesTests
{
    private static readonly FiscalQuarter Q2 = FiscalQuarter.Create(2025, 2);

    [Fact]
    public void Common_shares_are_read_when_the_split_adds_up_to_the_total()
    {
        // MSN: ₫15,129bn contributed, of which ₫746bn is preferred.
        var lines = Lines(("411", 15_129_281_000_000m), ("411a", 14_383_516_000_000m), ("411b", 745_765_000_000m));

        Assert.Equal(14_383_516_000_000m, lines.CharterCapital(Q2));
    }

    [Fact]
    public void The_total_is_read_when_the_common_line_does_not_add_up_and_there_are_no_preferred_shares()
    {
        // VNM 2025: a stray 411a a hundredth of the total.
        var lines = Lines(("411", 20_899_554_450_000m), ("411a", 157_903_902_450m));

        Assert.Equal(20_899_554_450_000m, lines.CharterCapital(Q2));
    }

    [Fact]
    public void No_capital_is_read_when_the_split_does_not_add_up_and_preferred_shares_exist() =>
        Assert.Null(Lines(("411", 15_000m), ("411a", 10_000m), ("411b", 1_000m)).CharterCapital(Q2));

    [Fact]
    public void The_common_line_alone_is_read() =>
        Assert.Equal(9_000m, Lines(("411a", 9_000m)).CharterCapital(Q2));

    private static StatementLines Lines(params (string Code, decimal Value)[] balance) =>
        StatementLines.From(
            [
                .. balance.Select(line => new FundamentalValue(
                    FinancialTemplate.NonFinancialCircular200,
                    FinancialStatement.BalanceSheet,
                    line.Code,
                    Q2,
                    PeriodSpan.Instant,
                    line.Value,
                    ValueBasis.AsFiled,
                    FinancialReportId.New(),
                    FinancialReportKind.Quarterly,
                    DateTimeOffset.UnixEpoch,
                    Restated: false)),
            ],
            FinancialStatementScope.Consolidated);
}
