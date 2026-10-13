using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.Exchanges;

namespace PersonalQuant.UnitTests.Exchanges;

/// <summary>
/// Verifies the venues' equity ticks and the rounding a reference price gets.
/// </summary>
public sealed class TickScheduleTests
{
    private static TickSchedule Hose => TickSchedule.ForEquitiesOn(ExchangeCode.Create("HOSE"))!;

    [Theory]
    [InlineData(9_990, 10)]
    [InlineData(10_000, 50)]
    [InlineData(49_950, 50)]
    [InlineData(50_000, 100)]
    public void HOSE_quotes_in_a_step_set_by_the_price_band(decimal price, decimal tick) =>
        Assert.Equal(tick, Hose.TickAt(price));

    [Theory]
    [InlineData("HNX")]
    [InlineData("UPCOM")]
    public void HNX_and_UPCOM_quote_every_share_in_a_hundred_dong(string venue)
    {
        var schedule = TickSchedule.ForEquitiesOn(ExchangeCode.Create(venue))!;

        Assert.Equal(100m, schedule.TickAt(5_000m));
        Assert.Equal(100m, schedule.TickAt(120_000m));
    }

    [Fact]
    public void A_venue_nobody_transcribed_has_no_schedule() =>
        Assert.Null(TickSchedule.ForEquitiesOn(ExchangeCode.Create("XSES")));

    [Theory]
    // References HOSE set on 2017 ex-dates. The first two rule out rounding
    // down, the third rules out rounding up: the rule is the nearest tick.
    [InlineData(28_933.33, 28_950)]
    [InlineData(30_333.33, 30_350)]
    [InlineData(97_928.57, 97_900)]
    // FPT 27 May 2016: 47,500 × 0.8512585812.
    [InlineData(40_434.78, 40_450)]
    public void A_reference_rounds_to_the_nearest_tick(decimal computed, decimal reference) =>
        Assert.Equal(reference, Hose.RoundToTick(computed));

    [Fact]
    public void A_price_just_under_a_band_boundary_can_round_onto_it() =>
        Assert.Equal(50_000m, Hose.RoundToTick(49_980m));

    [Fact]
    public void A_price_already_on_a_tick_is_left_alone() =>
        Assert.Equal(24_000m, Hose.RoundToTick(24_000m));

    [Fact]
    public void A_price_that_is_not_positive_has_no_tick() =>
        Assert.Throws<DomainValidationException>(() => Hose.TickAt(0m));
}
