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
/// <param name="Values">The values; empty when the form was left empty.</param>
internal sealed record NormalisedFiling(FinancialTemplate Template, IReadOnlyList<NormalisedValue> Values);

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
/// </remarks>
internal static class StatementNormaliser
{
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

        return new NormalisedFiling(
            template.Value,
            [.. values.Select(pair => new NormalisedValue(pair.Key.Item1, pair.Key.Item2, pair.Key.Item3, pair.Key.Item4, pair.Value))]);
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
