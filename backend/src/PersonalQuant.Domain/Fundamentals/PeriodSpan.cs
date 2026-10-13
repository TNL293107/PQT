namespace PersonalQuant.Domain.Fundamentals;

/// <summary>
/// What stretch of time a statement value covers, ending at a fiscal quarter.
/// </summary>
/// <remarks>
/// A Q3 income statement states two different facts on one line: the third
/// quarter alone, and the nine months to its end. They are not derivable from
/// each other once a restatement lands in one and not the other, so both are
/// stored, each with its own span.
/// </remarks>
public enum PeriodSpan
{
    /// <summary>Not specified. Never valid on a stored line.</summary>
    Unspecified = 0,

    /// <summary>A balance at the end of the quarter.</summary>
    Instant = 1,

    /// <summary>The three months ending at the quarter.</summary>
    Quarter = 2,

    /// <summary>The fiscal year to the end of the quarter. At the fourth quarter, the whole year.</summary>
    YearToDate = 3,
}
