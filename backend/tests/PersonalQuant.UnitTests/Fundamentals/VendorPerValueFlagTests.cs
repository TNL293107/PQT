using System.Reflection;
using PersonalQuant.Application.Fundamentals;
using PersonalQuant.Domain.Fundamentals;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.UnitTests.CorporateActions.Fakes;
using PersonalQuant.UnitTests.MarketData.Fakes;

namespace PersonalQuant.UnitTests.Fundamentals;

/// <summary>
/// Acceptance for sprint 11.6, option A: every ratio value states whether a
/// vendor value went into it (manifest schema 7, ADR-038 Consequences), so a
/// report can give the vendor share of each value feature's inputs from the
/// dataset alone (QA v6-M2).
/// </summary>
/// <remarks>
/// <para>
/// <c>BuiltOnVendor</c> stays the row's flag (it backs <c>vendor_rows</c>,
/// judged by quarter). These tests pin the per-value flag, which is narrower:
/// a value is a vendor's only when a vendor value is among its own inputs.
/// </para>
/// <list type="bullet">
/// <item><description>Close and dividend yield: never (bars and corporate actions).</description></item>
/// <item><description>Net profit TTM, EPS, P/E: a vendor stated one of the trailing quarters.</description></item>
/// <item><description>Book value per share, P/B: a vendor stated the balance sheet's equity.</description></item>
/// <item><description>ROE: a vendor stated a trailing quarter, the closing equity or the year-earlier equity.</description></item>
/// </list>
/// <para>
/// The share count is not pinned: a vendor states no charter capital today,
/// and Dev may judge it by the balance-sheet quarter (overstating, never
/// understating).
/// </para>
/// <para>
/// The flag's shape is Dev's choice. <see cref="IsVendor"/> is the one place
/// that reads it: today it looks for a public <see cref="bool"/> property on
/// <see cref="RatioValue"/> whose name contains "Vendor". If Dev picks another
/// shape, change only that helper, not the asserts.
/// </para>
/// </remarks>
public sealed class VendorPerValueFlagTests
{
    private static readonly FiscalQuarter Q2026_1 = FiscalQuarter.Create(2026, 1);
    private static readonly DateOnly November = new(2026, 11, 16);

    [Fact]
    public async Task A_row_read_only_from_filed_Circular_200_values_flags_no_value()
    {
        // 2024Q4 and 2025Q4 equity filed, 2025 profit filed: TTM, balance sheet
        // and opening equity are all as_filed.
        var issuer = new Issuer();
        issuer.FiledYear(2024, capitalAt: 4, equityAt: [4]);
        issuer.FiledYear(2025, capitalAt: 4, equityAt: [4]);

        var ratios = await issuer.ReadAsync(November);

        Assert.NotNull(ratios.ReturnOnEquity.Value);
        Assert.NotNull(ratios.BookValuePerShare.Value);

        foreach (var (name, value) in Values(ratios))
        {
            Assert.False(IsVendor(value), $"{name} was flagged vendor on a row read only from filed values.");
        }
    }

    [Fact]
    public async Task A_vendor_balance_sheet_and_trailing_quarter_flag_every_value_built_on_them()
    {
        // Vendor 2026Q1 states profit and equity: the balance sheet is the
        // vendor's, and the trailing year 2025Q2–2026Q1 holds one vendor quarter.
        var issuer = new Issuer();
        issuer.FiledYear(2025, capitalAt: 4, equityAt: [1, 4]);
        issuer.Vendor(Q2026_1, profit: 300_000_000_000m, equity: 11_000_000_000_000m);

        var ratios = await issuer.ReadAsync(November);

        Assert.Equal(Q2026_1, ratios.BalanceSheet);
        AssertFlags(
            ratios,
            vendor: [nameof(FundamentalRatios.NetProfitTtm), nameof(FundamentalRatios.EpsTtm), nameof(FundamentalRatios.PriceToEarnings),
                nameof(FundamentalRatios.BookValuePerShare), nameof(FundamentalRatios.PriceToBook), nameof(FundamentalRatios.ReturnOnEquity)],
            filed: [nameof(FundamentalRatios.Close), nameof(FundamentalRatios.DividendYield)]);
    }

