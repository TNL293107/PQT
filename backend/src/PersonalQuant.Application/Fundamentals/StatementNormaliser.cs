using System.Globalization;
using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.Fundamentals;

namespace PersonalQuant.Application.Fundamentals;

/// <summary>One value a filing states, placed in its statement, line and period.</summary>
/// <param name="Statement">The statement.</param>
/// <param name="LineCode">The normalised line code.</param>
/// <param name="Period">The quarter it is anchored to.</param>
/// <param name="Span">The stretch of time it covers.</param>
/// <param name="Value">The value.</param>
internal sealed record NormalisedValue(
    FinancialStatement Statement,
    string LineCode,
    FiscalQuarter Period,
    PeriodSpan Span,
    decimal Value);

/// <summary>A filing's form and its values.</summary>
/// <param name="Template">The form the values were filed on.</param>
/// <param name="Values">The values in đồng; empty when the form was left empty.</param>
/// <param name="Unit">The đồng in one unit the issuer typed: 1, or 1,000,000 for a form typed in millions.</param>
internal sealed record NormalisedFiling(FinancialTemplate Template, IReadOnlyList<NormalisedValue> Values, decimal Unit = 1m);

/// <summary>
/// Turns a filing's tables into values, each in its period and span.
/// </summary>
/// <remarks>
/// <para>
/// IDS heads every column in words. A balance sheet has <em>Số cuối kỳ</em>
/// (closing) and <em>Số đầu kỳ</em> (the year's opening, which is last year's
/// close). The income statement has <em>Kỳ này</em> this year and last, and
/// <em>Lũy kế từ đầu năm</em> this year and last. <em>Kỳ này</em> means
/// the quarter on a quarterly report and the whole half-year or year on the
/// others, whose cumulative columns are left blank. A heading this does not
/// know is refused, never guessed: a column read into the wrong period is a
/// silent error in every ratio built on it.
/// </para>
/// <para>
/// A blank cell is no value, which is not zero. A statement whose every cell
/// is zero or blank was not filed: IDS shows an issuer that reports its cash
/// flow by the indirect method an all-zero direct form. Its zeros are not
/// recorded, and a filing with nothing else is recorded without values
/// (ADR-029, verification 4).
/// </para>
/// <para>
/// The form has no unit field, and issuers type in the unit of their own
/// statements: HDB, VIB and LPB in 2022, and VPL throughout, typed millions.
/// The unit is read from charter capital, the one line whose size is known
/// within limits, and every amount is turned into đồng. Earnings per share
/// is typed in đồng by everyone and is left as typed.
/// </para>
/// </remarks>
internal static class StatementNormaliser
{
    /// <summary>The least charter capital, in đồng, the unit check accepts.</summary>
    /// <remarks>
    /// ₫100bn. Every name in the first import has more than ₫1,000bn. A form
    /// typed in millions then reads below ₫100m, and in thousands below
    /// ₫100bn, so exactly one unit fits. A smaller issuer needs this widened
    /// deliberately, with the thousands case thought through again.
    /// </remarks>
    internal const decimal MinimumCharterCapital = 100_000_000_000m;

    /// <summary>The most charter capital, in đồng, the unit check accepts.</summary>
    internal const decimal MaximumCharterCapital = 1_000_000_000_000_000m;

    private static readonly decimal[] Units = [1m, 1_000m, 1_000_000m];
    private const NumberStyles CellStyle =
        NumberStyles.AllowLeadingSign | NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint
            | NumberStyles.AllowParentheses | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite;

    /// <summary>Normalises a filing, or says why it cannot be read.</summary>
    /// <param name="filing">The filing.</param>
    /// <param name="classification">What it is.</param>
    /// <param name="rejection">Why it was refused, when it was.</param>
    /// <returns>The form and values, or null when refused.</returns>
    public static NormalisedFiling? Normalise(
        ProviderFiling filing,
        FilingClassification classification,
        out FilingRejection? rejection)
    {
        ArgumentNullException.ThrowIfNull(filing);
        ArgumentNullException.ThrowIfNull(classification);

        rejection = null;

        // Two tabs holding one table is a capture taken before the portal
        // redrew: the second statement's name over the first's rows. Seen in
        // the 6.5 collection, where five filings' income statements equalled
        // their cash flows. Empty or all-zero tables may coincide honestly.
        var duplicated = filing.Tables
            .Where(table => table.Rows.Any(row => row.Values.Any(cell => !string.IsNullOrWhiteSpace(cell) && cell.Trim() != "0")))
            .GroupBy(table => string.Join('\n', table.Rows.Select(row => row.Code.Trim() + "|" + string.Join('|', row.Values))))
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicated is not null)
        {
            rejection = new FilingRejection(
                filing.Reference,
                filing.Ticker,
                FilingRejectionReason.Unreadable,
                $"Tabs {string.Join(" and ", duplicated.Select(table => table.Name))} hold the same table; it was read before the page redrew.");
            return null;
        }

