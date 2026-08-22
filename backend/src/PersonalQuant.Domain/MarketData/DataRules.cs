namespace PersonalQuant.Domain.MarketData;

/// <summary>
/// The versions of the rules that produce and check stored data.
/// </summary>
/// <remarks>
/// <para>
/// Every bar records which version of each rule set it passed through. That is
/// the lineage the roadmap asks for, and it exists to answer one question: when
/// a rule changes, which rows were written under the old one?
/// </para>
/// <para>
/// Without it, changing a validation rule leaves a series in which some rows
/// were checked one way and some another, with nothing to say which — so the
/// only safe response to any rule change is to re-validate everything, which
/// for a growing series eventually means never changing a rule.
/// </para>
/// <para>
/// Bump a version when the corresponding rules change in a way that could
/// classify existing data differently. Do not bump for a refactor: the number
/// means "checked by these rules", not "checked by this code".
/// </para>
/// </remarks>
public static class DataRules
{
    /// <summary>
    /// The version of the normalisation rules that turn a provider's rows into
    /// canonical bars.
    /// </summary>
    /// <remarks>
    /// Version 1: timestamps folded to UTC and required to be on a period
    /// boundary, periods outside the requested range dropped, repeats within a
    /// response refused, prices required to be positive and internally
    /// consistent.
    /// </remarks>
    public const int TransformationVersion = 1;

    /// <summary>
    /// The version of the quality rules applied across sessions and against the
    /// trading calendar.
    /// </summary>
    /// <remarks>
    /// Version 1: session-to-session moves checked against the venue's daily
    /// price limit, expected trading days checked for a bar, and bars checked
    /// against the calendar for sessions that should not exist.
    /// </remarks>
    public const int ValidationVersion = 1;

    /// <summary>
    /// The version recorded on data that has not been checked by the rules at
    /// all.
    /// </summary>
    /// <remarks>
    /// Distinct from version 1 on purpose. Rows written before quality
    /// validation existed were not checked and passed nothing; recording them
    /// as version 1 would assert that they had.
    /// </remarks>
    public const int Unvalidated = 0;
}
