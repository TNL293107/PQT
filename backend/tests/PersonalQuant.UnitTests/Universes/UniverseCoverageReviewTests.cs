using PersonalQuant.Application.Universes;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.Domain.Universes;
using PersonalQuant.UnitTests.MarketData.Fakes;

namespace PersonalQuant.UnitTests.Universes;

/// <summary>
/// Verifies that a membership history nobody sourced is written down rather
/// than left silent.
/// </summary>
/// <remarks>
/// The read side already answers <em>unknown</em> for an unsourced date, so no
/// single query can be fooled. These cover the other half: nobody runs that
/// query for every date, and without a recorded finding an empty universe is
/// indistinguishable from a complete one until a researcher trips over it.
/// </remarks>
public sealed class UniverseCoverageReviewTests
{
    private static readonly SourceCode Source = SourceCode.Create("TEST");
    private static readonly DateTimeOffset ReviewedAt = new(2026, 8, 30, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Sourced = new(2024, 1, 2);

    [Fact]
    public void A_universe_with_no_membership_is_a_finding()
    {
        // The headline requirement: a defined universe with nothing in it must
        // not be indistinguishable from a complete one.
        var found = UniverseCoverageReview.Diagnose(Define(), UniverseMembershipSpan.Empty);

        var (kind, detail) = Assert.Single(found);
        Assert.Equal(UniverseCoverageFindingKind.NoMembershipRecorded, kind);
        Assert.Contains("VN30", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_universe_with_no_membership_is_not_also_told_it_declared_no_claim()
    {
        // Both are true of a newly defined universe, and only one is
        // actionable. Two findings for one situation trains an operator to
        // ignore the list.
        var found = UniverseCoverageReview.Diagnose(Define(), UniverseMembershipSpan.Empty);

        Assert.DoesNotContain(
            found,
            gap => gap.Kind == UniverseCoverageFindingKind.NoCoverageDeclared);
    }

    [Fact]
    public void Rows_without_a_claim_are_a_finding()
    {
        // Worse than nothing in one specific way: the rows make the universe
        // look sourced while every as-of read against it stays unanswerable.
        var span = new UniverseMembershipSpan(30, Sourced, LatestEnd: null, HasOpenSpell: true);

        var found = UniverseCoverageReview.Diagnose(Define(), span);

        var (kind, _) = Assert.Single(found);
        Assert.Equal(UniverseCoverageFindingKind.NoCoverageDeclared, kind);
    }

    [Fact]
    public void Rows_inside_a_claim_are_no_finding()
    {
        var universe = Define();
        universe.DeclareCoverage(
            MembershipCoverage.Create(Sourced, until: null), Source, ReviewedAt);
        var span = new UniverseMembershipSpan(30, Sourced, LatestEnd: null, HasOpenSpell: true);

        Assert.Empty(UniverseCoverageReview.Diagnose(universe, span));
    }

    [Fact]
    public void Membership_older_than_the_claim_is_a_finding()
    {
        // The claim and the rows disagree. Either the history reaches further
        // back than the claim admits, or rows arrived from somewhere the claim
        // does not account for, and until somebody decides which, the claim
        // cannot be trusted to describe the rows.
        var universe = Define();
        universe.DeclareCoverage(
            MembershipCoverage.Create(Sourced, until: null), Source, ReviewedAt);
        var span = new UniverseMembershipSpan(
            30, new DateOnly(2018, 1, 2), LatestEnd: null, HasOpenSpell: true);

        var (kind, detail) = Assert.Single(UniverseCoverageReview.Diagnose(universe, span));

        Assert.Equal(UniverseCoverageFindingKind.MembershipOutsideCoverage, kind);
        Assert.Contains("2018-01-02", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_spell_still_running_past_a_closed_claim_is_a_finding()
    {
        // A universe whose upkeep stopped, with a security that never left.
        // The rows keep answering for dates the claim has abandoned.
        var universe = Define();
        universe.DeclareCoverage(
            MembershipCoverage.Create(Sourced, new DateOnly(2025, 1, 2)), Source, ReviewedAt);
        var span = new UniverseMembershipSpan(30, Sourced, LatestEnd: null, HasOpenSpell: true);

        var (kind, _) = Assert.Single(UniverseCoverageReview.Diagnose(universe, span));

        Assert.Equal(UniverseCoverageFindingKind.MembershipOutsideCoverage, kind);
    }

    [Fact]
    public void A_closed_spell_inside_a_closed_claim_is_no_finding()
    {
        var universe = Define();
        universe.DeclareCoverage(
            MembershipCoverage.Create(Sourced, new DateOnly(2025, 1, 2)), Source, ReviewedAt);
        var span = new UniverseMembershipSpan(
            30, Sourced, new DateOnly(2024, 12, 1), HasOpenSpell: false);

        Assert.Empty(UniverseCoverageReview.Diagnose(universe, span));
    }

    [Fact]
    public async Task A_review_records_the_gap_it_finds()
    {
        var repository = new FakeUniverseRepository([Define()]);

        var report = await new UniverseCoverageReview(repository, new FakeClock(ReviewedAt))
            .ReviewAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, report.Reviewed);
        Assert.Equal(1, report.Raised);
        Assert.Equal(1, report.StillOpen);

        var finding = Assert.Single(repository.Added);
        Assert.Equal(UniverseCoverageFindingKind.NoMembershipRecorded, finding.Kind);
        Assert.Equal(ReviewedAt, finding.DetectedAtUtc);
        Assert.True(finding.IsOpen);
    }

    [Fact]
    public async Task A_second_review_raises_nothing_new()
    {
        // A nightly run must not stack duplicates, and must not reset the age
        // of a finding an operator has already looked at.
        var universe = Define();
        var standing = UniverseCoverageFinding.Raise(
            universe.Id,
            UniverseCoverageFindingKind.NoMembershipRecorded,
            "Recorded yesterday.",
            ReviewedAt.AddDays(-1));

        var repository = new FakeUniverseRepository([universe], open: [standing]);

        var report = await new UniverseCoverageReview(repository, new FakeClock(ReviewedAt))
            .ReviewAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, report.Raised);
        Assert.Equal(1, report.StillOpen);
        Assert.Empty(repository.Added);
        Assert.True(standing.IsOpen);
    }

    [Fact]
    public async Task A_gap_that_has_been_filled_is_explained_rather_than_deleted()
    {
        // The record still has to show that the universe was once incomplete,
        // and when it stopped being.
        var universe = Define();
        universe.DeclareCoverage(
            MembershipCoverage.Create(Sourced, until: null), Source, ReviewedAt);

        var standing = UniverseCoverageFinding.Raise(
            universe.Id,
            UniverseCoverageFindingKind.NoMembershipRecorded,
            "Recorded before the history was sourced.",
            ReviewedAt.AddDays(-1));

        var repository = new FakeUniverseRepository(
            [universe],
            open: [standing],
            span: new UniverseMembershipSpan(30, Sourced, LatestEnd: null, HasOpenSpell: true));

        var report = await new UniverseCoverageReview(repository, new FakeClock(ReviewedAt))
            .ReviewAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, report.Explained);
        Assert.Equal(0, report.StillOpen);
        Assert.False(standing.IsOpen);
        Assert.Equal(DataQualityIssueStatus.Explained, standing.Status);
        Assert.Equal(ReviewedAt, standing.ResolvedAtUtc);
    }

    [Fact]
    public async Task A_review_stages_findings_and_does_not_commit_them()
    {
        // The caller owns the transaction, so an import can record membership
        // and the finding about it together. A finding committed on its own
        // would describe a state the rows never reached.
        var repository = new FakeUniverseRepository([Define()]);

        await new UniverseCoverageReview(repository, new FakeClock(ReviewedAt))
            .ReviewAsync(TestContext.Current.CancellationToken);

        Assert.Single(repository.Added);
        Assert.Equal(0, repository.SaveCount);
    }

    private static Universe Define() => Universe.Define(
        UniverseId.New(),
        UniverseCode.Create("VN30"),
        "VN30 Index",
        UniverseKind.Index,
        Source,
        ReviewedAt);

    private sealed class FakeUniverseRepository(
        IReadOnlyList<Universe> defined,
        IReadOnlyList<UniverseCoverageFinding>? open = null,
        UniverseMembershipSpan? span = null) : IUniverseRepository
    {
        public List<UniverseCoverageFinding> Added { get; } = [];

        public int SaveCount { get; }

        public Task<IReadOnlyList<Universe>> ListAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(defined);

        public Task<Universe?> FindByCodeAsync(
            UniverseCode code,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(defined.FirstOrDefault(universe => universe.Code == code));

        public Task<IReadOnlyList<InstrumentId>> ListMembersAsOfAsync(
            UniverseId universeId,
            DateOnly asOf,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<InstrumentId>>([]);

        public Task<IReadOnlyList<UniverseMembership>> ListSpellsForUpdateAsync(
            UniverseId universeId,
            InstrumentId instrumentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UniverseMembership>>([]);

        public Task<int> CountMembershipsAsync(
            UniverseId universeId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult((span ?? UniverseMembershipSpan.Empty).Count);

        public Task<UniverseMembershipSpan> DescribeMembershipAsync(
            UniverseId universeId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(span ?? UniverseMembershipSpan.Empty);

        public Task<IReadOnlyList<UniverseCoverageFinding>> ListOpenFindingsAsync(
            UniverseId universeId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UniverseCoverageFinding>>(open ?? []);

        public void Add(Universe universe) => throw new NotSupportedException();

        public void Add(UniverseMembership membership) => throw new NotSupportedException();

        public void Add(UniverseCoverageFinding finding) => Added.Add(finding);
    }
}
