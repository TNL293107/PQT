using PersonalQuant.Domain.Fundamentals;

namespace PersonalQuant.Application.Fundamentals;

/// <summary>
/// The canonical items ratios need, read from one issuer's point-in-time
/// values by the codes of the form they were filed on (ADR-029, verification 4).
/// </summary>
/// <remarks>
/// <para>
/// One family of forms at a time: the bank form alone, or the two non-financial
/// forms together. The latest balance sheet's form decides the family, because
/// code 270 on one form is not code 270 on another. Circular 200 and the
/// vendor's Circular 99 codes never collide (numeric against <c>isa*</c> and
/// <c>bsa*</c>), so a non-bank reads both: its 2025 quarters on Circular 200
/// and its 2026 quarters from a vendor, as one series (ADR-038, QA H1). Where
/// both state a quarter, the filed Circular 200 value is read. An item no form
/// carries is absent, never zero.
/// </para>
/// <para>
/// Circular 200: charter capital 411a (411 when the split is not filed or
/// does not add up),
/// equity 400 less non-controlling interest 429, profit to the parent 61 (60
/// on separate statements, which have no 61). Bank: charter capital 411,
/// equity 500, profit after tax 21 less minority interest 22. Circular 99,
/// from a vendor (ADR-038): profit to the parent isa22, equity bsa78 less
/// non-controlling interest bsa210; no charter capital, and EPS is never read.
/// </para>
/// </remarks>
internal sealed class StatementLines
{
    private readonly Dictionary<(FinancialStatement, string, FiscalQuarter, PeriodSpan), decimal> _values;

    private readonly FinancialStatementScope _scope;

    /// <summary>Quarters with a balance sheet on a vendor's Circular 99 codes.</summary>
    private readonly HashSet<FiscalQuarter> _vendorBalanceSheets = [];

    private StatementLines(
        FinancialTemplate? template,
        FinancialStatementScope scope,
        Dictionary<(FinancialStatement, string, FiscalQuarter, PeriodSpan), decimal> values)
    {
        Template = template;
        _scope = scope;
        _values = values;
    }

    /// <summary>Gets the form of the latest balance sheet, or null when none is known.</summary>
    public FinancialTemplate? Template { get; }

    private bool Bank => Template == FinancialTemplate.Bank;

    /// <summary>Gets whether a value on a form is one these lines read.</summary>
    /// <param name="template">The value's form.</param>
    /// <returns>True for the lines' own family of forms.</returns>
    public bool Reads(FinancialTemplate template) =>
        Template is { } own && (template == FinancialTemplate.Bank) == (own == FinancialTemplate.Bank);

    /// <summary>Takes the values of the family of forms of the latest balance sheet.</summary>
    /// <param name="values">The as-of values.</param>
    /// <param name="scope">Whose accounts they are.</param>
    /// <returns>The lines.</returns>
    public static StatementLines From(IReadOnlyList<FundamentalValue> values, FinancialStatementScope scope)
    {
        // The latest balance sheet's form, or with no balance sheet known yet
        // the latest form of any statement, so a quarter's profit is still
        // read before the first balance sheet arrives.
        var readable = values.Where(value => value.Template is FinancialTemplate.NonFinancialCircular200 or FinancialTemplate.Bank or FinancialTemplate.NonFinancialCircular99).ToList();
        var template = (readable.Any(value => value.Statement == FinancialStatement.BalanceSheet)
                ? readable.Where(value => value.Statement == FinancialStatement.BalanceSheet)
                : readable)
            .OrderBy(value => value.Period)
            .Select(value => (FinancialTemplate?)value.Template)
            .LastOrDefault();

        var lines = new StatementLines(template, scope, []);

        foreach (var value in readable.Where(value => lines.Reads(value.Template)))
        {
            lines._values[(value.Statement, value.LineCode, value.Period, value.Span)] = value.Value;

            if (value.Template == FinancialTemplate.NonFinancialCircular99 && value.Statement == FinancialStatement.BalanceSheet)
            {
                lines._vendorBalanceSheets.Add(value.Period);
            }
        }

        return lines;
    }

