using PersonalQuant.Application.Fundamentals;

namespace PersonalQuant.UnitTests.Fundamentals;

/// <summary>
/// Builds filings shaped like the IDS portal's, with invented values.
/// </summary>
internal static class Filings
{
    public static readonly string[] BalanceColumns = ["Số cuối kỳ", "Số đầu kỳ"];

    public static readonly string[] IncomeColumns =
    [
        "Kỳ này Năm nay",
        "Kỳ này Năm trước",
        "Lũy kế từ đầu năm đến cuối kỳ này Năm nay",
        "Lũy kế từ đầu năm đến cuối kỳ này Năm trước",
    ];

    public static ProviderFiling Quarterly(
        int year = 2022,
        int quarter = 1,
        DateOnly? submittedOn = null,
        string name = "Báo cáo tài chính Hợp nhất - Quý",
        string ticker = "FPT",
        string? reference = null,
        IReadOnlyList<ProviderStatementTable>? tables = null) =>
        new(
            reference ?? $"{ticker}-{year}Q{quarter}",
            ticker,
            name,
            $"Báo cáo tài chính quý {quarter} năm {year}",
            submittedOn ?? new DateOnly(year, quarter * 3, 1).AddMonths(1).AddDays(21),
            null,
            year,
            quarter,
            tables ?? NonFinancialTables(),
            "{\"invented\":true}",
            "application/json",
            new DateTimeOffset(2026, 10, 13, 9, 0, 0, TimeSpan.Zero));

    public static ProviderFiling Annual(
        int year = 2021,
        DateOnly? submittedOn = null,
        string name = "Báo cáo tài chính Hợp nhất - Năm",
        int? formQuarter = null,
        IReadOnlyList<ProviderStatementTable>? tables = null) =>
        Quarterly(year, 4, submittedOn ?? new DateOnly(year + 1, 3, 8), name, tables: tables) with
        {
            Reference = $"FPT-{year}FY",
            FiscalQuarter = formQuarter,
        };

    /// <summary>A Circular 200 balance sheet and income statement.</summary>
    public static ProviderStatementTable[] NonFinancialTables(
        string revenue = "1,000",
        string revenueLastYear = "800",
        string revenueToDate = "1,000",
        string revenueToDateLastYear = "800") =>
    [
        Balance(("270", "5,000", "4,500"), ("440", "5,000", "4,500")),
        new("KQKD", IncomeColumns, [Row("10", revenue, revenueLastYear, revenueToDate, revenueToDateLastYear)]),
    ];

    public static ProviderStatementTable Balance(params (string Code, string Closing, string Opening)[] rows) =>
        new("BCDKT", BalanceColumns, [.. rows.Select(row => Row(row.Code, row.Closing, row.Opening))]);

    public static ProviderStatementRow Row(string code, params string[] values) => new($"Line {code}", code, values);
}
