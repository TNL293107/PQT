namespace PersonalQuant.Application.Universes;

/// <summary>
/// What a universe's membership rows actually span, as opposed to what it
/// claims to know.
/// </summary>
/// <remarks>
/// Aggregated in the database rather than by reading the rows, because a
/// coverage review asks this of every universe and needs three numbers, not a
/// membership history.
/// </remarks>
/// <param name="Count">How many spells are recorded, of any date.</param>
/// <param name="EarliestFrom">
/// The first date any spell begins, or <see langword="null"/> when there are no
/// spells.
/// </param>
/// <param name="LatestEnd">
/// The last date any closed spell ends, or <see langword="null"/> when there
/// are no closed spells. Says nothing about open ones —
/// <paramref name="HasOpenSpell"/> does.
/// </param>
/// <param name="HasOpenSpell">
/// Whether any spell is still running, which reaches past every claim with an
/// end date.
/// </param>
public sealed record UniverseMembershipSpan(
    int Count,
    DateOnly? EarliestFrom,
    DateOnly? LatestEnd,
    bool HasOpenSpell)
{
    /// <summary>Gets the span of a universe nobody has sourced.</summary>
    public static UniverseMembershipSpan Empty { get; } =
        new(Count: 0, EarliestFrom: null, LatestEnd: null, HasOpenSpell: false);

    /// <summary>Gets a value indicating whether any membership is recorded.</summary>
    public bool IsEmpty => Count == 0;
}
