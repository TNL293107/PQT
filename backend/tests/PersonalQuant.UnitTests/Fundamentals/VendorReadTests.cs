using PersonalQuant.Application.Fundamentals;
using PersonalQuant.Domain.Fundamentals;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.UnitTests.Fundamentals;

/// <summary>
/// The point-in-time guard on vendor values (ADR-038): a blank IDS report's
/// vendor line is known from its own fetch, never from the IDS date, and an
/// as_filed reading that arrives later supersedes it. Values are invented.
/// </summary>
public sealed class VendorReadTests
{
    private static readonly DateTimeOffset IdsRecorded = new(2026, 5, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Fetched = new(2026, 11, 8, 3, 0, 0, TimeSpan.Zero);
    private static readonly FiscalQuarter Q1 = FiscalQuarter.Create(2026, 1);
    private static readonly InstrumentId Issuer = InstrumentId.New();
    private static readonly SourceCode Vcs = SourceCode.Create("VCS");

    [Fact]
    public async Task A_read_before_the_fetch_sees_the_blank_report_without_the_vendor_value()
    {
        var (store, _) = BlankWithVendorLine();

        // Long after the IDS filing, one tick before the fetch.
        var read = await Read(store, Fetched.AddTicks(-1));

        Assert.Empty(read.Values);
        Assert.Single(read.DisclosedWithoutValues);
    }

    [Fact]
    public async Task A_read_after_the_fetch_sees_the_vendor_value()
    {
        var (store, _) = BlankWithVendorLine();

        var value = Assert.Single((await Read(store, Fetched)).Values);

        Assert.Equal((1_111m, ValueBasis.Vendor, "isa22"), (value.Value, value.Basis, value.LineCode));
    }

    [Fact]
    public async Task An_as_filed_reading_arriving_later_supersedes_the_vendor_line()
    {
        var (store, vendor) = BlankWithVendorLine();
        var filed = Fetched.AddDays(30);
        store.Add(vendor.Restate(1_111m, ValueBasis.AsFiled, FinancialReportTests.Ids, filed));

        var before = Assert.Single((await Read(store, filed, filed.AddTicks(-1))).Values);
        var after = Assert.Single((await Read(store, filed)).Values);

        Assert.Equal(ValueBasis.Vendor, before.Basis);
        Assert.Equal((1_111m, ValueBasis.AsFiled), (after.Value, after.Basis));
    }

    [Fact]
    public void A_blank_report_takes_only_vendor_lines()
    {
        var report = Blank();

        Assert.Throws<PersonalQuant.Domain.Common.DomainStateException>(() => StatementLine.Observe(
            report, FinancialStatement.IncomeStatement, "isa22", Q1, PeriodSpan.Quarter, 1m, ValueBasis.AsFiled, FinancialReportTests.Ids, Fetched));
    }

    [Theory]
    [InlineData("isa3", "isa3")]
    [InlineData(" BSA210 ", "bsa210")]
    [InlineData("3", "03")]
    [InlineData("isa22", "isa22")]
    public void A_vendor_code_never_collides_with_an_ids_code(string written, string normalised)
    {
        // isa3 stays isa3: it is never read as IDS line 03, nor 03 as isa3.
        Assert.Equal(normalised, StatementLine.NormaliseLineCode(written));
        Assert.NotEqual(StatementLine.NormaliseLineCode("3"), StatementLine.NormaliseLineCode("isa3"));
        Assert.NotEqual(StatementLine.NormaliseLineCode("22"), StatementLine.NormaliseLineCode("isa22"));
    }

    [Theory]
    [InlineData("isa")]
    [InlineData("isa3a")]
    [InlineData("xsa3")]
    [InlineData("bsa1234567")]
    public void A_code_that_is_neither_form_is_refused(string written) =>
        Assert.Throws<PersonalQuant.Domain.Common.DomainValidationException>(() => StatementLine.NormaliseLineCode(written));

    private static (InMemoryFundamentals Store, StatementLine Vendor) BlankWithVendorLine()
    {
        var store = new InMemoryFundamentals();
        var report = Blank();
        var line = StatementLine.Observe(
            report, FinancialStatement.IncomeStatement, "isa22", Q1, PeriodSpan.Quarter, 1_111m, ValueBasis.Vendor, Vcs, Fetched);
        store.Add(report);
        store.Add(line);
        return (store, line);
    }

    private static FinancialReport Blank() =>
        FinancialReport.Record(
            Issuer,
            FinancialReportTests.Ids,
            $"ids-{Guid.NewGuid():N}",
            FinancialReportKind.Quarterly,
            FinancialStatementScope.Consolidated,
            FinancialTemplate.NonFinancialCircular99,
            Q1,
            "Báo cáo tài chính hợp nhất",
            new DateOnly(2026, 4, 28),
            disclosedAtUtc: null,
            FinancialReportTests.Vietnam,
            isCorrection: false,
            valuesFiled: false,
            IdsRecorded);

    private static Task<FundamentalsAsOf> Read(InMemoryFundamentals store, DateTimeOffset knownAsOf, DateTimeOffset? observedAsOf = null) =>
        new FundamentalsReader(store).ReadAsync(
            Issuer, FinancialStatementScope.Consolidated, knownAsOf, observedAsOf, TestContext.Current.CancellationToken);
}