    [Fact]
    public async Task A_vendor_reached_only_through_a_trailing_quarter_flags_the_earnings_values_not_the_book_ones()
    {
        // The row is BuiltOnVendor, but its balance sheet (2025Q4) is filed:
        // book value and P/B rest on no vendor value. This is what a per-row
        // flag cannot say.
        var issuer = new Issuer();
        issuer.FiledYear(2025, capitalAt: 4, equityAt: [1, 4]);
        issuer.Vendor(Q2026_1, profit: 300_000_000_000m);

        var ratios = await issuer.ReadAsync(November);

        Assert.Equal(FiscalQuarter.Create(2025, 4), ratios.BalanceSheet);
        Assert.Equal(Q2026_1, ratios.ProfitQuarters[^1]);
        Assert.True(ratios.BuiltOnVendor);
        AssertFlags(
            ratios,
            vendor: [nameof(FundamentalRatios.NetProfitTtm), nameof(FundamentalRatios.EpsTtm), nameof(FundamentalRatios.PriceToEarnings),
                nameof(FundamentalRatios.ReturnOnEquity)],
            filed: [nameof(FundamentalRatios.Close), nameof(FundamentalRatios.BookValuePerShare), nameof(FundamentalRatios.PriceToBook),
                nameof(FundamentalRatios.DividendYield)]);
    }

    [Fact]
    public async Task A_vendor_reached_only_through_the_year_earlier_equity_flags_ROE_alone()
    {
        // 2026Q2–2027Q1 filed, 2027Q1 the balance sheet; the only vendor value
        // is 2026Q1's equity, ROE's opening equity.
        var issuer = new Issuer();
        foreach (var period in new[] { FiscalQuarter.Create(2026, 2), FiscalQuarter.Create(2026, 3), FiscalQuarter.Create(2026, 4) })
        {
            issuer.Filed(period, profit: 250_000_000_000m);
        }

        issuer.Filed(FiscalQuarter.Create(2027, 1), profit: 250_000_000_000m, capital: 1_000_000_000_000m, equity: 11_000_000_000_000m);
        issuer.Vendor(Q2026_1, equity: 9_000_000_000_000m);

        var ratios = await issuer.ReadAsync(new DateOnly(2027, 6, 1));

        Assert.Equal(0.1m, ratios.ReturnOnEquity.Value);
        AssertFlags(
            ratios,
            vendor: [nameof(FundamentalRatios.ReturnOnEquity)],
            filed: [nameof(FundamentalRatios.Close), nameof(FundamentalRatios.NetProfitTtm), nameof(FundamentalRatios.EpsTtm),
                nameof(FundamentalRatios.PriceToEarnings), nameof(FundamentalRatios.BookValuePerShare), nameof(FundamentalRatios.PriceToBook),
                nameof(FundamentalRatios.DividendYield)]);
    }

    [Fact]
    public async Task A_vendor_value_fetched_after_the_read_flags_nothing()
    {
        // Read on 1 November, before the 8 November fetch: the vendor quarter
        // is not known, so nothing rests on it.
        var issuer = new Issuer();
        issuer.FiledYear(2025, capitalAt: 4, equityAt: [1, 4]);
        issuer.Vendor(Q2026_1, profit: 300_000_000_000m, equity: 11_000_000_000_000m);

        var ratios = await issuer.ReadAsync(new DateOnly(2026, 11, 1));

        foreach (var (name, value) in Values(ratios))
        {
            Assert.False(IsVendor(value), $"{name} was flagged vendor before the vendor fetch.");
        }
    }

    [Fact]
    public async Task A_trailing_year_missing_a_quarter_is_still_flagged_when_a_vendor_quarter_was_read()
    {
        // QA 11.6 LOW-1: 2025Q4's profit is unknown, so the trailing year is
        // absent, yet its vendor 2026Q1 was read. As for any absent value
        // (NOTE 1, settled 2026-11-17), the flag says what was read.
        var issuer = new Issuer();
        issuer.Filed(FiscalQuarter.Create(2025, 1), profit: 250_000_000_000m, equity: 9_250_000_000_000m);
        issuer.Filed(FiscalQuarter.Create(2025, 2), profit: 250_000_000_000m);
        issuer.Filed(FiscalQuarter.Create(2025, 3), profit: 250_000_000_000m);
        issuer.Filed(FiscalQuarter.Create(2025, 4), capital: 1_000_000_000_000m, equity: 10_000_000_000_000m);
        issuer.Vendor(Q2026_1, profit: 300_000_000_000m);

        var ratios = await issuer.ReadAsync(November);

        Assert.Null(ratios.NetProfitTtm.Value);
        Assert.Equal(RatioAbsence.MissingQuarter, ratios.NetProfitTtm.Reason);
        AssertFlags(
            ratios,
            vendor: [nameof(FundamentalRatios.NetProfitTtm), nameof(FundamentalRatios.EpsTtm), nameof(FundamentalRatios.PriceToEarnings),
                nameof(FundamentalRatios.ReturnOnEquity)],
            filed: [nameof(FundamentalRatios.Close), nameof(FundamentalRatios.BookValuePerShare), nameof(FundamentalRatios.PriceToBook),
                nameof(FundamentalRatios.DividendYield)]);
    }

