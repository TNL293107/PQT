using PersonalQuant.Application.Quotes;
using PersonalQuant.Application.Universes;
using PersonalQuant.Domain.CorporateActions;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.Domain.Universes;

namespace PersonalQuant.UnitTests.Breadth;

/// <summary>
/// Universes and their spells, held in memory, answering the half-open reads
/// the real repository answers.
/// </summary>
internal sealed class InMemoryUniverses : IUniverseRepository
{
    private readonly List<Universe> _universes = [];
    private readonly List<UniverseMembership> _spells = [];

    public int MemberReads { get; private set; }

    public Task<IReadOnlyList<Universe>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Universe>>(
            [.. _universes.OrderBy(universe => universe.Code.Value, StringComparer.Ordinal)]);

    public Task<Universe?> FindByCodeAsync(UniverseCode code, CancellationToken cancellationToken = default) =>
        Task.FromResult(_universes.Find(universe => universe.Code == code));

    public Task<IReadOnlyList<InstrumentId>> ListMembersAsOfAsync(
        UniverseId universeId,
        DateOnly asOf,
        CancellationToken cancellationToken = default)
    {
        MemberReads++;

        return Task.FromResult<IReadOnlyList<InstrumentId>>(
            [.. _spells
                .Where(spell => spell.UniverseId == universeId && spell.WasMemberOn(asOf))
                .Select(spell => spell.InstrumentId)]);
    }

    public Task<IReadOnlyList<UniverseSpell>> ListSpellsOverlappingAsync(
        UniverseId universeId,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<UniverseSpell>>(
            [.. _spells
                .Where(spell =>
                    spell.UniverseId == universeId
                    && spell.EffectiveFrom <= toDate
                    && (spell.EffectiveTo is not { } end || end > fromDate))
                .Select(spell => new UniverseSpell(
                    spell.InstrumentId, spell.EffectiveFrom, spell.EffectiveTo, spell.AnnouncedOn))]);

    public Task<IReadOnlyList<UniverseMembership>> ListSpellsForUpdateAsync(
        UniverseId universeId,
        InstrumentId instrumentId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<int> CountMembershipsAsync(UniverseId universeId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<UniverseMembershipSpan> DescribeMembershipAsync(
        UniverseId universeId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<UniverseCoverageFinding>> ListOpenFindingsAsync(
        UniverseId universeId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public void Add(Universe universe) => _universes.Add(universe);

    public void Add(UniverseMembership membership) => _spells.Add(membership);

    public void Add(UniverseCoverageFinding finding) => throw new NotSupportedException();
}

/// <summary>
/// Instruments, closes and factors held in memory, counting the reads so a test
/// can assert the count does not grow with the members.
/// </summary>
internal sealed class FakeQuoteReader : IQuoteReader
{
    private readonly List<QuoteInstrument> _instruments = [];
    private readonly List<SessionClose> _closes = [];
    private readonly List<ExDateFactor> _factors = [];

    public int Reads { get; private set; }

    public void Add(QuoteInstrument instrument) => _instruments.Add(instrument);

    public void Add(SessionClose close) => _closes.Add(close);

    public void Add(ExDateFactor factor) => _factors.Add(factor);

    /// <summary>Equities the venue reads return, with their lifecycle.</summary>
    public List<ListedEquity> Equities { get; } = [];

    public Task<IReadOnlyList<ListedEquity>> ListEquitiesAsync(CancellationToken cancellationToken = default)
    {
        Reads++;
        return Task.FromResult<IReadOnlyList<ListedEquity>>([.. Equities]);
    }

    public Task<IReadOnlySet<InstrumentId>> ListWithDailyBarsAsync(
        IReadOnlyCollection<InstrumentId> instrumentIds,
        CancellationToken cancellationToken = default)
    {
        Reads++;
        return Task.FromResult<IReadOnlySet<InstrumentId>>(
            _closes.Select(close => close.InstrumentId).Where(instrumentIds.Contains).ToHashSet());
    }

    public Task<IReadOnlyList<QuoteInstrument>> ListInstrumentsAsync(
        IReadOnlyCollection<InstrumentId> instrumentIds,
        CancellationToken cancellationToken = default)
    {
        Reads++;
        return Task.FromResult<IReadOnlyList<QuoteInstrument>>(
            [.. _instruments.Where(instrument => instrumentIds.Contains(instrument.InstrumentId))]);
    }

    public Task<IReadOnlyList<SessionClose>> ListDailyClosesAsync(
        IReadOnlyCollection<InstrumentId> instrumentIds,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default)
    {
        Reads++;
        return Task.FromResult<IReadOnlyList<SessionClose>>(
            [.. _closes.Where(close =>
                instrumentIds.Contains(close.InstrumentId)
                && close.Session >= fromDate
                && close.Session <= toDate)]);
    }

    public Task<IReadOnlyList<ExDateFactor>> ListFactorsGoingExAsync(
        IReadOnlyCollection<InstrumentId> instrumentIds,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default)
    {
        Reads++;
        return Task.FromResult<IReadOnlyList<ExDateFactor>>(
            [.. _factors.Where(factor =>
                instrumentIds.Contains(factor.InstrumentId)
                && factor.ExDate >= fromDate
                && factor.ExDate <= toDate)]);
    }
}

/// <summary>Shorthand for the values a quote or breadth test writes.</summary>
internal static class QuoteValues
{
    public static readonly SourceCode Source = SourceCode.Create("TEST");

    public static SessionClose Close(InstrumentId id, DateOnly session, decimal close, long volume = 1_000) =>
        new(id, session, Price.Create(close), volume, Source);

    public static ExDateFactor Factor(InstrumentId id, DateOnly exDate, decimal price) =>
        new(id, exDate, AdjustmentFactor.Create(price, 1m));

    public static QuoteInstrument Instrument(string ticker, ExchangeId venue, AssetType assetType = AssetType.Equity) =>
        new(InstrumentId.New(), Ticker.Create(ticker), venue, assetType);
}