        var values = new Dictionary<(FinancialStatement, string, FiscalQuarter, PeriodSpan), decimal>();
        var codes = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            foreach (var table in filing.Tables)
            {
                var statement = StatementOf(table.Name);
                var columns = table.ValueColumns.Select(heading => ColumnOf(heading, classification)).ToList();
                var parsed = new List<NormalisedValue>();

                foreach (var row in table.Rows)
                {
                    var code = StatementLine.NormaliseLineCode(row.Code);

                    if (statement == FinancialStatement.BalanceSheet)
                    {
                        codes.Add(code);
                    }

                    for (var column = 0; column < Math.Min(columns.Count, row.Values.Count); column++)
                    {
                        if (Parse(row.Values[column], row.Code, table.Name) is { } value)
                        {
                            var (span, period) = columns[column];
                            parsed.Add(new NormalisedValue(statement, code, period, span, value));
                        }
                    }
                }

                // An all-zero statement was not filed. Keeping its zeros would
                // state that the issuer had no cash flows at all.
                if (parsed.All(value => value.Value == 0m))
                {
                    continue;
                }

                foreach (var value in parsed)
                {
                    var key = (value.Statement, value.LineCode, value.Period, value.Span);

                    if (values.TryGetValue(key, out var held) && held != value.Value)
                    {
                        rejection = new FilingRejection(
                            filing.Reference,
                            filing.Ticker,
                            FilingRejectionReason.ConflictingValues,
                            $"{value.Statement} line {value.LineCode} for {value.Period} {value.Span} is stated as both {held} and {value.Value}.");
                        return null;
                    }

                    values[key] = value.Value;
                }
            }
        }
        catch (DomainValidationException error)
        {
            rejection = new FilingRejection(filing.Reference, filing.Ticker, FilingRejectionReason.Unreadable, error.Message);
            return null;
        }

        var template = TemplateOf(codes, classification.Period, values.Count > 0);

        if (template is null)
        {
            rejection = new FilingRejection(
                filing.Reference,
                filing.Ticker,
                FilingRejectionReason.Unreadable,
                "The balance sheet's codes match neither the Circular 200 form (270, 440) nor the bank form (300, 800).");
            return null;
        }

        if (UnitOf(template.Value, values, classification.Period) is not { } unit)
        {
            rejection = new FilingRejection(
                filing.Reference,
                filing.Ticker,
                FilingRejectionReason.UnknownUnit,
                $"Charter capital is stated as {CharterCapital(template.Value, values, classification.Period)}, which is no "
                    + "plausible amount in đồng, thousands or millions.");
            return null;
        }

        return new NormalisedFiling(
            template.Value,
            [
                .. values.Select(pair => new NormalisedValue(
                    pair.Key.Item1,
                    pair.Key.Item2,
                    pair.Key.Item3,
                    pair.Key.Item4,
                    IsPerShare(template.Value, pair.Key.Item1, pair.Key.Item2) ? pair.Value : pair.Value * unit)),
            ],
            unit);
    }

    /// <summary>Whether a line is stated per share, in đồng whatever the form's unit.</summary>
    /// <param name="template">The form.</param>
    /// <param name="statement">The statement.</param>
    /// <param name="code">The line code.</param>
    /// <returns>True for basic and diluted earnings per share.</returns>
    internal static bool IsPerShare(FinancialTemplate template, FinancialStatement statement, string code) =>
        statement == FinancialStatement.IncomeStatement && template switch
        {
            FinancialTemplate.Bank => code == "23",
            _ => code is "70" or "71",
        };

    private static decimal? UnitOf(
        FinancialTemplate template,
        Dictionary<(FinancialStatement, string, FiscalQuarter, PeriodSpan), decimal> values,
        FiscalQuarter period)
    {
        // A form with no capital, an empty one above all, has nothing to scale
        // or nothing to check it by, and is taken as typed.
        if (CharterCapital(template, values, period) is not { } capital)
        {
            return 1m;
        }

        var fits = Units
            .Where(unit => capital * unit is >= MinimumCharterCapital and <= MaximumCharterCapital)
            .ToList();

        return fits.Count == 1 ? fits[0] : null;
    }

    private static decimal? CharterCapital(
        FinancialTemplate template,
        Dictionary<(FinancialStatement, string, FiscalQuarter, PeriodSpan), decimal> values,
        FiscalQuarter period)
    {
        decimal? Line(string code) =>
            values.TryGetValue((FinancialStatement.BalanceSheet, code, period, PeriodSpan.Instant), out var value) && value > 0m
                ? value
                : null;

        return template == FinancialTemplate.Bank ? Line("411") : Line("411a") ?? Line("411");
    }

    private static FinancialStatement StatementOf(string tab) => tab.Trim().ToUpperInvariant() switch
    {
        "BCDKT" => FinancialStatement.BalanceSheet,
        "KQKD" => FinancialStatement.IncomeStatement,
        "LCTT-TT" => FinancialStatement.CashFlowDirect,
        "LCTT-GT" => FinancialStatement.CashFlowIndirect,
        _ => throw new DomainValidationException($"'{tab}' is not a statement tab this importer reads."),
    };

    private static (PeriodSpan Span, FiscalQuarter Period) ColumnOf(string heading, FilingClassification filing)
    {
        var text = FilingClassifier.Fold(heading);
        var period = filing.Period;
        var thisYear = text.Contains("năm nay", StringComparison.Ordinal);
        var lastYear = text.Contains("năm trước", StringComparison.Ordinal);

        if (text.Contains("cuối kỳ", StringComparison.Ordinal) && !text.Contains("lũy kế", StringComparison.Ordinal))
        {
            return (PeriodSpan.Instant, period);
        }

        if (text.Contains("đầu kỳ", StringComparison.Ordinal) || text.Contains("đầu năm", StringComparison.Ordinal))
        {
            if (!text.Contains("lũy kế", StringComparison.Ordinal))
            {
                return (PeriodSpan.Instant, period.PriorYearEnd);
            }
        }

        if (thisYear == lastYear)
        {
            throw new DomainValidationException($"The column '{heading}' does not say whether it is this year or last.");
        }

        var anchor = thisYear ? period : period.YearEarlier;

        if (text.Contains("lũy kế", StringComparison.Ordinal))
        {
            return (PeriodSpan.YearToDate, anchor);
        }

        if (text.Contains("kỳ này", StringComparison.Ordinal))
        {
            // On a half-year or annual report "this period" is the whole of it.
            var span = filing.Kind == FinancialReportKind.Quarterly ? PeriodSpan.Quarter : PeriodSpan.YearToDate;
            return (span, anchor);
        }

        throw new DomainValidationException($"The column '{heading}' is not one this importer reads.");
    }

    private static decimal? Parse(string? cell, string code, string tab)
    {
        var text = cell?.Trim();

        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (!decimal.TryParse(text, CellStyle, CultureInfo.InvariantCulture, out var value))
        {
            throw new DomainValidationException($"'{text}' on {tab} line {code} is not a number.");
        }

        // Refused here rather than by the line, which would refuse it only
        // after a restatement had already closed the old reading.
        if (decimal.Round(value, StatementLine.ValueScale) != value)
        {
            throw new DomainValidationException(
                $"'{text}' on {tab} line {code} has more than {StatementLine.ValueScale} decimal places.");
        }

        return value;
    }

    private static FinancialTemplate? TemplateOf(HashSet<string> codes, FiscalQuarter period, bool valuesFiled)
    {
        if (codes.Contains("270") && codes.Contains("440"))
        {
            // From fiscal 2026 issuers draw up their statements under Circular
            // 99 and leave IDS's Circular 200 form empty. An empty form says
            // which regime the statements were really made under.
            return period.Year >= 2026 && !valuesFiled
                ? FinancialTemplate.NonFinancialCircular99
                : FinancialTemplate.NonFinancialCircular200;
        }

        return codes.Contains("300") && codes.Contains("800") ? FinancialTemplate.Bank : null;
    }
}