    /// <summary>Gets every quarter whose balance sheet states charter capital, oldest first.</summary>
    /// <returns>The quarters.</returns>
    public IReadOnlyList<FiscalQuarter> CapitalQuarters() =>
        [
            .. _values.Keys
                .Where(key => key.Item1 == FinancialStatement.BalanceSheet && key.Item4 == PeriodSpan.Instant)
                .Select(key => key.Item3)
                .Distinct()
                .Where(quarter => CharterCapital(quarter) is not null)
                .Order(),
        ];

    /// <summary>Gets the basic earnings per share a quarter's own column reports.</summary>
    /// <param name="quarter">The quarter.</param>
    /// <returns>The EPS in đồng, or null when not reported.</returns>
    public decimal? ReportedEps(FiscalQuarter quarter) => Template is null
        ? null
        : Income(Bank ? "23" : "70", quarter, PeriodSpan.Quarter);

    /// <summary>Gets the end date of a quarter.</summary>
    /// <param name="quarter">The quarter.</param>
    /// <returns>Its last day.</returns>
    public static DateOnly QuarterEnd(FiscalQuarter quarter) =>
        new DateOnly(quarter.Year, quarter.Quarter * 3, 1).AddMonths(1).AddDays(-1);

    /// <summary>
    /// Gets the latest quarter whose balance sheet states charter capital, or
    /// that a vendor's Circular 99 balance sheet covers, which comes with no
    /// capital.
    /// </summary>
    /// <returns>The quarter, or null.</returns>
    public FiscalQuarter? LatestBalanceSheet() =>
        _values.Keys
            .Where(key => key.Item1 == FinancialStatement.BalanceSheet && key.Item4 == PeriodSpan.Instant
                && (CharterCapital(key.Item3) is not null || _vendorBalanceSheets.Contains(key.Item3)))
            .Select(key => (FiscalQuarter?)key.Item3)
            .OrderBy(quarter => quarter)
            .LastOrDefault();

    /// <summary>
    /// Gets the latest quarter, up to <paramref name="through"/>, whose
    /// balance sheet states charter capital: the share count's basis.
    /// </summary>
    /// <param name="through">The latest quarter considered.</param>
    /// <returns>The quarter, or null.</returns>
    public FiscalQuarter? LatestCapital(FiscalQuarter through) =>
        CapitalQuarters()
            .Select(quarter => (FiscalQuarter?)quarter)
            .LastOrDefault(quarter => quarter!.Value.CompareTo(through) <= 0);

    /// <summary>Gets the common-share capital at a quarter end.</summary>
    /// <remarks>
    /// <para>
    /// Circular 200 splits contributed capital (411) into common shares with
    /// votes (411a) and preferred shares (411b). Common shares are what a
    /// per-share ratio counts, so 411a is read when the split adds up: MSN and
    /// VIC carry preferred shares, and 411 would count them.
    /// </para>
    /// <para>
    /// A split that does not add up is not trusted. VNM's 2025 forms state
    /// 411a as ₫157.9bn under a 411 of ₫20,900bn, with no preferred shares, so
    /// 411 is read. With preferred shares and a split that does not add up,
    /// nothing is read: the common count cannot be told.
    /// </para>
    /// </remarks>
    /// <param name="quarter">The quarter.</param>
    /// <returns>The capital in đồng, or null.</returns>
    public decimal? CharterCapital(FiscalQuarter quarter)
    {
        if (Template is null)
        {
            return null;
        }

        if (Bank)
        {
            return Balance("411", quarter);
        }

        // A vendor's Circular 99 states no charter capital: only the
        // Circular 200 codes are read here.

        var total = Balance("411", quarter);
        var common = Balance("411a", quarter);
        var preferred = Balance("411b", quarter) ?? 0m;

        if (common is { } shares && (total is null || shares + preferred == total))
        {
            return shares;
        }

        return total is { } all && preferred == 0m ? all : null;
    }

    /// <summary>Gets equity attributable to the parent at a quarter end.</summary>
    /// <param name="quarter">The quarter.</param>
    /// <returns>The equity in đồng, or null.</returns>
    public decimal? EquityToParent(FiscalQuarter quarter)
    {
        if (Template is null)
        {
            return null;
        }

        if (Bank)
        {
            return Balance("500", quarter);
        }

        return Balance("400", quarter) is { } equity
            ? equity - (Balance("429", quarter) ?? 0m)
            : VendorEquity(quarter);
    }

