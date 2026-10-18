using PersonalQuant.Domain.Fundamentals;

namespace PersonalQuant.Application.Fundamentals;

/// <summary>
/// The canonical items ratios need, read from one issuer's point-in-time
/// values by the codes of the form they were filed on (ADR-029, verification 4).
/// </summary>
/// <remarks>
/// <para>
/// One template at a time: the latest balance sheet's form decides which codes
/// are read, because code 270 on one form is not code 270 on another. An item
/// the form does not carry is absent, never zero.
/// </para>
/// <para>
/// Circular 200: charter capital 411a (411 when the split is not filed),
/// equity 400 less non-controlling interest 429, profit to the parent 61 (60
/// on separate statements, which have no 61). Bank: charter capital 411,
/// equity 500, profit after tax 21 less minority interest 22.
/// </para>
/// </remarks>
internal sealed class StatementLines
{
    private readonly Dictionary<(FinancialStatement, string, FiscalQuarter, PeriodSpan), decimal> _values;

    private readonly FinancialStatementScope _scope;

    private StatementLines(
        FinancialTemplate? template,
        FinancialStatementScope scope,
        Dictionary<(FinancialStatement, string, FiscalQuarter, PeriodSpan), decimal> values)
    {
        Template = template;
        _scope = scope;
        _values = values;
    }

    /// <summary>Gets the form the values are read on, or null when none is known.</summary>
    public FinancialTemplate? Template { get; }

    /// <summary>Takes the values of the template of the latest balance sheet.</summary>
    /// <param name="values">The as-of values.</param>
    /// <param name="scope">Whose accounts they are.</param>
    /// <returns>The lines.</returns>
    public static StatementLines From(IReadOnlyList<FundamentalValue> values, FinancialStatementScope scope)
    {
        // The latest balance sheet's form, or with no balance sheet known yet
        // the latest form of any statement, so a quarter's profit is still
        // read before the first balance sheet arrives.
        var readable = values.Where(value => value.Template is FinancialTemplate.NonFinancialCircular200 or FinancialTemplate.Bank).ToList();
        var template = (readable.Any(value => value.Statement == FinancialStatement.BalanceSheet)
                ? readable.Where(value => value.Statement == FinancialStatement.BalanceSheet)
                : readable)
            .OrderBy(value => value.Period)
            .Select(value => (FinancialTemplate?)value.Template)
            .LastOrDefault();

        return new StatementLines(
            template,
            scope,
            values
                .Where(value => value.Template == template)
                .ToDictionary(value => (value.Statement, value.LineCode, value.Period, value.Span), value => value.Value));
    }

    /// <summary>Gets the latest quarter whose balance sheet states charter capital.</summary>
    /// <returns>The quarter, or null.</returns>
    public FiscalQuarter? LatestBalanceSheet() =>
        _values.Keys
            .Where(key => key.Item1 == FinancialStatement.BalanceSheet && key.Item4 == PeriodSpan.Instant && CharterCapital(key.Item3) is not null)
            .Select(key => (FiscalQuarter?)key.Item3)
            .OrderBy(quarter => quarter)
            .LastOrDefault();

    /// <summary>Gets the charter capital at a quarter end.</summary>
    /// <param name="quarter">The quarter.</param>
    /// <returns>The capital in đồng, or null.</returns>
    public decimal? CharterCapital(FiscalQuarter quarter) => Template switch
    {
        FinancialTemplate.NonFinancialCircular200 => Balance("411a", quarter) ?? Balance("411", quarter),
        FinancialTemplate.Bank => Balance("411", quarter),
        _ => null,
    };

    /// <summary>Gets equity attributable to the parent at a quarter end.</summary>
    /// <param name="quarter">The quarter.</param>
    /// <returns>The equity in đồng, or null.</returns>
    public decimal? EquityToParent(FiscalQuarter quarter) => Template switch
    {
        FinancialTemplate.NonFinancialCircular200 => Balance("400", quarter) is { } equity ? equity - (Balance("429", quarter) ?? 0m) : null,
        FinancialTemplate.Bank => Balance("500", quarter),
        _ => null,
    };

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
            return (RatioValue.Missing("no quarterly profit known"), []);
        }

        var quarters = new List<FiscalQuarter>();
        var sum = 0m;

        for (var back = 3; back >= 0; back--)
        {
            var quarter = Back(end, back);

            if (QuarterProfit(quarter) is not { } profit)
            {
                return (RatioValue.Missing($"profit for {quarter} is not known; four consecutive quarters are needed"), []);
            }

            quarters.Add(quarter);
            sum += profit;
        }

        return (RatioValue.Known(sum), quarters);
    }

    private decimal? Profit(FiscalQuarter quarter, PeriodSpan span) => Template switch
    {
        // A separate statement has no parent line (61): the entity is the
        // parent, so its profit after tax (60) is. A consolidated one without
        // 61 has no figure to the parent, and 60 would include minorities.
        FinancialTemplate.NonFinancialCircular200 => _scope == FinancialStatementScope.Separate
            ? Income("61", quarter, span) ?? Income("60", quarter, span)
            : Income("61", quarter, span),
        FinancialTemplate.Bank => Income("21", quarter, span) is { } afterTax ? afterTax - (Income("22", quarter, span) ?? 0m) : null,
        _ => null,
    };

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
