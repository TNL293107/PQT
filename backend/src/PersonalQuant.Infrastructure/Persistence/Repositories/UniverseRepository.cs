using Microsoft.EntityFrameworkCore;
using PersonalQuant.Application.Universes;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.Universes;

namespace PersonalQuant.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IUniverseRepository"/>.
/// </summary>
/// <remarks>
/// The as-of read is the half-open predicate and nothing else. There is no
/// fallback to the current constituent set when a date has no rows, in the same
/// way the point-in-time bar read has no fallback to today's price: the absence
/// is the answer, and filling it in from what is true now is the bias both
/// tables exist to prevent.
/// </remarks>
/// <param name="dbContext">The unit of work to read and stage through.</param>
internal sealed class UniverseRepository(PersonalQuantDbContext dbContext) : IUniverseRepository
{
    /// <inheritdoc />
    public Task<Universe?> FindByCodeAsync(
        UniverseCode code,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(code);

        // Compared as the value object, which the converter turns into the
        // stored string, so the unique index on code answers this rather than a
        // scan.
        return dbContext.Universes
            .FirstOrDefaultAsync(universe => universe.Code == code, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InstrumentId>> ListMembersAsOfAsync(
        UniverseId universeId,
        DateOnly asOf,
        CancellationToken cancellationToken = default) =>
        await dbContext.UniverseMemberships
            .AsNoTracking()
            .Where(membership =>
                membership.UniverseId == universeId
                // The half-open interval. Inclusive on the date the security
                // joined, exclusive on the date it left, so a review that
                // swaps one name for another counts each on exactly one side
                // of the review date.
                && membership.EffectiveFrom <= asOf
                && (membership.EffectiveTo == null || membership.EffectiveTo > asOf))
            .Select(membership => membership.InstrumentId)
            // Ordered so that a constituent set hashes to the same manifest
            // entry however the rows came back.
            .OrderBy(instrumentId => instrumentId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<UniverseMembership>> ListSpellsForUpdateAsync(
        UniverseId universeId,
        InstrumentId instrumentId,
        CancellationToken cancellationToken = default) =>
        await dbContext.UniverseMemberships
            .Where(membership =>
                membership.UniverseId == universeId
                && membership.InstrumentId == instrumentId)
            .OrderBy(membership => membership.EffectiveFrom)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public Task<int> CountMembershipsAsync(
        UniverseId universeId,
        CancellationToken cancellationToken = default) =>
        dbContext.UniverseMemberships
            .CountAsync(membership => membership.UniverseId == universeId, cancellationToken);

    /// <inheritdoc />
    public void Add(Universe universe)
    {
        ArgumentNullException.ThrowIfNull(universe);

        dbContext.Universes.Add(universe);
    }

    /// <inheritdoc />
    public void Add(UniverseMembership membership)
    {
        ArgumentNullException.ThrowIfNull(membership);

        dbContext.UniverseMemberships.Add(membership);
    }
}
