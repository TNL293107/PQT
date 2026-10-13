namespace PersonalQuant.Domain.Fundamentals;

/// <summary>
/// Which periodic disclosure a <see cref="FinancialReport"/> is.
/// </summary>
/// <remarks>
/// Circular 96/2020/TT-BTC sets one deadline for each, and the kind fixes the
/// fiscal quarter the report closes on: a half-year report closes on the
/// second and an annual report on the fourth. See ADR-029.
/// </remarks>
public enum FinancialReportKind
{
    /// <summary>Not specified. Never valid on a stored report.</summary>
    Unspecified = 0,

    /// <summary>A quarterly report, reviewed or not.</summary>
    Quarterly = 1,

    /// <summary>The reviewed half-year report.</summary>
    SemiAnnualReviewed = 2,

    /// <summary>The audited annual report.</summary>
    AnnualAudited = 3,
}
