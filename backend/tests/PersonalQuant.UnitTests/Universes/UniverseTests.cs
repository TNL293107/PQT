using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.Domain.Universes;

namespace PersonalQuant.UnitTests.Universes;

/// <summary>
/// Verifies what a universe claims to know about its own membership.
/// </summary>
/// <remarks>
/// <para>
/// The coverage claim is the whole point of this type. Membership rows alone
/// cannot distinguish <em>this index had no constituents then</em> from
/// <em>nobody has sourced its constituents for then</em>, and those two answers
/// are opposites: the first is a fact, the second is an absence of data that a
/// backtest would silently read as a fact.
/// </para>
/// <para>
/// So a universe states, separately from its rows, the span it claims to know.
/// An as-of read outside that span is unknown rather than empty, and a universe
/// that claims nothing knows nothing — which is the honest state a newly
/// defined one starts in.
/// </para>
/// </remarks>
public sealed class UniverseTests
{
    private static readonly SourceCode Source = SourceCode.Create("TEST");
    private static readonly DateTimeOffset DefinedAt = new(2026, 8, 30, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Start = new(2024, 1, 2);
    private static readonly DateOnly End = new(2026, 1, 2);

    [Fact]
    public void A_new_universe_claims_to_know_nothing()
    {
        // The state that matters most, because it is the state every universe
        // is in before anyone sources its history. It must not read as an
        // index that existed and had no members.
        var universe = Define();

        Assert.Null(universe.Coverage);
        Assert.False(universe.Knows(Start));
        Assert.False(universe.Knows(End));
    }

    [Fact]
    public void A_declared_span_is_known_from_its_first_day()
    {
        var universe = Define();

        universe.DeclareCoverage(MembershipCoverage.Create(Start, End), Source, DefinedAt);

        Assert.True(universe.Knows(Start));
        Assert.False(universe.Knows(Start.AddDays(-1)));
    }

    [Fact]
    public void A_declared_span_is_not_known_on_the_day_it_ends()
    {
        // Half-open, like every other interval in the system. A claim that ends
        // on the second of January covers the first and not the second.
        var universe = Define();

        universe.DeclareCoverage(MembershipCoverage.Create(Start, End), Source, DefinedAt);

        Assert.True(universe.Knows(End.AddDays(-1)));
        Assert.False(universe.Knows(End));
    }

    [Fact]
    public void An_open_ended_span_is_known_from_its_first_day_onwards()
    {
        // What a maintained universe looks like: sourced from a date, still
        // being kept up to date, no end claimed.
        var universe = Define();

        universe.DeclareCoverage(MembershipCoverage.Create(Start, until: null), Source, DefinedAt);

        Assert.True(universe.Knows(Start));
        Assert.True(universe.Knows(End));
        Assert.False(universe.Knows(Start.AddDays(-1)));
    }

    [Fact]
    public void A_widened_claim_replaces_the_previous_one()
    {
        // Sourcing older history is ordinary. The claim moves; it does not
        // accumulate into a set of disjoint spans, which nothing yet needs and
        // which would make the read a range scan rather than a comparison.
        var universe = Define();
        universe.DeclareCoverage(MembershipCoverage.Create(Start, End), Source, DefinedAt);

        var earlier = new DateOnly(2018, 1, 2);
        universe.DeclareCoverage(MembershipCoverage.Create(earlier, End), Source, DefinedAt);

        Assert.True(universe.Knows(earlier));
        Assert.Equal(earlier, universe.Coverage?.From);
    }

    [Fact]
    public void A_claim_that_ends_before_it_starts_is_refused()
    {
        Assert.Throws<DomainValidationException>(() =>
            MembershipCoverage.Create(End, Start));
    }

    [Fact]
    public void A_claim_that_covers_no_day_is_refused()
    {
        // [d, d) is empty. A universe claiming an empty span is claiming
        // nothing, and should say so by claiming nothing.
        Assert.Throws<DomainValidationException>(() =>
            MembershipCoverage.Create(Start, Start));
    }

    [Fact]
    public void A_universe_is_identified_by_a_code_and_keeps_its_kind()
    {
        var universe = Universe.Define(
            UniverseId.New(),
            UniverseCode.Create("vn30"),
            "VN30 Index",
            UniverseKind.Index,
            Source,
            DefinedAt);

        Assert.Equal("VN30", universe.Code.Value);
        Assert.Equal(UniverseKind.Index, universe.Kind);
        Assert.Equal(DefinedAt, universe.CreatedAtUtc);
    }

    [Fact]
    public void A_universe_must_be_named()
    {
        Assert.Throws<DomainValidationException>(() => Universe.Define(
            UniverseId.New(),
            UniverseCode.Create("VN30"),
            "   ",
            UniverseKind.Index,
            Source,
            DefinedAt));
    }

    [Fact]
    public void An_undeclared_kind_is_refused()
    {
        // A universe whose kind is a cast integer would reach a dashboard as a
        // blank, and reach a query as a value no branch handles.
        Assert.Throws<DomainValidationException>(() => Universe.Define(
            UniverseId.New(),
            UniverseCode.Create("VN30"),
            "VN30 Index",
            (UniverseKind)99,
            Source,
            DefinedAt));
    }

    [Theory]
    [InlineData("VN30")]
    [InlineData("VNINDEX")]
    [InlineData("HOSE_ALL")]
    public void A_code_may_carry_letters_digits_and_underscores(string value)
    {
        Assert.Equal(value, UniverseCode.Create(value).Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("V")]
    [InlineData("VN 30")]
    [InlineData("VN-30")]
    public void An_unusable_code_is_refused(string? value)
    {
        Assert.False(UniverseCode.TryCreate(value, out _));
    }

    private static Universe Define() => Universe.Define(
        UniverseId.New(),
        UniverseCode.Create("VN30"),
        "VN30 Index",
        UniverseKind.Index,
        Source,
        DefinedAt);
}
