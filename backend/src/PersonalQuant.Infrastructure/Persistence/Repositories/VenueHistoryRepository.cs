using Microsoft.EntityFrameworkCore;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IVenueHistoryRepository"/>.
/// </summary>
/// <param name="dbContext">The unit of work to read and stage through.</param>
internal sealed class VenueHistoryRepository(PersonalQuantDbContext dbContext) : IVenueHistoryRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<InstrumentVenueSpell>> ListAsync(
        IReadOnlyCollection<InstrumentId> instrumentIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instrumentIds);

        var ids = instrumentIds.Distinct().ToList();

        if (ids.Count == 0)
        {
            return [];
        }

        return await dbContext.InstrumentVenueSpells
            .AsNoTracking()
            .Where(spell => ids.Contains(spell.InstrumentId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Add(InstrumentVenueSpell spell)
    {
        ArgumentNullException.ThrowIfNull(spell);
        dbContext.InstrumentVenueSpells.Add(spell);
    }
}
