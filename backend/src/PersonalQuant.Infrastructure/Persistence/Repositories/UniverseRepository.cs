using Microsoft.EntityFrameworkCore;
using PersonalQuant.Application.Universes;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;
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
    public async Task<IReadOnlyList<Universe>> ListAsync(
        CancellationToken cancellationToken = default) =>
        await dbContext.Universes
            .OrderBy(universe => universe.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

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
    public async Task<UniverseMembershipSpan> DescribeMembershipAsync(
        UniverseId universeId,
        CancellationToken cancellationToken = default)
    {
        // One round trip and one pass over the universe's rows. Reading the
        // spells to compute this in memory would make a review's cost grow with
        // the history it is reviewing, which is backwards.
        var span = await dbContext.UniverseMemberships
            .AsNoTracking()
            .Where(membership => membership.UniverseId == universeId)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Count = group.Count(),
                EarliestFrom = group.Min(membership => (DateOnly?)membership.EffectiveFrom),
                LatestEnd = group.Max(membership => membership.EffectiveTo),
                OpenSpells = group.Count(membership => membership.EffectiveTo == null),
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return span is null
            ? UniverseMembershipSpan.Empty
            : new UniverseMembershipSpan(
                span.Count,
                span.EarliestFrom,
                span.LatestEnd,
                span.OpenSpells > 0);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UniverseCoverageFinding>> ListOpenFindingsAsync(
        UniverseId universeId,
        CancellationToken cancellationToken = default) =>
        await dbContext.UniverseCoverageFindings
            .Where(finding =>
                finding.UniverseId == universeId
                && finding.Status == DataQualityIssueStatus.Open)
            .OrderBy(finding => finding.Kind)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

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

    /// <inheritdoc />
    public void Add(UniverseCoverageFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);

        dbContext.UniverseCoverageFindings.Add(finding);
    }
}
