using PersonalQuant.Application.Fundamentals;
using PersonalQuant.Domain.Fundamentals;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.UnitTests.Fundamentals;

/// <summary>
/// Verifies the point-in-time read (sprint 6 item 6.4): a report filed after
/// the instant is absent, a restatement filed after it is absent, and a
/// value read before a vendor rewrote it reads as it stood.
/// </summary>
/// <remarks>
/// The scenario is invented. Fiscal 2022's full-year revenue is stated as
/// 4,000 by the Q4 report on 25 January 2023 and restated as 4,100 by the
/// audited annual report on 8 March 2023.
/// </remarks>
public sealed class FundamentalsReaderTests
{
    private static readonly DateTimeOffset Recorded = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);
    private static readonly FiscalQuarter Fy2022 = FiscalQuarter.Create(2022, 4);
    private static readonly InstrumentId Issuer = InstrumentId.New();

    [Fact]
    public async Task A_report_filed_after_the_instant_is_absent()
    {
        var store = new InMemoryFundamentals();
        Report(store, FinancialReportKind.Quarterly, new DateOnly(2023, 1, 25), ("10", Fy2022, PeriodSpan.YearToDate, 4_000m));

        // The report is dated 25 January, so it is known from midnight in
        // Hanoi: 17:00 UTC that day.
        var before = await Read(store, new DateTimeOffset(2023, 1, 25, 16, 59, 59, TimeSpan.Zero));
        var after = await Read(store, new DateTimeOffset(2023, 1, 25, 17, 0, 0, TimeSpan.Zero));

        Assert.Empty(before.Values);
        Assert.Equal((0, 1), (before.ReportsKnown, before.ReportsWithheld));
        Assert.Equal(4_000m, Assert.Single(after.Values).Value);
    }

    [Fact]
    public async Task A_restatement_filed_after_the_instant_is_absent_and_after_it_is_served_as_restated()
    {
        var store = new InMemoryFundamentals();
        Report(store, FinancialReportKind.Quarterly, new DateOnly(2023, 1, 25), ("10", Fy2022, PeriodSpan.YearToDate, 4_000m));
        Report(store, FinancialReportKind.AnnualAudited, new DateOnly(2023, 3, 8), ("10", Fy2022, PeriodSpan.YearToDate, 4_100m));

        var february = Assert.Single((await Read(store, new DateTimeOffset(2023, 2, 15, 0, 0, 0, TimeSpan.Zero))).Values);
        var april = Assert.Single((await Read(store, new DateTimeOffset(2023, 4, 1, 0, 0, 0, TimeSpan.Zero))).Values);

        Assert.Equal((4_000m, FinancialReportKind.Quarterly, false), (february.Value, february.ReportKind, february.Restated));
        Assert.Equal((4_100m, FinancialReportKind.AnnualAudited, true), (april.Value, april.ReportKind, april.Restated));
    }

    [Fact]
    public async Task A_later_report_restating_nothing_is_not_a_restatement()
    {
        var store = new InMemoryFundamentals();
        Report(store, FinancialReportKind.Quarterly, new DateOnly(2023, 1, 25), ("10", Fy2022, PeriodSpan.YearToDate, 4_000m));
        Report(store, FinancialReportKind.AnnualAudited, new DateOnly(2023, 3, 8), ("10", Fy2022, PeriodSpan.YearToDate, 4_000m));

        var value = Assert.Single((await Read(store, new DateTimeOffset(2023, 4, 1, 0, 0, 0, TimeSpan.Zero))).Values);

        Assert.Equal((FinancialReportKind.AnnualAudited, false), (value.ReportKind, value.Restated));
    }

    [Fact]
    public async Task On_the_same_day_a_correction_outranks_what_it_corrects()
    {
        // VCB filed its Q4 2023 statements and a "(final)" correction on the
        // same day.
        var store = new InMemoryFundamentals();
        Report(store, FinancialReportKind.Quarterly, new DateOnly(2023, 1, 30), isCorrection: true, ("10", Fy2022, PeriodSpan.Quarter, 1_010m));
        Report(store, FinancialReportKind.Quarterly, new DateOnly(2023, 1, 30), isCorrection: false, ("10", Fy2022, PeriodSpan.Quarter, 1_000m));

        var value = Assert.Single((await Read(store, new DateTimeOffset(2023, 2, 1, 0, 0, 0, TimeSpan.Zero))).Values);

        Assert.Equal(1_010m, value.Value);
    }

    [Fact]
    public async Task A_read_reproduced_at_an_earlier_observation_sees_the_value_before_a_vendor_rewrote_it()
    {
        var store = new InMemoryFundamentals();
        var report = Report(store, FinancialReportKind.Quarterly, new DateOnly(2023, 1, 25), ("10", Fy2022, PeriodSpan.YearToDate, 4_000m));
        var rewritten = Recorded.AddDays(10);
        store.Add(store.Lines.Single(line => line.ReportId == report.Id).Restate(4_050m, ValueBasis.Vendor, SourceCode.Create("VCI"), rewritten));
        var instant = new DateTimeOffset(2023, 2, 1, 0, 0, 0, TimeSpan.Zero);

        var then = Assert.Single((await Read(store, instant, rewritten.AddTicks(-1))).Values);
        var now = Assert.Single((await Read(store, instant)).Values);

        Assert.Equal((4_000m, ValueBasis.AsFiled), (then.Value, then.Basis));
        Assert.Equal((4_050m, ValueBasis.Vendor), (now.Value, now.Basis));
    }

    [Fact]
    public async Task A_report_recorded_after_the_observation_instant_is_absent()
    {
        var store = new InMemoryFundamentals();
        Report(store, FinancialReportKind.Quarterly, new DateOnly(2023, 1, 25), ("10", Fy2022, PeriodSpan.YearToDate, 4_000m));

        var read = await Read(store, new DateTimeOffset(2023, 2, 1, 0, 0, 0, TimeSpan.Zero), Recorded.AddTicks(-1));

        Assert.Equal((0, 0), (read.Values.Count, read.ReportsKnown));
    }

    [Fact]
    public async Task Separate_and_consolidated_values_are_never_mixed()
    {
        var store = new InMemoryFundamentals();
        Report(store, FinancialReportKind.Quarterly, new DateOnly(2023, 1, 25), ("10", Fy2022, PeriodSpan.YearToDate, 4_000m));

        var separate = await new FundamentalsReader(store).ReadAsync(
            Issuer, FinancialStatementScope.Separate, new DateTimeOffset(2023, 2, 1, 0, 0, 0, TimeSpan.Zero),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(separate.Values);
    }

    [Fact]
    public async Task A_report_disclosed_without_values_is_listed_and_serves_none()
    {
        var store = new InMemoryFundamentals();
        store.Add(Record(FinancialReportKind.Quarterly, new DateOnly(2026, 4, 24), isCorrection: false, valuesFiled: false, period: FiscalQuarter.Create(2026, 1)));

        var read = await Read(store, new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Empty(read.Values);
        Assert.Single(read.DisclosedWithoutValues);
    }

    private static Task<FundamentalsAsOf> Read(InMemoryFundamentals store, DateTimeOffset knownAsOf, DateTimeOffset? observedAsOf = null) =>
        new FundamentalsReader(store).ReadAsync(
            Issuer, FinancialStatementScope.Consolidated, knownAsOf, observedAsOf, TestContext.Current.CancellationToken);

    private static FinancialReport Report(
        InMemoryFundamentals store,
        FinancialReportKind kind,
        DateOnly disclosedOn,
        params (string Code, FiscalQuarter Period, PeriodSpan Span, decimal Value)[] values) =>
        Report(store, kind, disclosedOn, isCorrection: false, values);

    private static FinancialReport Report(
        InMemoryFundamentals store,
        FinancialReportKind kind,
        DateOnly disclosedOn,
        bool isCorrection,
        params (string Code, FiscalQuarter Period, PeriodSpan Span, decimal Value)[] values)
    {
        var report = Record(kind, disclosedOn, isCorrection, valuesFiled: true, Fy2022);
        store.Add(report);

        foreach (var (code, period, span, value) in values)
        {
            store.Add(StatementLine.Observe(
                report, FinancialStatement.IncomeStatement, code, period, span, value, ValueBasis.AsFiled, FinancialReportTests.Ids, Recorded));
        }

        return report;
    }

    private static FinancialReport Record(
        FinancialReportKind kind,
        DateOnly disclosedOn,
        bool isCorrection,
        bool valuesFiled,
        FiscalQuarter period) =>
        FinancialReport.Record(
            Issuer,
            FinancialReportTests.Ids,
            $"ids-{Guid.NewGuid():N}",
            kind,
            FinancialStatementScope.Consolidated,
            FinancialTemplate.NonFinancialCircular200,
            period,
            "Báo cáo tài chính hợp nhất",
            disclosedOn,
            disclosedAtUtc: null,
            FinancialReportTests.Vietnam,
            isCorrection,
            valuesFiled,
            Recorded);
}
