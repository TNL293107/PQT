using System.Text;
using PersonalQuant.Domain.Fundamentals;

namespace PersonalQuant.Application.Fundamentals;

/// <summary>
/// Finds filings whose form is another filing's, so that the values are
/// recorded once, under the report they belong to.
/// </summary>
/// <remarks>
/// <para>
/// IDS keeps one data form per issuer and period. A parent company that files
/// its separate and its consolidated report gets that one form shown under
/// both: FPT's separate report for 2025Q2 shows the consolidated revenue and a
/// line for profit to the parent, which a separate statement does not have.
/// Checked 2026-10-20 by opening each filing in a fresh browser. Of the 1,602
/// filings of the first import, 680 groups of two or more held one form.
/// </para>
/// <para>
/// Two rules follow. Where a consolidated and a separate report of one period
/// share a form, it is the consolidated one's, and the separate report is
/// recorded without values. Where filings of different periods share a form,
/// it is the earliest period's: DGC's 2022Q3 form repeats its 2022Q2 values,
/// and MBB's 2023 annual form its 2022 one. The later filings are recorded
/// without values. Either way the filing itself, and its date, are kept.
/// </para>
/// <para>
/// Forms are compared cell for cell as IDS showed them, so a form repeated
/// under a later period compares equal, as it would to a reader of the
/// portal. Only forms with values are compared: empty ones coincide honestly.
/// Reports of one scope and period that share a form are left alone: they are
/// the same report filed twice.
/// </para>
/// </remarks>
internal static class SharedForms
{
    /// <summary>Finds the filings whose values are withheld, each with the reason.</summary>
    /// <param name="filings">Every filing of the run, read.</param>
    /// <returns>The withheld filings.</returns>
    public static IReadOnlyDictionary<ProviderFiling, FilingRejection> Find(
        IReadOnlyDictionary<ProviderFiling, FundamentalsImportService.ReadFiling> filings)
    {
        var withheld = new Dictionary<ProviderFiling, FilingRejection>();

        var groups = filings
            .Where(pair => pair.Value is { Classification: not null, Normalised.Values.Count: > 0 })
            .Select(pair => (Filing: pair.Key, pair.Value.Classification!, pair.Value.Normalised!))
            .GroupBy(entry => (Ticker: entry.Filing.Ticker.Trim().ToUpperInvariant(), Form: Fingerprint(entry.Filing)))
            .Where(group => group.Count() > 1);

        foreach (var group in groups)
        {
            var members = group
                .OrderBy(entry => entry.Item2.Period)
                .ThenBy(entry => entry.Item2.Scope == FinancialStatementScope.Consolidated ? 0 : 1)
                .ThenBy(entry => entry.Filing.SubmittedOn)
                .ThenBy(entry => entry.Filing.Reference, StringComparer.Ordinal)
                .ToList();

            var owner = members[0];

            foreach (var (filing, classification, _) in members.Skip(1))
            {
                if (classification.Period != owner.Item2.Period)
                {
                    withheld[filing] = new FilingRejection(
                        filing.Reference,
                        filing.Ticker,
                        FilingRejectionReason.RepeatedForm,
                        $"Its {classification.Period} form repeats {owner.Filing.Reference}'s {owner.Item2.Period} values; "
                            + "recorded without values.");
                }
                else if (owner.Item2.Scope == FinancialStatementScope.Consolidated && classification.Scope == FinancialStatementScope.Separate)
                {
                    withheld[filing] = new FilingRejection(
                        filing.Reference,
                        filing.Ticker,
                        FilingRejectionReason.SharedForm,
                        $"Its form is the consolidated one filed as {owner.Filing.Reference}; IDS shows one form for both. "
                            + "Recorded without values.");
                }
            }
        }

        return withheld;
    }

    private static string Fingerprint(ProviderFiling filing)
    {
        var text = new StringBuilder();

        foreach (var table in filing.Tables.OrderBy(table => table.Name, StringComparer.Ordinal))
        {
            text.Append(table.Name).Append('\n');

            foreach (var row in table.Rows)
            {
                text.Append(row.Code.Trim()).Append('|').AppendJoin('|', row.Values.Select(cell => cell.Trim())).Append('\n');
            }
        }

        return text.ToString();
    }
}
