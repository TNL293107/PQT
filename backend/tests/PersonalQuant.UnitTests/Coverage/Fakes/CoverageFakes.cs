using PersonalQuant.Application.Coverage;
using PersonalQuant.Application.Universes;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.Domain.Universes;

namespace PersonalQuant.UnitTests.Coverage.Fakes;

/// <summary>
/// The coverage reads, computed over in-memory rows the way the database
/// computes them.
/// </summary>
/// <remarks>
/// Rows rather than canned answers, so the grouping the real repository does in
/// SQL — by resolution and source, newest attempt per resolution — is the
/// thing the service is tested against, not a summary a test wrote by hand.
/// </remarks>
internal sealed class FakeCoverageRepository : ICoverageRepository
{
    private readonly List<(InstrumentId Instrument, BarInterval Interval, SourceCode Source, DateTimeOffset OpenedAtUtc)> _bars = [];
    private readonly List<(InstrumentId Instrument, BarInterval Interval)> _openFindings = [];
    private readonly List<(InstrumentId Instrument, IngestionAttempt Attempt)> _attempts = [];
    private readonly List<(InstrumentId Instrument, InstrumentUniverseSpell Spell)> _spells = [];
    private readonly Dictionary<InstrumentId, CorporateActionExtent> _actions = [];

    /// <summary>Gets how many times the session count was asked for.</summary>
    public int SessionCounts { get; private set; }

    /// <summary>Holds one bar per day of a range, inclusive.</summary>
    public void HoldDaily(InstrumentId instrument, string source, params DateOnly[] sessions)
    {
        foreach (var session in sessions)
        {
            _bars.Add((
                instrument,
                BarInterval.OneDay,
                SourceCode.Create(source),
                new DateTimeOffset(session.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)));
        }
    }

    /// <summary>Holds one bar at an instant.</summary>
    public void Hold(InstrumentId instrument, BarInterval interval, string source, DateTimeOffset openedAtUtc) =>
        _bars.Add((instrument, interval, SourceCode.Create(source), openedAtUtc));

    /// <summary>Records an open finding.</summary>
    public void Find(InstrumentId instrument, BarInterval interval) =>
        _openFindings.Add((instrument, interval));

    /// <summary>Records an ingestion attempt.</summary>
    public void Attempt(InstrumentId instrument, IngestionAttempt attempt) =>
        _attempts.Add((instrument, attempt));

    /// <summary>Records a spell.</summary>
    public void Spell(InstrumentId instrument, UniverseId universe, UniverseSpell spell) =>
        _spells.Add((instrument, new InstrumentUniverseSpell(universe, spell)));

    /// <summary>Records the corporate action summary.</summary>
    public void Actions(InstrumentId instrument, CorporateActionExtent extent) =>
        _actions[instrument] = extent;

    public Task<IReadOnlyList<BarSeriesExtent>> SummariseBarsAsync(
        InstrumentId instrumentId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<BarSeriesExtent>>(
            [.. _bars
                .Where(bar => bar.Instrument == instrumentId)
                .GroupBy(bar => (bar.Interval, bar.Source.Value))
                .Select(group => new BarSeriesExtent(
                    group.Key.Interval,
                    group.First().Source,
                    group.Count(),
                    group.Min(bar => bar.OpenedAtUtc),
                    group.Max(bar => bar.OpenedAtUtc)))]);

    public Task<int> CountDailyBarsOnAsync(
        InstrumentId instrumentId,
        IReadOnlyCollection<DateOnly> sessions,
        CancellationToken cancellationToken = default)
    {
        SessionCounts++;

        return Task.FromResult(_bars.Count(bar =>
            bar.Instrument == instrumentId
            && bar.Interval == BarInterval.OneDay
            && sessions.Contains(DateOnly.FromDateTime(bar.OpenedAtUtc.UtcDateTime))));
    }

    public Task<IReadOnlyDictionary<BarInterval, int>> CountOpenFindingsAsync(
        InstrumentId instrumentId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<BarInterval, int>>(
            _openFindings
                .Where(finding => finding.Instrument == instrumentId)
                .GroupBy(finding => finding.Interval)
                .ToDictionary(group => group.Key, group => group.Count()));

    public Task<IReadOnlyList<IngestionAttempt>> ListLatestAttemptsAsync(
        InstrumentId instrumentId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<IngestionAttempt>>(
            [.. _attempts
                .Where(entry => entry.Instrument == instrumentId)
                .Select(entry => entry.Attempt)
                .GroupBy(attempt => attempt.Interval)
                .Select(group => group.MaxBy(attempt => attempt.StartedAtUtc)!)]);

    public Task<CorporateActionExtent> SummariseCorporateActionsAsync(
        InstrumentId instrumentId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_actions.GetValueOrDefault(instrumentId, CorporateActionExtent.None));

    public Task<IReadOnlyList<InstrumentUniverseSpell>> ListUniverseSpellsAsync(
        InstrumentId instrumentId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<InstrumentUniverseSpell>>(
            [.. _spells.Where(entry => entry.Instrument == instrumentId).Select(entry => entry.Spell)]);
}

/// <summary>
/// A universe repository that only lists, which is all coverage asks of it.
/// </summary>
internal sealed class ListedUniverses : IUniverseRepository
{
    private readonly List<Universe> _universes = [];

    /// <summary>Defines a universe, optionally declaring the window it claims to know.</summary>
    public Universe Define(string code, DateOnly? from = null, DateOnly? until = null)
    {
        var occurredAtUtc = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        var source = SourceCode.Create("TEST");

        var universe = Universe.Define(
            UniverseId.New(),
            UniverseCode.Create(code),
            $"{code} Index",
            UniverseKind.Index,
            source,
            occurredAtUtc);

        if (from is { } start)
        {
            universe.DeclareCoverage(MembershipCoverage.Create(start, until), source, occurredAtUtc);
        }

        _universes.Add(universe);

        return universe;
    }

    public Task<IReadOnlyList<Universe>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Universe>>([.. _universes]);

    public Task<Universe?> FindByCodeAsync(UniverseCode code, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Coverage only lists universes.");

    public Task<IReadOnlyList<InstrumentId>> ListMembersAsOfAsync(
        UniverseId universeId,
        DateOnly asOf,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Coverage only lists universes.");

    public Task<IReadOnlyList<UniverseSpell>> ListSpellsOverlappingAsync(
        UniverseId universeId,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Coverage only lists universes.");

    public Task<IReadOnlyList<UniverseMembership>> ListSpellsForUpdateAsync(
        UniverseId universeId,
        InstrumentId instrumentId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Coverage only lists universes.");

    public Task<int> CountMembershipsAsync(UniverseId universeId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Coverage only lists universes.");

    public Task<UniverseMembershipSpan> DescribeMembershipAsync(
        UniverseId universeId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Coverage only lists universes.");

    public Task<IReadOnlyList<UniverseCoverageFinding>> ListOpenFindingsAsync(
        UniverseId universeId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Coverage only lists universes.");

    public void Add(Universe universe) =>
        throw new NotSupportedException("Coverage writes nothing.");

    public void Add(UniverseMembership membership) =>
        throw new NotSupportedException("Coverage writes nothing.");

    public void Add(UniverseCoverageFinding finding) =>
        throw new NotSupportedException("Coverage writes nothing.");
}
