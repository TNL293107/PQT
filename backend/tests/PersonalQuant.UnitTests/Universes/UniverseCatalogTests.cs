using PersonalQuant.Application.Universes;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.Domain.Universes;

namespace PersonalQuant.UnitTests.Universes;

/// <summary>
/// Verifies that a constituent read never lets an unsourced date look like an
/// empty market.
/// </summary>
/// <remarks>
/// These are the survivorship tests. Every one of them is a case where the
/// wrong answer is silent: a list that is empty for a reason the caller cannot
/// see, and a backtest that reports no positions rather than an error.
/// </remarks>
public sealed class UniverseCatalogTests
{
    private static readonly UniverseCode Vn30 = UniverseCode.Create("VN30");
    private static readonly SourceCode Source = SourceCode.Create("TEST");
    private static readonly DateTimeOffset DefinedAt = new(2026, 8, 30, 3, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Sourced = new(2024, 1, 2);
    private static readonly DateOnly Unsourced = new(2018, 6, 1);

    [Fact]
    public async Task A_universe_nobody_has_defined_is_unknown()
    {
        var catalog = new UniverseCatalog(new FakeUniverseRepository(universe: null));

        var result = await catalog.ConstituentsAsOfAsync(
            Vn30,
            Sourced,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsKnown);
        Assert.Equal(UniverseUnknownReason.NoSuchUniverse, result.UnknownReason);
    }

    [Fact]
    public async Task A_universe_that_claims_nothing_is_unknown_on_every_date()
    {
        // The state a universe is in from the moment it is defined until
        // somebody sources its history. It has no rows, and without the claim
        // that would read as an index with no constituents.
        var repository = new FakeUniverseRepository(Define());

        var catalog = new UniverseCatalog(repository);
        var result = await catalog.ConstituentsAsOfAsync(
            Vn30,
            Sourced,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsKnown);
        Assert.Equal(UniverseUnknownReason.NoCoverageDeclared, result.UnknownReason);
    }

    [Fact]
    public async Task A_date_before_the_sourced_history_is_unknown_rather_than_empty()
    {
        // The dangerous case, stated plainly: VN30 sourced from 2024, asked for
        // 2018. There are no rows for 2018 and there is no index without
        // constituents; the honest answer is that nobody recorded it.
        var universe = Define();
        universe.DeclareCoverage(
            MembershipCoverage.Create(Sourced, until: null),
            Source,
            DefinedAt);
        var catalog = new UniverseCatalog(new FakeUniverseRepository(universe));

        var result = await catalog.ConstituentsAsOfAsync(
            Vn30,
            Unsourced,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsKnown);
        Assert.Equal(UniverseUnknownReason.OutsideCoverage, result.UnknownReason);
    }

    [Fact]
    public async Task An_unknown_answer_has_no_member_list_to_read()
    {
        // Not a convenience. If an unknown result exposed an empty list, every
        // caller that forgot to check would get the survivorship bug back, and
        // it would look like working code.
        var catalog = new UniverseCatalog(new FakeUniverseRepository(Define()));

        var result = await catalog.ConstituentsAsOfAsync(
            Vn30,
            Sourced,
            TestContext.Current.CancellationToken);

        var error = Assert.Throws<InvalidOperationException>(() => result.Members);
        Assert.Contains("not known", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_covered_date_returns_the_members_recorded_for_it()
    {
        var universe = Define();
        universe.DeclareCoverage(
            MembershipCoverage.Create(Sourced, until: null),
            Source,
            DefinedAt);
        var member = InstrumentId.New();
        var repository = new FakeUniverseRepository(universe, [member]);

        var result = await new UniverseCatalog(repository)
            .ConstituentsAsOfAsync(Vn30, Sourced, TestContext.Current.CancellationToken);

        Assert.True(result.IsKnown);
        Assert.Equal(member, Assert.Single(result.Members));
        Assert.Equal(Sourced, result.AsOf);
    }

    [Fact]
    public async Task A_covered_date_with_no_members_is_known_and_empty()
    {
        // The other side of the distinction, and the reason the coverage claim
        // is consulted first: once a date is covered, an empty set is a fact
        // about the market rather than an absence of data.
        var universe = Define();
        universe.DeclareCoverage(
            MembershipCoverage.Create(Sourced, until: null),
            Source,
            DefinedAt);
        var repository = new FakeUniverseRepository(universe, []);

        var result = await new UniverseCatalog(repository)
            .ConstituentsAsOfAsync(Vn30, Sourced, TestContext.Current.CancellationToken);

        Assert.True(result.IsKnown);
        Assert.Empty(result.Members);
    }

    [Fact]
    public async Task A_date_after_a_closed_claim_is_unknown()
    {
        // A universe whose upkeep stopped. Reading past the end must not return
        // the last constituent set it happened to hold, which is the same bias
        // wearing a different hat.
        var universe = Define();
        universe.DeclareCoverage(
            MembershipCoverage.Create(Sourced, new DateOnly(2026, 1, 2)),
            Source,
            DefinedAt);
        var repository = new FakeUniverseRepository(universe, [InstrumentId.New()]);

        var result = await new UniverseCatalog(repository)
            .ConstituentsAsOfAsync(
                Vn30,
                new DateOnly(2026, 6, 1),
                TestContext.Current.CancellationToken);

        Assert.False(result.IsKnown);
        Assert.Equal(UniverseUnknownReason.OutsideCoverage, result.UnknownReason);
    }

    private static Universe Define() => Universe.Define(
        UniverseId.New(),
        Vn30,
        "VN30 Index",
        UniverseKind.Index,
        Source,
        DefinedAt);

    private sealed class FakeUniverseRepository(
        Universe? universe,
        IReadOnlyList<InstrumentId>? members = null) : IUniverseRepository
    {
        public Task<IReadOnlyList<Universe>> ListAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Universe>>(universe is null ? [] : [universe]);

        public Task<Universe?> FindByCodeAsync(
            UniverseCode code,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(universe);

        public Task<IReadOnlyList<InstrumentId>> ListMembersAsOfAsync(
            UniverseId universeId,
            DateOnly asOf,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(members ?? []);

        public Task<IReadOnlyList<UniverseMembership>> ListSpellsForUpdateAsync(
            UniverseId universeId,
            InstrumentId instrumentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UniverseMembership>>([]);

        public Task<int> CountMembershipsAsync(
            UniverseId universeId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(members?.Count ?? 0);

        public Task<UniverseMembershipSpan> DescribeMembershipAsync(
            UniverseId universeId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(UniverseMembershipSpan.Empty);

        public Task<IReadOnlyList<UniverseCoverageFinding>> ListOpenFindingsAsync(
            UniverseId universeId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UniverseCoverageFinding>>([]);

        public void Add(Universe universe) => throw new NotSupportedException();

        public void Add(UniverseMembership membership) => throw new NotSupportedException();

        public void Add(UniverseCoverageFinding finding) => throw new NotSupportedException();
    }
}
