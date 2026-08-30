using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.Universes;

namespace PersonalQuant.Application.Universes;

/// <summary>
/// Reads and records universes and their membership history.
/// </summary>
/// <remarks>
/// <para>
/// There is no delete and no update beyond closing a membership interval.
/// Removing a spell is how a backtest silently starts choosing from a set
/// nobody could have chosen from.
/// </para>
/// <para>
/// Nothing here decides whether an answer is <em>known</em>; that is
/// <see cref="IUniverseCatalog"/>'s, because it depends on the universe's
/// coverage claim rather than on the rows. A repository that returned an empty
/// list for an unsourced year would already have destroyed the distinction.
/// </para>
/// </remarks>
public interface IUniverseRepository
{
    /// <summary>
    /// Finds a universe by its code.
    /// </summary>
    /// <param name="code">The code to look up.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The universe, or null when none is defined under that code.</returns>
    Task<Universe?> FindByCodeAsync(
        UniverseCode code,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the securities whose membership interval contains a date.
    /// </summary>
    /// <remarks>
    /// The as-of read, expressed as the half-open predicate
    /// <c>effective_from &lt;= asOf AND (effective_to IS NULL OR effective_to
    /// &gt; asOf)</c>. It answers only from what is recorded, and says nothing
    /// about whether the recording is complete.
    /// </remarks>
    /// <param name="universeId">The universe to read.</param>
    /// <param name="asOf">The date to read as of.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The securities that belonged on that date.</returns>
    Task<IReadOnlyList<InstrumentId>> ListMembersAsOfAsync(
        UniverseId universeId,
        DateOnly asOf,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a security's spells in a universe, oldest first.
    /// </summary>
    /// <remarks>
    /// Tracked, because closing a spell is an update through the entity and a
    /// re-entry has to be checked against the spells already recorded before it
    /// reaches the exclusion constraint.
    /// </remarks>
    /// <param name="universeId">The universe.</param>
    /// <param name="instrumentId">The security.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Every spell recorded, oldest first.</returns>
    Task<IReadOnlyList<UniverseMembership>> ListSpellsForUpdateAsync(
        UniverseId universeId,
        InstrumentId instrumentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the membership rows a universe has, of any date.
    /// </summary>
    /// <remarks>
    /// Used by the coverage review to tell a universe nobody has sourced from
    /// one whose history is recorded. Cheap, and asked once per universe.
    /// </remarks>
    /// <param name="universeId">The universe.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>How many membership rows exist.</returns>
    Task<int> CountMembershipsAsync(
        UniverseId universeId,
        CancellationToken cancellationToken = default);

    /// <summary>Stages a new universe.</summary>
    /// <param name="universe">The universe to add.</param>
    void Add(Universe universe);

    /// <summary>Stages a new membership spell.</summary>
    /// <param name="membership">The spell to add.</param>
    void Add(UniverseMembership membership);
}
