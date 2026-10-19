using System.Globalization;
using System.Text;
using PersonalQuant.Domain.Fundamentals;

namespace PersonalQuant.Application.Fundamentals;

/// <summary>
/// What a filing is: its kind, scope and period, and whether its date can be
/// believed.
/// </summary>
/// <param name="Kind">Quarterly, half-year or annual.</param>
/// <param name="Scope">Separate or consolidated.</param>
/// <param name="Period">The quarter it closes on.</param>
/// <param name="IsCorrection">Whether it corrects an earlier filing.</param>
/// <param name="Late">Set when the filing missed its deadline.</param>
internal sealed record FilingClassification(
    FinancialReportKind Kind,
    FinancialStatementScope Scope,
    FiscalQuarter Period,
    bool IsCorrection,
    LateFiling? Late);

/// <summary>
/// Reads a filing's kind and scope from its name and checks them against its
/// form and its date (ADR-029, verification 2).
/// </summary>
/// <remarks>
/// <para>
/// IDS names reports loosely: <em>Báo cáo tài chính Hợp nhất - Quý</em>,
/// <em>Riêng - Quý</em>, <em>hợp nhất quý 3/ năm 2022</em>. The words are
/// reliable and the layout is not, so the name is read by its words, checked
/// in an order that keeps <em>quý … năm 2022</em> a quarter.
/// </para>
/// <para>
/// A label is not trusted alone. VCB listed a consolidated "audited annual
/// 2022" thirty days after year end, almost certainly its Q4 report filed
/// under the wrong type. Used as filed, it would date the audited year two
/// months early. So the form's own period must agree with the name, and a
/// date that is impossible or implausible for the kind is refused.
/// </para>
/// <para>
/// The fiscal year is taken to end in December, as it does for every name in
/// the first import. A name whose year ends elsewhere has its filings refused
/// for impossible dates, which is loud. It is not silently misdated.
/// </para>
/// </remarks>
internal static class FilingClassifier
{
    /// <summary>Fewer days than this between a year's end and its audited report is implausible.</summary>
    /// <remarks>
    /// VCB's mislabelled filing came exactly 30 days after year end; the earliest
    /// audited report seen in verification 2 came 67 days after. An audit is
    /// not done in six weeks, so anything sooner is refused, loudly.
    /// </remarks>
    internal const int MinimumAuditDays = 45;

    /// <summary>
    /// Classifies a filing, or says why it cannot be.
    /// </summary>
    /// <param name="filing">The filing.</param>
    /// <param name="rejection">Why it was refused, when it was.</param>
    /// <returns>The classification, or null when refused.</returns>
    public static FilingClassification? Classify(ProviderFiling filing, out FilingRejection? rejection)
    {
        ArgumentNullException.ThrowIfNull(filing);

        rejection = null;
        var name = Fold(filing.ReportName);
        var text = name + " " + Fold(filing.Summary);

        // The name first, then the abstract, then the abstract's
        // abbreviations (BIDV: "BCTC RL", "BCTC HN"), then the document's
        // file name (SHB: "BCTC_rieng_le_Q4.23.pdf"). A filing that says it
        // nowhere is refused; its scope is never guessed from its siblings.
        var scope = ScopeOf(name)
            ?? ScopeOf(Fold(filing.Summary))
            ?? ScopeOfAbbreviation(filing.Summary)
            ?? ScopeOfDocument(filing.Document)
            ?? ScopeOfCombined(text);
        var kind = KindOf(name) ?? KindOf(Fold(filing.Summary));

        if (scope is null || kind is null)
        {
            rejection = Refuse(
                filing,
                FilingRejectionReason.Unclassified,
                $"'{filing.ReportName}' does not say {(scope is null ? "whose accounts it states" : "which periodic report it is")}.");
            return null;
        }

        var quarter = kind switch
        {
            FinancialReportKind.SemiAnnualReviewed => 2,
            FinancialReportKind.AnnualAudited => 4,
            _ => filing.FiscalQuarter ?? 0,
        };

        // A quarterly form names its quarter; a half-year or annual form has
        // no quarter, or names the one it closes on.
        var formAgrees = kind == FinancialReportKind.Quarterly
            ? filing.FiscalQuarter is >= 1 and <= 4
            : filing.FiscalQuarter is null || filing.FiscalQuarter == quarter
                || (kind == FinancialReportKind.SemiAnnualReviewed && filing.FiscalQuarter == 0);

        if (!formAgrees || filing.FiscalYear is < FiscalQuarter.MinYear or > FiscalQuarter.MaxYear)
        {
            rejection = Refuse(
                filing,
                FilingRejectionReason.PeriodConflict,
                $"'{filing.ReportName}' is a {kind} report, but its form states year {filing.FiscalYear}"
                    + $"{(filing.FiscalQuarter is { } q ? $", quarter {q}" : string.Empty)}.");
            return null;
        }

        var period = FiscalQuarter.Create(filing.FiscalYear, quarter);
        var periodEnd = new DateOnly(period.Year, period.Quarter * 3, 1).AddMonths(1).AddDays(-1);
        var days = filing.SubmittedOn.DayNumber - periodEnd.DayNumber;

        if (days <= 0)
        {
            rejection = Refuse(
                filing,
                FilingRejectionReason.ImplausibleDate,
                $"Filed on {filing.SubmittedOn:yyyy-MM-dd}, before {period} ended on {periodEnd:yyyy-MM-dd}.");
            return null;
        }

        if (kind == FinancialReportKind.AnnualAudited && days < MinimumAuditDays)
        {
            rejection = Refuse(
                filing,
                FilingRejectionReason.ImplausibleDate,
                $"An audited annual report filed {days} days after the year ended. Nothing is audited that fast;"
                    + " it is most likely a quarterly report filed under the wrong type.");
            return null;
        }

        var deadline = Deadline(kind.Value);
        var late = days > deadline ? new LateFiling(filing.Reference, filing.Ticker, days, deadline) : null;

        return new FilingClassification(
            kind.Value,
            scope.Value,
            period,
            text.Contains("đính chính", StringComparison.Ordinal),
            late);
    }

