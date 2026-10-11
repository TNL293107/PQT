namespace PersonalQuant.Application.Coverage;

/// <summary>
/// What PQT can say about one dataset for one instrument.
/// </summary>
/// <remarks>
/// <para>
/// Four values and no fifth, because every surface that renders coverage has
/// to render each of them differently (ADR-026). The distinction that matters
/// most is between <see cref="NotHeld"/> and <see cref="NotMeasured"/>: the
/// first is a fact about the instrument, the second is a fact about what PQT
/// knows. A universe that never declared its coverage cannot say an instrument
/// was not a member, and rendering that silence as "not a member" is the
/// survivorship bias U2 removed, moved from the data into the display.
/// </para>
/// <para>
/// Explicit values because the state crosses the wire as a name and is stored
/// nowhere; the numbers only keep a default of zero from meaning anything.
/// </para>
/// </remarks>
public enum CoverageState
{
    /// <summary>PQT holds it, and the extent is stated beside the state.</summary>
    Held = 1,

    /// <summary>PQT collects this kind of data and holds none for this instrument.</summary>
    NotHeld = 2,

    /// <summary>
    /// Whether PQT should hold it cannot be said — a universe whose coverage
    /// was never declared, a span the calendar does not reach.
    /// </summary>
    NotMeasured = 3,

    /// <summary>PQT does not collect this kind of data yet.</summary>
    NotCollected = 4,
}