    private decimal? VendorEquity(FiscalQuarter quarter) =>
        Template is null || Bank || Balance("bsa78", quarter) is not { } equity
            ? null
            : equity - (Balance("bsa210", quarter) ?? 0m);

    /// <summary>Gets net profit to the parent for one quarter alone.</summary>
    /// <remarks>
    /// The quarter's own figure when a report states it, or else the year to
    /// date less the year to the quarter before, so a fourth quarter that only
    /// the annual report states is still a quarter.
    /// </remarks>
    /// <param name="quarter">The quarter.</param>
    /// <returns>The profit in đồng, or null.</returns>
    public decimal? QuarterProfit(FiscalQuarter quarter)
    {
        if (Profit(quarter, PeriodSpan.Quarter) is { } alone)
        {
            return alone;
        }

        if (Profit(quarter, PeriodSpan.YearToDate) is not { } toDate)
        {
            return null;
        }

        if (quarter.Quarter == 1)
        {
            return toDate;
        }

        return Profit(FiscalQuarter.Create(quarter.Year, quarter.Quarter - 1), PeriodSpan.YearToDate) is { } before
            ? toDate - before
            : null;
    }

    /// <summary>Sums the four latest consecutive quarters of profit to the parent.</summary>
    /// <returns>The sum and the quarters, or a missing value and none.</returns>
    public (RatioValue Profit, IReadOnlyList<FiscalQuarter> Quarters) TrailingProfit()
    {
        var latest = _values.Keys
            .Where(key => key.Item1 == FinancialStatement.IncomeStatement)
            .Select(key => key.Item3)
            .Where(quarter => QuarterProfit(quarter) is not null)
            .Select(quarter => (FiscalQuarter?)quarter)
            .OrderBy(quarter => quarter)
            .LastOrDefault();

        if (latest is not { } end)
        {
            return (RatioValue.Missing(RatioAbsence.NoQuarterlyProfit, "no quarterly profit known"), []);
        }

        var quarters = new List<FiscalQuarter>();
        var sum = 0m;

        for (var back = 3; back >= 0; back--)
        {
            var quarter = Back(end, back);

            if (QuarterProfit(quarter) is not { } profit)
            {
                return (RatioValue.Missing(RatioAbsence.MissingQuarter, $"profit for {quarter} is not known; four consecutive quarters are needed"), []);
            }

            quarters.Add(quarter);
            sum += profit;
        }

        return (RatioValue.Known(sum), quarters);
    }

    private decimal? Profit(FiscalQuarter quarter, PeriodSpan span)
    {
        if (Template is null)
        {
            return null;
        }

        if (Bank)
        {
            return Income("21", quarter, span) is { } afterTax ? afterTax - (Income("22", quarter, span) ?? 0m) : null;
        }

        // A separate statement has no parent line (61): the entity is the
        // parent, so its profit after tax (60) is. A consolidated one without
        // 61 has no figure to the parent, and 60 would include minorities.
        // A vendor's isa22 is read only where no Circular 200 value is held.
        var filed = _scope == FinancialStatementScope.Separate
            ? Income("61", quarter, span) ?? Income("60", quarter, span)
            : Income("61", quarter, span);

        return filed ?? Income("isa22", quarter, span);
    }

    private decimal? Balance(string code, FiscalQuarter quarter) =>
        _values.TryGetValue((FinancialStatement.BalanceSheet, code, quarter, PeriodSpan.Instant), out var value) ? value : null;

    private decimal? Income(string code, FiscalQuarter quarter, PeriodSpan span) =>
        _values.TryGetValue((FinancialStatement.IncomeStatement, code, quarter, span), out var value) ? value : null;

    private static FiscalQuarter Back(FiscalQuarter quarter, int quarters)
    {
        var index = (quarter.Year * 4) + quarter.Quarter - 1 - quarters;
        return FiscalQuarter.Create(index / 4, (index % 4) + 1);
    }
}