    /// <summary>
    /// The latest a report of a kind may be filed, in days after its period.
    /// </summary>
    /// <remarks>
    /// Circular 96/2020/TT-BTC: 30 days for a quarter (the parent-company
    /// limit, so a parent is never wrongly late), 45 for the reviewed half
    /// year, 90 for the audited year.
    /// </remarks>
    /// <param name="kind">The kind.</param>
    /// <returns>The deadline in days.</returns>
    internal static int Deadline(FinancialReportKind kind) => kind switch
    {
        FinancialReportKind.SemiAnnualReviewed => 45,
        FinancialReportKind.AnnualAudited => 90,
        _ => 30,
    };

    /// <summary>Lower-cases and composes Vietnamese text so words compare by meaning.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The folded text.</returns>
    internal static string Fold(string? text) =>
        (text ?? string.Empty).Normalize(NormalizationForm.FormC).ToLower(CultureInfo.InvariantCulture);

    private static FinancialStatementScope? ScopeOf(string text)
    {
        var consolidated = text.Contains("hợp nhất", StringComparison.Ordinal);
        var separate = text.Contains("riêng", StringComparison.Ordinal) || text.Contains("mẹ", StringComparison.Ordinal);

        return (consolidated, separate) switch
        {
            (true, false) => FinancialStatementScope.Consolidated,
            (false, true) => FinancialStatementScope.Separate,
            _ => null,
        };
    }

    private static FinancialStatementScope? ScopeOfCombined(string text)
    {
        // A combined report (BCM: "BCTC TỔNG HỢP QUÝ 3 NĂM 2025") adds the
        // parent's branches and dependent units and consolidates no
        // subsidiary, so it is the separate statement. It is read last, and
        // only when nothing says consolidated: IDS also files ACB's, HDB's,
        // MBB's and SHB's consolidated reports under a "Tổng hợp" type.
        return text.Contains("tổng hợp", StringComparison.Ordinal) && !text.Contains("hợp nhất", StringComparison.Ordinal)
            ? FinancialStatementScope.Separate
            : null;
    }

    private static FinancialStatementScope? ScopeOfAbbreviation(string? summary)
    {
        // Upper-case whole words only: "HN" inside a word, or a lower-case
        // "hn", is not the abbreviation.
        var words = (summary ?? string.Empty).Split(
            [' ', '.', ',', '-', '_', '/', '(', ')', ':'], StringSplitOptions.RemoveEmptyEntries);

        return (words.Contains("HN", StringComparer.Ordinal), words.Contains("RL", StringComparer.Ordinal)) switch
        {
            (true, false) => FinancialStatementScope.Consolidated,
            (false, true) => FinancialStatementScope.Separate,
            _ => null,
        };
    }

    private static FinancialStatementScope? ScopeOfDocument(string? document)
    {
        var name = "_" + string.Concat((document ?? string.Empty).ToLowerInvariant()
            .Select(character => char.IsAsciiLetterOrDigit(character) ? character : '_')) + "_";

        var consolidated = name.Contains("_hop_nhat_", StringComparison.Ordinal)
            || name.Contains("_hopnhat_", StringComparison.Ordinal)
            || name.Contains("_hn_", StringComparison.Ordinal);
        var separate = name.Contains("_rieng_", StringComparison.Ordinal)
            || name.Contains("_rieng_le_", StringComparison.Ordinal)
            || name.Contains("_rl_", StringComparison.Ordinal)
            || name.Contains("_cong_ty_me_", StringComparison.Ordinal);

        return (consolidated, separate) switch
        {
            (true, false) => FinancialStatementScope.Consolidated,
            (false, true) => FinancialStatementScope.Separate,
            _ => null,
        };
    }

    private static FinancialReportKind? KindOf(string text) =>
        text.Contains("bán niên", StringComparison.Ordinal) || text.Contains("giữa niên độ", StringComparison.Ordinal)
            ? FinancialReportKind.SemiAnnualReviewed
            : text.Contains("quý", StringComparison.Ordinal)
                ? FinancialReportKind.Quarterly
                : text.Contains("năm", StringComparison.Ordinal)
                    ? FinancialReportKind.AnnualAudited
                    : null;

    private static FilingRejection Refuse(ProviderFiling filing, FilingRejectionReason reason, string detail) =>
        new(filing.Reference, filing.Ticker, reason, detail);
}
