namespace PersonalQuant.Domain.Fundamentals;

/// <summary>
/// Whose accounts a <see cref="FinancialReport"/> states.
/// </summary>
/// <remarks>
/// A Vietnamese group files both, separately and often on the same day. They
/// are different facts about different entities: the parent's own profit is
/// not the group's, so the two are never mixed in one series.
/// </remarks>
public enum FinancialStatementScope
{
    /// <summary>Not specified. Never valid on a stored report.</summary>
    Unspecified = 0,

    /// <summary>The issuer alone (<em>riêng</em>, or <em>công ty mẹ</em> on IDS).</summary>
    Separate = 1,

    /// <summary>The issuer and its subsidiaries (<em>hợp nhất</em>).</summary>
    Consolidated = 2,
}
