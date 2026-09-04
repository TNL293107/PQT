using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.Exchanges;

namespace PersonalQuant.UnitTests.Exchanges;

/// <summary>
/// Verifies the claim that replaced an inference.
/// </summary>
/// <remarks>
/// Coverage used to be read off the furthest recorded closure, and that was
/// wrong in both directions at once: a calendar transcribed through 2026
/// reported its reach as 2 September — the year's last public holiday — while
/// every date before that closure read as covered, including years holding no
/// rows at all. The second half was live, and raised three real Vietnamese
/// public holidays in 2016 as missing sessions.
/// </remarks>
public sealed class CalendarCoverageTests
{
    private static readonly DateOnly Start = new(2022, 1, 1);
    private static readonly DateOnly End = new(2027, 1, 1);

    [Fact]
    public void A_claim_covers_its_first_date_and_stops_before_its_end()
    {
        var coverage = CalendarCoverage.Create(Start, End);

        Assert.True(coverage.Covers(Start));
        Assert.True(coverage.Covers(new DateOnly(2026, 12, 31)));

        // Half-open, like every other interval here.
        Assert.False(coverage.Covers(End));
        Assert.False(coverage.Covers(Start.AddDays(-1)));
    }

    [Fact]
    public void The_last_covered_date_is_the_day_before_the_exclusive_end()
    {
        // What an operator compares against today, rather than the boundary the
        // arithmetic uses.
        Assert.Equal(new DateOnly(2026, 12, 31), CalendarCoverage.Create(Start, End).Through);
    }

    [Fact]
    public void A_claim_that_runs_on_covers_every_later_date_and_has_no_last_date()
    {
        var coverage = CalendarCoverage.Create(Start, until: null);

        Assert.True(coverage.Covers(new DateOnly(2099, 1, 1)));
        Assert.Null(coverage.Through);
    }

    [Fact]
    public void A_window_is_covered_only_when_both_ends_are()
    {
        // A completeness figure over a partly-transcribed window is wrong for
        // the part that is not, and nothing in the number says which part.
        var coverage = CalendarCoverage.Create(Start, End);

        Assert.True(coverage.CoversRange(new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 6)));
        Assert.False(coverage.CoversRange(new DateOnly(2026, 12, 28), new DateOnly(2027, 1, 2)));
        Assert.False(coverage.CoversRange(new DateOnly(2021, 12, 28), new DateOnly(2022, 1, 5)));
    }

    [Fact]
    public void A_span_covering_no_date_is_refused()
    {
        // Claiming to have transcribed an empty span is claiming to have
        // transcribed nothing, which is said unambiguously by claiming nothing.
        Assert.Throws<DomainValidationException>(() => CalendarCoverage.Create(Start, Start));
        Assert.Throws<DomainValidationException>(
            () => CalendarCoverage.Create(Start, Start.AddDays(-1)));
    }

    [Fact]
    public void A_venue_with_no_claim_covers_nothing()
    {
        // No claim is not a small claim. This is the state every venue starts
        // in, and it makes completeness unmeasurable rather than wrong.
        var venue = Venue();

        Assert.Null(venue.CalendarCoverage);
        Assert.False(venue.CalendarCovers(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)));
    }

    [Fact]
    public void A_year_the_calendar_never_reached_is_not_covered_by_a_later_claim()
    {
        // The over-claim, exactly as it happened. A calendar transcribed from
        // 2022 says nothing about 2016, and a system that thought otherwise
        // raised real public holidays as missing sessions.
        var venue = Venue();

        venue.DeclareCalendarCoverage(CalendarCoverage.Create(Start, End), Now);

        Assert.False(venue.CalendarCovers(new DateOnly(2016, 4, 1), new DateOnly(2016, 6, 30)));
    }

    [Fact]
    public void A_quarter_with_no_closures_inside_the_claim_is_still_covered()
    {
        // The under-claim. Nothing closes on the Vietnamese exchanges between
        // September and December, and the last quarter of a transcribed year
        // must not therefore become unmeasurable.
        var venue = Venue();

        venue.DeclareCalendarCoverage(CalendarCoverage.Create(Start, End), Now);

        Assert.True(venue.CalendarCovers(new DateOnly(2026, 10, 1), new DateOnly(2026, 12, 31)));
    }

    [Fact]
    public void A_claim_can_be_narrowed_as_well_as_extended()
    {
        // A transcription found to be wrong states a smaller span. A claim that
        // could only ever grow would make that inexpressible.
        var venue = Venue();

        venue.DeclareCalendarCoverage(CalendarCoverage.Create(Start, End), Now);
        venue.DeclareCalendarCoverage(
            CalendarCoverage.Create(Start, new DateOnly(2025, 1, 1)), Now);

        Assert.False(venue.CalendarCovers(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)));
    }

    [Fact]
    public void A_claim_can_be_withdrawn()
    {
        var venue = Venue();

        venue.DeclareCalendarCoverage(CalendarCoverage.Create(Start, End), Now);
        venue.DeclareCalendarCoverage(null, Now);

        Assert.Null(venue.CalendarCoverage);
        Assert.False(venue.CalendarCovers(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)));
    }

    private static readonly DateTimeOffset Now = new(2026, 9, 4, 0, 0, 0, TimeSpan.Zero);

    private static Exchange Venue() =>
        Exchange.Register(
            ExchangeCode.Create("HOSE"),
            "Ho Chi Minh Stock Exchange",
            "Asia/Ho_Chi_Minh",
            Now);
}
