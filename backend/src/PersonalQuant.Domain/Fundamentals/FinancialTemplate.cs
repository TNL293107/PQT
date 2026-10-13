namespace PersonalQuant.Domain.Fundamentals;

/// <summary>
/// The statement form a report is drawn up on, which decides what each line
/// code means.
/// </summary>
/// <remarks>
/// <para>
/// Code 270 is total assets on the Circular 200 form and nothing at all on the
/// bank form, where total assets is 300. A line code is therefore only
/// meaningful together with its template, and the template is recorded on the
/// report rather than inferred from the issuer, because an issuer's form
/// changes: Circular 99/2025 replaces Circular 200 from fiscal 2026.
/// </para>
/// <para>
/// ADR-029's first import reads only <see cref="NonFinancialCircular200"/> and
/// <see cref="Bank"/>. The others are named so that a report on them can be
/// recorded as disclosed without its values being misread.
/// </para>
/// </remarks>
public enum FinancialTemplate
{
    /// <summary>Not specified. Never valid on a stored report.</summary>
    Unspecified = 0,

    /// <summary>The enterprise form of Circular 200/2014/TT-BTC, used to fiscal 2025.</summary>
    NonFinancialCircular200 = 1,

    /// <summary>The enterprise form of Circular 99/2025/TT-BTC, from fiscal 2026.</summary>
    NonFinancialCircular99 = 2,

    /// <summary>The credit-institution form.</summary>
    Bank = 3,

    /// <summary>The securities-firm form.</summary>
    SecuritiesFirm = 4,

    /// <summary>The insurer form.</summary>
    Insurer = 5,
}
