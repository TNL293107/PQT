using PersonalQuant.Application.Fundamentals;
using PersonalQuant.Domain.Fundamentals;

namespace PersonalQuant.UnitTests.Fundamentals;

/// <summary>
/// Verifies that a filing's kind and scope are read from its loosely written
/// name, and that a label contradicted by the form or the date is refused
/// (ADR-029, verification 2).
/// </summary>
public sealed class FilingClassifierTests
{
    [Theory]
    [InlineData("Báo cáo tài chính Hợp nhất - Quý", FinancialReportKind.Quarterly, FinancialStatementScope.Consolidated)]
    [InlineData("Riêng - Quý", FinancialReportKind.Quarterly, FinancialStatementScope.Separate)]
    [InlineData("Báo cáo tài chính Mẹ - Quý", FinancialReportKind.Quarterly, FinancialStatementScope.Separate)]
    [InlineData("hợp nhất quý 3/ năm 2022", FinancialReportKind.Quarterly, FinancialStatementScope.Consolidated)]
    public void Quarterly_names_as_IDS_writes_them_are_read(string name, FinancialReportKind kind, FinancialStatementScope scope)
    {
        var classified = FilingClassifier.Classify(Filings.Quarterly(name: name), out var rejection);

        Assert.Null(rejection);
        Assert.Equal((kind, scope, FiscalQuarter.Create(2022, 1)), (classified!.Kind, classified.Scope, classified.Period));
    }

    [Fact]
    public void A_half_year_report_closes_on_the_second_quarter()
    {
        var filing = Filings.Quarterly(quarter: 2, name: "Mẹ - Bán niên năm 2022", submittedOn: new DateOnly(2022, 8, 25)) with
        {
            FiscalQuarter = null,
        };

        var classified = FilingClassifier.Classify(filing, out _);

        Assert.Equal((FinancialReportKind.SemiAnnualReviewed, 2), (classified!.Kind, classified.Period.Quarter));
    }

    [Fact]
    public void An_audited_year_filed_thirty_days_after_year_end_is_refused()
    {
        // VCB's 2023 listing: a consolidated "audited annual 2022" on
        // 30/01/2023, almost certainly the Q4 report under the wrong type.
        var classified = FilingClassifier.Classify(
            Filings.Annual(2022, new DateOnly(2023, 1, 30), "hợp nhất năm 2022"), out var rejection);

        Assert.Null(classified);
        Assert.Equal(FilingRejectionReason.ImplausibleDate, rejection!.Reason);
    }

    [Fact]
    public void A_filing_dated_before_its_period_ended_is_refused()
    {
        _ = FilingClassifier.Classify(Filings.Quarterly(submittedOn: new DateOnly(2022, 3, 30)), out var rejection);

        Assert.Equal(FilingRejectionReason.ImplausibleDate, rejection!.Reason);
    }

    [Fact]
    public void An_annual_name_on_a_form_stating_another_quarter_is_refused()
    {
        _ = FilingClassifier.Classify(Filings.Annual(formQuarter: 3), out var rejection);

        Assert.Equal(FilingRejectionReason.PeriodConflict, rejection!.Reason);
    }

    [Theory]
    [InlineData("Báo cáo tài chính - Quý")]
    [InlineData("BCTC riêng và hợp nhất quý 1")]
    public void A_name_that_does_not_say_whose_accounts_is_refused(string name)
    {
        _ = FilingClassifier.Classify(Filings.Quarterly(name: name), out var rejection);

        Assert.Equal(FilingRejectionReason.Unclassified, rejection!.Reason);
    }

    [Theory]
    [InlineData("BIDV CBTT BCTC RL Quý I.2022", null, FinancialStatementScope.Separate)]
    [InlineData("BIDV CBTT BCTC HN Quý I.2022", null, FinancialStatementScope.Consolidated)]
    [InlineData("BCTC quý 4.2023", "BCTC_rieng_le_Q4.23.pdf", FinancialStatementScope.Separate)]
    [InlineData("BCTC quý 4.2023", "20220426__BID__BCTC_hop_nhat_Q1.pdf", FinancialStatementScope.Consolidated)]
    public void A_name_silent_on_scope_is_read_from_the_abstract_abbreviation_or_document(
        string summary,
        string? document,
        FinancialStatementScope scope)
    {
        var filing = Filings.Quarterly(name: "Báo cáo tài chính quý 1/ năm 2022") with { Summary = summary, Document = document };

        Assert.Equal(scope, FilingClassifier.Classify(filing, out _)!.Scope);
    }

    [Fact]
    public void A_filing_that_says_its_scope_nowhere_is_refused()
    {
        // LPB: "Báo cáo tài chính Quý I năm 2022", BCTC_Q1.2022__CA.pdf.
        var filing = Filings.Quarterly(name: "Báo cáo tài chính quý 1/ năm 2022") with
        {
            Summary = "Báo cáo tài chính Quý I năm 2022",
            Document = "BCTC_Q1.2022__CA.pdf",
        };

        _ = FilingClassifier.Classify(filing, out var rejection);

        Assert.Equal(FilingRejectionReason.Unclassified, rejection!.Reason);
    }

    [Fact]
    public void A_correction_is_recognised_from_its_name() =>
        Assert.True(FilingClassifier.Classify(
            Filings.Quarterly(name: "Đính chính hợp nhất quý 4/ năm 2023", quarter: 4, year: 2023, submittedOn: new DateOnly(2024, 1, 30)),
            out _)!.IsCorrection);

    [Fact]
    public void A_filing_past_its_deadline_is_recorded_as_late()
    {
        var classified = FilingClassifier.Classify(Filings.Quarterly(submittedOn: new DateOnly(2022, 5, 10)), out _);

        Assert.Equal((40, 30), (classified!.Late!.DaysAfterPeriod, classified.Late.DeadlineDays));
    }
}
