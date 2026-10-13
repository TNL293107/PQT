using PersonalQuant.Application.Instruments;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.UnitTests.Instruments.Fakes;

/// <summary>Venue spells held in memory; empty means no instrument ever moved.</summary>
internal sealed class InMemoryVenueHistory : IVenueHistoryRepository
{
    private readonly List<InstrumentVenueSpell> _spells = [];

    public IReadOnlyList<InstrumentVenueSpell> Spells => _spells;

    public Task<IReadOnlyList<InstrumentVenueSpell>> ListAsync(
        IReadOnlyCollection<InstrumentId> instrumentIds,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<InstrumentVenueSpell>>(
            [.. _spells.Where(spell => instrumentIds.Contains(spell.InstrumentId))]);

    public Task<IReadOnlyList<InstrumentVenueSpell>> ListForUpdateAsync(
        InstrumentId instrumentId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<InstrumentVenueSpell>>(
            [.. _spells.Where(spell => spell.InstrumentId == instrumentId)]);

    public void Add(InstrumentVenueSpell spell) => _spells.Add(spell);
}
