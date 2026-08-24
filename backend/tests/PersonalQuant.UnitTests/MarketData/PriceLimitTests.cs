using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.Exchanges;

namespace PersonalQuant.UnitTests.MarketData;

/// <summary>
/// Verifies the daily price band that the cross-session check measures against.
/// </summary>
/// <remarks>
/// The sharpest data-quality test this market offers: the exchange rejects
/// orders outside the band, so a larger move did not happen the way the numbers
/// claim.
/// </remarks>
public sealed class PriceLimitTests
{
    [Theory]
    [InlineData(7, 0.07)]
    [InlineData(10, 0.10)]
    [InlineData(15, 0.15)]
    public void A_published_percentage_becomes_a_fraction(decimal percent, decimal fraction)
    {
        var limit = PriceLimit.FromPercent(percent);

        Assert.Equal(fraction, limit.Fraction);
        Assert.Equal(percent, limit.Percent);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-7)]
    [InlineData(100)]
    [InlineData(250)]
    public void A_limit_that_is_not_one_is_rejected(decimal percent)
    {
        // Zero would halt the security and a hundred per cent is not a limit.
        // Both are configuration errors rather than market structure.
        Assert.False(PriceLimit.TryFromPercent(percent, out _));
        Assert.Throws<DomainValidationException>(() => PriceLimit.FromPercent(percent));
    }

    [Fact]
    public void A_move_inside_the_band_is_permitted()
    {
        var limit = PriceLimit.FromPercent(7m);

        Assert.True(limit.Permits(100m, 105m, tolerance: 0m));
        Assert.True(limit.Permits(100m, 95m, tolerance: 0m));
    }

    [Fact]
    public void A_move_exactly_at_the_ceiling_is_permitted()
    {
        // A security closing at its limit is an ordinary day for a stock in
        // demand, not a data fault.
        var limit = PriceLimit.FromPercent(7m);

        Assert.True(limit.Permits(100m, 107m, tolerance: 0m));
        Assert.True(limit.Permits(100m, 93m, tolerance: 0m));
    }

    [Fact]
    public void A_move_beyond_the_band_is_refused_in_both_directions()
    {
        var limit = PriceLimit.FromPercent(7m);

        Assert.False(limit.Permits(100m, 108m, tolerance: 0m));
        Assert.False(limit.Permits(100m, 92m, tolerance: 0m));
    }

    [Fact]
    public void The_tolerance_absorbs_tick_rounding()
    {
        // The reference price is rounded to a tick before the band is
        // computed, so a realised move can exceed the nominal percentage
        // slightly without anything being wrong.
        var limit = PriceLimit.FromPercent(7m);

        Assert.False(limit.Permits(100m, 107.4m, tolerance: 0m));
        Assert.True(limit.Permits(100m, 107.4m, tolerance: 0.005m));
    }

    [Fact]
    public void A_split_sized_move_is_refused_whatever_the_tolerance()
    {
        // The case the check exists for: a two-for-one split halves the price
        // overnight and nothing about the bar itself says so.
        var limit = PriceLimit.FromPercent(15m);

        Assert.False(limit.Permits(100m, 50m, tolerance: 0.005m));
    }

    [Fact]
    public void A_bar_with_no_usable_predecessor_is_permitted()
    {
        // A series that has just started is not a breach.
        var limit = PriceLimit.FromPercent(7m);

        Assert.True(limit.Permits(0m, 100m, tolerance: 0m));
    }

    [Fact]
    public void A_limit_reads_as_the_band_it_is()
    {
        Assert.Equal("±7%", PriceLimit.FromPercent(7m).ToString());
        Assert.Equal("±6.5%", PriceLimit.FromPercent(6.5m).ToString());
    }
}
