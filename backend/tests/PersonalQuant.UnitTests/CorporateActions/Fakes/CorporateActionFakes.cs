using PersonalQuant.Application.CorporateActions;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Domain.CorporateActions;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.UnitTests.CorporateActions.Fakes;

/// <summary>
/// An in-memory corporate action record that behaves like the real one.
/// </summary>
/// <remarks>
/// Writes are visible immediately rather than on commit, unlike the instrument
/// master's fake. The adjustment engine reads what it has staged within one
/// run — it removes a superseded factor and adds its replacement — so a store
/// that hid staged writes would make the engine appear to lose them.
/// </remarks>
internal sealed class FakeCorporateActionRepository : ICorporateActionRepository
{
    private readonly List<CorporateAction> _actions = [];
    private readonly List<PriceAdjustment> _adjustments = [];

    /// <summary>Gets every factor currently stored.</summary>
    public IReadOnlyList<PriceAdjustment> Adjustments => _adjustments;

    public Task<IReadOnlyList<CorporateAction>> ListAsync(
        InstrumentId instrumentId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CorporateAction>>(
            [.. _actions
                .Where(action => action.InstrumentId == instrumentId)
                .OrderBy(action => action.ExDate)
                .ThenBy(action => action.Id.Value)]);

    public Task<CorporateAction?> FindAsync(
        InstrumentId instrumentId,
        CorporateActionType type,
        DateOnly exDate,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_actions.Find(action =>
            action.InstrumentId == instrumentId
            && action.Type == type
            && action.ExDate == exDate));

    public Task<IReadOnlyList<PriceAdjustment>> ListAdjustmentsAsync(
        InstrumentId instrumentId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PriceAdjustment>>(
            [.. _adjustments
                .Where(adjustment => adjustment.InstrumentId == instrumentId)
                .OrderBy(adjustment => adjustment.ExDate)]);

    public void Add(CorporateAction action) => _actions.Add(action);

    public void AddAdjustment(PriceAdjustment adjustment) => _adjustments.Add(adjustment);

    public void RemoveAdjustment(PriceAdjustment adjustment) => _adjustments.Remove(adjustment);
}

/// <summary>An in-memory findings store for the explanation path.</summary>
internal sealed class FakeQualityRepository : IDataQualityRepository
{
    private readonly List<DataQualityIssue> _issues = [];

    /// <summary>Gets every finding recorded.</summary>
    public IReadOnlyList<DataQualityIssue> All => _issues;

    /// <summary>Adds a finding as if it were already committed.</summary>
    /// <param name="issue">The finding to seed.</param>
    public void Seed(DataQualityIssue issue) => _issues.Add(issue);

    public Task<IReadOnlyList<DataQualityIssue>> ListAsync(
        InstrumentId instrumentId,
        BarInterval interval,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DataQualityIssue>>(
            [.. _issues.Where(issue =>
                issue.InstrumentId == instrumentId
                && issue.Interval == interval
                && issue.SessionAtUtc >= fromUtc
                && issue.SessionAtUtc < toUtc)]);

    public Task<IReadOnlyList<DataQualityIssue>> ListOpenAsync(
        InstrumentId instrumentId,
        BarInterval interval,
        int limit,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DataQualityIssue>>(
            [.. _issues.Where(issue => issue.IsOpen).Take(limit)]);

    public Task<IReadOnlyDictionary<DataQualityIssueKind, int>> CountOpenByKindAsync(
        InstrumentId instrumentId,
        BarInterval interval,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<DataQualityIssueKind, int>>(
            _issues
                .Where(issue => issue.IsOpen)
                .GroupBy(issue => issue.Kind)
                .ToDictionary(group => group.Key, group => group.Count()));

    public Task<DataQualityIssue?> FindAsync(
        DataQualityIssueId id,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_issues.Find(issue => issue.Id == id));

    public void Add(DataQualityIssue issue) => _issues.Add(issue);
}
