using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.Fundamentals;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.UnitTests.Fundamentals;

/// <summary>
/// Verifies that a disclosed report knows when its values became usable
/// (ADR-029, decision 2) and refuses what cannot be a report.
/// </summary>
public sealed class FinancialReportTests
{
    internal static readonly TimeZoneInfo Vietnam = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
    internal static readonly SourceCode Ids = SourceCode.Create("IDS");
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_report_dated_without_a_time_is_known_from_the_next_local_midnight()
    {
        // FPT's Q1 2022 consolidated report, dated 22/04/2022 on IDS. Midnight
        // in Hanoi is 17:00 UTC the day before.
        var report = Quarterly(new DateOnly(2022, 4, 22), disclosedAtUtc: null);

        Assert.Equal(new DateTimeOffset(2022, 4, 22, 17, 0, 0, TimeSpan.Zero), report.KnownFromUtc);
    }

    [Fact]
    public void A_report_with_a_time_is_known_from_that_instant()
    {
        // HOSE lists to the second: 13/10/2026 17:31:15 local.
        var at = new DateTimeOffset(2026, 10, 13, 10, 31, 15, TimeSpan.Zero);

        var report = Quarterly(new DateOnly(2026, 10, 13), at);

        Assert.Equal(at, report.KnownFromUtc);
    }

    [Fact]
    public void A_time_on_another_local_day_than_the_date_is_refused() =>
        // 18:00 UTC on the 12th is already the 13th in Hanoi.
        Assert.Throws<DomainValidationException>(() => Quarterly(
            new DateOnly(2026, 10, 12), new DateTimeOffset(2026, 10, 12, 18, 0, 0, TimeSpan.Zero)));

    [Theory]
    [InlineData(FinancialReportKind.SemiAnnualReviewed, 3)]
    [InlineData(FinancialReportKind.AnnualAudited, 2)]
    public void A_half_year_or_annual_report_must_close_on_its_own_quarter(FinancialReportKind kind, int quarter) =>
        Assert.Throws<DomainValidationException>(() => Record(kind, FiscalQuarter.Create(2023, quarter), new DateOnly(2024, 3, 8)));

    [Fact]
    public void A_report_cannot_be_recorded_before_it_is_disclosed() =>
        Assert.Throws<DomainValidationException>(() => Quarterly(new DateOnly(2026, 10, 14), disclosedAtUtc: null));

    [Fact]
    public void The_title_and_reference_are_trimmed_and_required()
    {
        var report = FinancialReport.Record(
            InstrumentId.New(), Ids, "  ids-1  ", FinancialReportKind.Quarterly, FinancialStatementScope.Consolidated,
            FinancialTemplate.NonFinancialCircular200, FiscalQuarter.Create(2022, 1), " BCTC Q1 ", new DateOnly(2022, 4, 22),
            null, Vietnam, isCorrection: false, valuesFiled: true, Now);

        Assert.Equal(("ids-1", "BCTC Q1"), (report.SourceReference, report.Title));
        Assert.Throws<DomainValidationException>(() => FinancialReport.Record(
            InstrumentId.New(), Ids, " ", FinancialReportKind.Quarterly, FinancialStatementScope.Consolidated,
            FinancialTemplate.NonFinancialCircular200, FiscalQuarter.Create(2022, 1), "BCTC", new DateOnly(2022, 4, 22),
            null, Vietnam, isCorrection: false, valuesFiled: true, Now));
    }

    internal static FinancialReport Quarterly(
        DateOnly disclosedOn,
        DateTimeOffset? disclosedAtUtc,
        bool valuesFiled = true,
        FiscalQuarter? period = null) =>
        FinancialReport.Record(
            InstrumentId.New(),
            Ids,
            $"ids-{Guid.NewGuid():N}",
            FinancialReportKind.Quarterly,
            FinancialStatementScope.Consolidated,
            FinancialTemplate.NonFinancialCircular200,
            period ?? FiscalQuarter.Create(2022, 1),
            "Báo cáo tài chính hợp nhất",
            disclosedOn,
            disclosedAtUtc,
            Vietnam,
            isCorrection: false,
            valuesFiled,
            Now);

    private static FinancialReport Record(FinancialReportKind kind, FiscalQuarter period, DateOnly disclosedOn) =>
        FinancialReport.Record(
            InstrumentId.New(), Ids, "ids-1", kind, FinancialStatementScope.Consolidated,
            FinancialTemplate.NonFinancialCircular200, period, "BCTC", disclosedOn, null, Vietnam,
            isCorrection: false, valuesFiled: true, Now);
}
