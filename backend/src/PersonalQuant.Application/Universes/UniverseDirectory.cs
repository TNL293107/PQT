using PersonalQuant.Application.Abstractions;
using PersonalQuant.Domain.Universes;

namespace PersonalQuant.Application.Universes;

/// <summary>
/// Lists the universes this system defines, with what each claims to know.
/// </summary>
public interface IUniverseDirectory
{
    /// <summary>
    /// Lists every universe, by code, with today's member count where known.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Every universe, ordered by code, and the date counted as of.</returns>
    Task<UniverseListing> ListAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Every universe, and the date their member counts were read as of.
/// </summary>
/// <param name="AsOf">Today, as the system clock reads it in UTC.</param>
/// <param name="Universes">Every universe, ordered by code.</param>
public sealed record UniverseListing(DateOnly AsOf, IReadOnlyList<UniverseSummary> Universes);

/// <summary>
/// One universe as a listing shows it.
/// </summary>
/// <param name="Code">The short code.</param>
/// <param name="Name">The full name.</param>
/// <param name="Kind">What kind of set it is.</param>
/// <param name="Coverage">
/// The span whose membership it claims to know, or null when it claims nothing.
/// </param>
/// <param name="MemberCount">
/// How many members it has on the listing's date, or null when its coverage
/// does not reach that date. Null is not zero: a universe whose claim ended
/// last year has an unknown membership today, not an empty one.
/// </param>
public sealed record UniverseSummary(
    UniverseCode Code,
    string Name,
    UniverseKind Kind,
    MembershipCoverage? Coverage,
    int? MemberCount);

/// <summary>
/// Default <see cref="IUniverseDirectory"/>.
/// </summary>
/// <remarks>
/// One membership read per universe whose claim covers today. Universes are
/// counted in tens (see <see cref="IUniverseRepository.ListAsync"/>), and the
/// count goes through the same half-open as-of read every other consumer uses,
/// so a listing and a breadth count cannot disagree about who is a member.
/// </remarks>
/// <param name="universes">The universe store.</param>
/// <param name="clock">Today.</param>
internal sealed class UniverseDirectory(IUniverseRepository universes, IClock clock) : IUniverseDirectory
{
    /// <inheritdoc />
    public async Task<UniverseListing> ListAsync(CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var defined = await universes.ListAsync(cancellationToken).ConfigureAwait(false);
        var summaries = new List<UniverseSummary>(defined.Count);

        foreach (var universe in defined)
        {
            int? count = universe.Knows(today)
                ? (await universes
                    .ListMembersAsOfAsync(universe.Id, today, cancellationToken)
                    .ConfigureAwait(false)).Count
                : null;

            summaries.Add(new UniverseSummary(
                universe.Code, universe.Name, universe.Kind, universe.Coverage, count));
        }

        return new UniverseListing(today, summaries);
    }
}
