using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.Fundamentals;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.UnitTests.Fundamentals;

/// <summary>
/// Verifies that a statement value is anchored to the right period and span,
/// and that a changed reading supersedes the old one without erasing it.
/// </summary>
public sealed class StatementLineTests
{
    private static readonly DateTimeOffset Read = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);
    private static readonly FiscalQuarter Q1 = FiscalQuarter.Create(2022, 1);
    private static readonly SourceCode Vendor = SourceCode.Create("VCI");

    [Theory]
    [InlineData("01", "01")]
    [InlineData("1", "01")]
    [InlineData("270", "270")]
    [InlineData("411A", "411a")]
    [InlineData(" .37 ", "37")]
    public void Line_codes_are_normalised_so_two_sources_agree(string written, string normalised) =>
        Assert.Equal(normalised, StatementLine.NormaliseLineCode(written));

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("4x1")]
    [InlineData("411ab")]
    public void A_code_that_is_not_a_line_code_is_refused(string written) =>
        Assert.Throws<DomainValidationException>(() => StatementLine.NormaliseLineCode(written));

    [Theory]
    [InlineData(FinancialStatement.BalanceSheet, PeriodSpan.Quarter)]
    [InlineData(FinancialStatement.IncomeStatement, PeriodSpan.Instant)]
    public void A_balance_needs_an_instant_and_a_flow_a_span(FinancialStatement statement, PeriodSpan span) =>
        Assert.Throws<DomainValidationException>(() => Observe(Report(), statement, "10", Q1, span, 1m));

    [Fact]
    public void A_report_states_its_own_and_earlier_periods_but_no_later_one()
    {
        var report = Report();

        // The opening balance of the year and last year's quarter are both
        // earlier periods this report states again.
        _ = Observe(report, FinancialStatement.BalanceSheet, "270", Q1.PriorYearEnd, PeriodSpan.Instant, 1m);
        _ = Observe(report, FinancialStatement.IncomeStatement, "10", Q1.YearEarlier, PeriodSpan.Quarter, 1m);

        Assert.Throws<DomainValidationException>(() => Observe(
            report, FinancialStatement.IncomeStatement, "10", FiscalQuarter.Create(2022, 2), PeriodSpan.Quarter, 1m));
    }

    [Fact]
    public void A_report_recorded_without_values_states_no_lines() =>
        Assert.Throws<DomainStateException>(() => Observe(
            FinancialReportTests.Quarterly(new DateOnly(2022, 4, 22), null, valuesFiled: false),
            FinancialStatement.IncomeStatement, "10", Q1, PeriodSpan.Quarter, 1m));

    [Fact]
    public void A_value_with_more_places_than_the_column_keeps_is_refused() =>
        Assert.Throws<DomainValidationException>(() => Observe(
            Report(), FinancialStatement.IncomeStatement, "70", Q1, PeriodSpan.Quarter, 1.23456m));

    [Fact]
    public void A_changed_reading_supersedes_the_old_one_at_the_instant_it_opens()
    {
        var first = Observe(Report(), FinancialStatement.IncomeStatement, "10", Q1, PeriodSpan.Quarter, 9_730_253_844_391m);
        var later = Read.AddDays(30);

        var second = first.Restate(9_730_253_844_000m, ValueBasis.Vendor, Vendor, later);

        Assert.Equal((later, false), (first.ObservedToUtc!.Value, first.IsCurrent));
        Assert.Equal((1, later, true), (second.Revision, second.ObservedFromUtc, second.IsCurrent));
        Assert.Equal((first.ReportId, first.LineCode, first.Period), (second.ReportId, second.LineCode, second.Period));
    }

    [Fact]
    public void An_unchanged_reading_is_not_a_restatement()
    {
        var line = Observe(Report(), FinancialStatement.IncomeStatement, "10", Q1, PeriodSpan.Quarter, 5m);

        Assert.True(line.States(5m, ValueBasis.AsFiled));
        Assert.Throws<DomainStateException>(() => line.Restate(5m, ValueBasis.AsFiled, FinancialReportTests.Ids, Read.AddDays(1)));
    }

    [Fact]
    public void A_superseded_reading_cannot_be_restated_again()
    {
        var line = Observe(Report(), FinancialStatement.IncomeStatement, "10", Q1, PeriodSpan.Quarter, 5m);
        _ = line.Restate(6m, ValueBasis.AsFiled, FinancialReportTests.Ids, Read.AddDays(1));

        Assert.Throws<DomainStateException>(() => line.Restate(7m, ValueBasis.AsFiled, FinancialReportTests.Ids, Read.AddDays(2)));
    }

    private static FinancialReport Report() => FinancialReportTests.Quarterly(new DateOnly(2022, 4, 22), null);

    private static StatementLine Observe(
        FinancialReport report,
        FinancialStatement statement,
        string code,
        FiscalQuarter period,
        PeriodSpan span,
        decimal value) =>
        StatementLine.Observe(report, statement, code, period, span, value, ValueBasis.AsFiled, FinancialReportTests.Ids, Read);
}
