using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.Domain.Universes;

namespace PersonalQuant.UnitTests.Universes;

/// <summary>
/// Verifies the interval a membership claims.
/// </summary>
/// <remarks>
/// <para>
/// The interval is half-open — <c>[EffectiveFrom, EffectiveTo)</c> — for the
/// same reason an observation window is: a security removed from an index on
/// the day another replaces it must belong to exactly one side of that date.
/// Inclusive-inclusive bounds would count a leaver and its replacement on the
/// same session, and an index of thirty would silently hold thirty-one.
/// </para>
/// <para>
/// Re-entry is ordinary in this market: a security demoted from VN30 at one
/// review and restored at a later one has two disjoint memberships, not one
/// with a hole. The model must express that without an update that erases the
/// first spell.
/// </para>
/// </remarks>
public sealed class UniverseMembershipTests
{
    private static readonly UniverseId Vn30 = UniverseId.New();
    private static readonly InstrumentId Security = InstrumentId.New();
    private static readonly SourceCode Source = SourceCode.Create("TEST");
    private static readonly DateTimeOffset RecordedAt = new(2026, 8, 30, 3, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly January = new(2026, 1, 5);
    private static readonly DateOnly July = new(2026, 7, 6);

    [Fact]
    public void An_admission_opens_an_interval_that_is_still_running()
    {
        // Act
        var membership = Admit(January);

        // Assert
        Assert.Equal(Vn30, membership.UniverseId);
        Assert.Equal(Security, membership.InstrumentId);
        Assert.Equal(January, membership.EffectiveFrom);
        Assert.Null(membership.EffectiveTo);
        Assert.True(membership.IsCurrent);
        Assert.Equal(RecordedAt, membership.RecordedAtUtc);
    }

    [Fact]
    public void The_interval_is_inclusive_on_the_day_it_opened()
    {
        // A security admitted at the January review is a constituent for the
        // January session, not from the next one.
        var membership = Admit(January);

        Assert.True(membership.WasMemberOn(January));
    }

    [Fact]
    public void The_interval_is_exclusive_on_the_day_it_closed()
    {
        // The removal date is the first session the security is no longer a
        // member. Counting it as one more day of membership is how a
        // constituent set ends up one name too large on every review date.
        var membership = Admit(January);
        membership.Remove(July);

        Assert.True(membership.WasMemberOn(July.AddDays(-1)));
        Assert.False(membership.WasMemberOn(July));
    }

    [Fact]
    public void A_day_before_the_interval_is_not_membership()
    {
        var membership = Admit(January);

        Assert.False(membership.WasMemberOn(January.AddDays(-1)));
    }

    [Fact]
    public void A_running_membership_covers_every_later_day()
    {
        var membership = Admit(January);

        Assert.True(membership.WasMemberOn(July));
    }

    [Fact]
    public void A_removal_cannot_be_recorded_twice()
    {
        // The table is append-only and a row is only ever updated to close its
        // interval. Closing it a second time would rewrite history rather than
        // record it, and a re-entry belongs in a new row.
        var membership = Admit(January);
        membership.Remove(July);

        Assert.Throws<DomainStateException>(() => membership.Remove(July.AddDays(30)));
    }

    [Fact]
    public void A_removal_cannot_precede_the_admission()
    {
        var membership = Admit(July);

        Assert.Throws<DomainValidationException>(() => membership.Remove(January));
    }

    [Fact]
    public void A_membership_cannot_end_on_the_day_it_began()
    {
        // Half-open, so [d, d) is empty. A membership that covers no session
        // did not happen, and storing one would make a constituent set depend
        // on which side of the emptiness a query landed.
        var membership = Admit(January);

        Assert.Throws<DomainValidationException>(() => membership.Remove(January));
    }

    [Fact]
    public void Re_entry_is_a_second_interval_that_does_not_overlap_the_first()
    {
        // Demoted in July, restored in the following January. Both spells are
        // true, and the gap between them is the part a survivorship-free
        // backtest needs.
        var first = Admit(January);
        first.Remove(July);
        var second = Admit(new DateOnly(2027, 1, 4));

        Assert.False(first.Overlaps(second));
        Assert.False(second.Overlaps(first));
        Assert.False(first.WasMemberOn(new DateOnly(2026, 10, 1)));
        Assert.True(second.WasMemberOn(new DateOnly(2027, 2, 1)));
    }

    [Fact]
    public void Two_running_memberships_of_the_same_security_overlap()
    {
        // Both are open-ended, so they claim the same security is a member
        // twice over. The database refuses this outright; the domain says so
        // first, with a message that names the security rather than a
        // constraint.
        var first = Admit(January);
        var second = Admit(July);

        Assert.True(first.Overlaps(second));
        Assert.True(second.Overlaps(first));
    }

    [Fact]
    public void Intervals_that_meet_at_a_date_do_not_overlap()
    {
        // The closing edge of one and the opening edge of the next are the same
        // date, and exactly one of them contains it.
        var first = Admit(January);
        first.Remove(July);
        var second = Admit(July);

        Assert.False(first.Overlaps(second));
        Assert.True(second.WasMemberOn(July));
    }

    [Fact]
    public void Memberships_of_different_securities_never_overlap()
    {
        var first = Admit(January);
        var other = UniverseMembership.Admit(
            Vn30,
            InstrumentId.New(),
            January,
            announcedOn: null,
            Source,
            RecordedAt);

        Assert.False(first.Overlaps(other));
    }

    [Fact]
    public void Memberships_in_different_universes_never_overlap()
    {
        // The same security belongs to VN30 and VNINDEX at once. That is two
        // facts, not a contradiction.
        var first = Admit(January);
        var other = UniverseMembership.Admit(
            UniverseId.New(),
            Security,
            January,
            announcedOn: null,
            Source,
            RecordedAt);

        Assert.False(first.Overlaps(other));
    }

    [Fact]
    public void An_announcement_date_is_recorded_and_does_not_move_the_interval()
    {
        // Announcement time is stored for the same reason a corporate action's
        // is: a decision taken on the effective date could not have known the
        // review's outcome before it was published. Reading it is U4's, and
        // until then it must not quietly alter what the interval says.
        var announced = January.AddDays(-14);

        var membership = UniverseMembership.Admit(
            Vn30,
            Security,
            January,
            announced,
            Source,
            RecordedAt);

        Assert.Equal(announced, membership.AnnouncedOn);
        Assert.False(membership.WasMemberOn(announced));
        Assert.True(membership.WasMemberOn(January));
    }

    [Fact]
    public void A_membership_must_name_a_universe_and_a_security()
    {
        Assert.Throws<DomainValidationException>(() => UniverseMembership.Admit(
            default,
            Security,
            January,
            announcedOn: null,
            Source,
            RecordedAt));

        Assert.Throws<DomainValidationException>(() => UniverseMembership.Admit(
            Vn30,
            default,
            January,
            announcedOn: null,
            Source,
            RecordedAt));
    }

    [Fact]
    public void A_membership_must_record_when_this_system_learned_it()
    {
        // Local time here would be worse than none: it looks authoritative and
        // shifts when the process moves between machines.
        Assert.Throws<DomainValidationException>(() => UniverseMembership.Admit(
            Vn30,
            Security,
            January,
            announcedOn: null,
            Source,
            new DateTimeOffset(2026, 8, 30, 10, 0, 0, TimeSpan.FromHours(7))));
    }

    private static UniverseMembership Admit(DateOnly effectiveFrom) =>
        UniverseMembership.Admit(Vn30, Security, effectiveFrom, announcedOn: null, Source, RecordedAt);
}