    private static void AssertFlags(FundamentalRatios ratios, string[] vendor, string[] filed)
    {
        var values = Values(ratios).ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal);

        foreach (var name in vendor)
        {
            Assert.True(IsVendor(values[name]), $"{name} rests on a vendor value but is not flagged.");
        }

        foreach (var name in filed)
        {
            Assert.False(IsVendor(values[name]), $"{name} rests on no vendor value but is flagged.");
        }
    }

    // --- QA + QC, test gap from the PR #26 gate (Tester's survivor c) --------

    [Fact]
    public async Task A_trailing_loss_that_rests_on_a_vendor_quarter_keeps_the_vendor_flag_on_pe()
    {
        // 2025 filed at +250bn a quarter, then a vendor 2026Q1 loss of -2,000bn:
        // the trailing year 2025Q2–2026Q1 sums to -1,250bn, so EPS is negative
        // and P/E is absent as a trailing loss. That absence still rests on the
        // vendor quarter, so it must keep EPS's vendor flag (survivor c).
        var issuer = new Issuer();
        issuer.FiledYear(2025, capitalAt: 4, equityAt: [4]);
        issuer.Vendor(Q2026_1, profit: -2_000_000_000_000m, equity: 9_000_000_000_000m);

        var ratios = await issuer.ReadAsync(November);

        Assert.True(ratios.EpsTtm.Value < 0m, "the trailing year must be a loss for this case");
        Assert.True(IsVendor(ratios.EpsTtm), "EPS rests on a vendor quarter.");
        Assert.Null(ratios.PriceToEarnings.Value);
        Assert.Equal(RatioAbsence.TrailingLoss, ratios.PriceToEarnings.Reason);
        Assert.True(IsVendor(ratios.PriceToEarnings), "a trailing-loss P/E dropped the vendor flag of the EPS it rests on.");
    }

    private static IEnumerable<(string Name, RatioValue Value)> Values(FundamentalRatios ratios) =>
    [
        (nameof(ratios.Close), ratios.Close),
        (nameof(ratios.Shares), ratios.Shares),
        (nameof(ratios.NetProfitTtm), ratios.NetProfitTtm),
        (nameof(ratios.EpsTtm), ratios.EpsTtm),
        (nameof(ratios.BookValuePerShare), ratios.BookValuePerShare),
        (nameof(ratios.PriceToEarnings), ratios.PriceToEarnings),
        (nameof(ratios.PriceToBook), ratios.PriceToBook),
        (nameof(ratios.ReturnOnEquity), ratios.ReturnOnEquity),
        (nameof(ratios.DividendYield), ratios.DividendYield),
    ];

    /// <summary>
    /// The one reader of the per-value flag. Dev chooses its shape; adapt this
    /// helper, never the asserts.
    /// </summary>
    private static bool IsVendor(RatioValue value)
    {
        var flag = typeof(RatioValue)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .SingleOrDefault(property => property.PropertyType == typeof(bool)
                && property.Name.Contains("Vendor", StringComparison.OrdinalIgnoreCase));

        Assert.True(flag is not null, "RatioValue states no per-value vendor flag (11.6 option A, manifest schema 7).");

        return (bool)flag!.GetValue(value)!;
    }

    /// <summary>
    /// One issuer's filed Circular 200 quarters and vendor-filled Circular 99
    /// ones, recorded on 2 May 2026, vendor values fetched on 8 November 2026,
    /// and a close of ₫60,000 on any session read. Every value invented. The
    /// same issuer as FundamentalRatioServiceTests' MixedIssuer (private there).
    /// </summary>
    private sealed class Issuer
    {
        private static readonly DateTimeOffset IssuerRecorded = new(2026, 5, 2, 0, 0, 0, TimeSpan.Zero);
        private static readonly DateTimeOffset Fetched = new(2026, 11, 8, 3, 0, 0, TimeSpan.Zero);
        private static readonly SourceCode Ids = SourceCode.Create("IDS");

        private readonly InstrumentId _instrument = InstrumentId.New();
        private readonly InMemoryFundamentals _fundamentals = new();
        private readonly FakeBarRepository _bars = new();

        public void FiledYear(int year, int capitalAt, int[] equityAt)
        {
            for (var quarter = 1; quarter <= 4; quarter++)
            {
                Filed(
                    FiscalQuarter.Create(year, quarter),
                    profit: 250_000_000_000m,
                    capital: quarter == capitalAt ? 1_000_000_000_000m : null,
                    equity: equityAt.Contains(quarter) ? 9_000_000_000_000m + (quarter * 250_000_000_000m) : null);
            }
        }

        public void Filed(FiscalQuarter period, decimal? profit = null, decimal? capital = null, decimal? equity = null)
        {
            var report = Report(period, FinancialTemplate.NonFinancialCircular200, valuesFiled: true);

            foreach (var (statement, code, span, value) in new (FinancialStatement, string, PeriodSpan, decimal?)[]
            {
                (FinancialStatement.IncomeStatement, "61", PeriodSpan.Quarter, profit),
                (FinancialStatement.BalanceSheet, "411a", PeriodSpan.Instant, capital),
                (FinancialStatement.BalanceSheet, "400", PeriodSpan.Instant, equity),
            })
            {
                if (value is { } stated)
                {
                    _fundamentals.Add(StatementLine.Observe(report, statement, code, period, span, stated, ValueBasis.AsFiled, Ids, report.RecordedAtUtc));
                }
            }
        }

        public void Vendor(FiscalQuarter period, decimal? profit = null, decimal? equity = null)
        {
            var report = Report(period, FinancialTemplate.NonFinancialCircular99, valuesFiled: false);

            foreach (var (statement, code, span, value) in new (FinancialStatement, string, PeriodSpan, decimal?)[]
            {
                (FinancialStatement.IncomeStatement, "isa22", PeriodSpan.Quarter, profit),
                (FinancialStatement.BalanceSheet, "bsa78", PeriodSpan.Instant, equity),
            })
            {
                if (value is { } stated)
                {
                    _fundamentals.Add(StatementLine.Observe(
                        report, statement, code, period, span, stated, ValueBasis.Vendor, SourceCode.Create("VCS"),
                        report.RecordedAtUtc > Fetched ? report.RecordedAtUtc.AddDays(1) : Fetched));
                }
            }
        }

        public Task<FundamentalRatios> ReadAsync(DateOnly session)
        {
            var bar = OhlcvBar.Record(
                _instrument, BarInterval.OneDay, new DateTimeOffset(session.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                Price.Create(60_000m), Price.Create(60_000m), Price.Create(60_000m), Price.Create(60_000m), 1_000, null, Ids, IssuerRecorded);
            _bars.AddRange([bar]);
            _bars.Revisions.Add(BarRevision.Snapshot(bar, IssuerRecorded));

            return new FundamentalRatioService(
                    new FundamentalsReader(_fundamentals), _bars, new FakeCorporateActionRepository(), new FakeCorporateActionCoverageRepository())
                .ReadAsync(_instrument, session, cancellationToken: TestContext.Current.CancellationToken);
        }

        private FinancialReport Report(FiscalQuarter period, FinancialTemplate template, bool valuesFiled)
        {
            var disclosed = StatementLines.QuarterEnd(period).AddDays(25);
            var after = new DateTimeOffset(disclosed.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var report = FinancialReport.Record(
                _instrument, Ids, $"ids-{Guid.NewGuid():N}", FinancialReportKind.Quarterly, FinancialStatementScope.Consolidated,
                template, period, "Báo cáo tài chính", disclosed, null,
                FinancialReportTests.Vietnam, isCorrection: false, valuesFiled, after > IssuerRecorded ? after : IssuerRecorded);
            _fundamentals.Add(report);
            return report;
        }
    }
}
