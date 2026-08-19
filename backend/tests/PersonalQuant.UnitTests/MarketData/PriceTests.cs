using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.UnitTests.MarketData;

/// <summary>
/// Verifies what may and may not be a price.
/// </summary>
/// <remarks>
/// The rules here are what stop a provider's "no data" sentinel from becoming
/// a bar that every indicator downstream treats as a real move.
/// </remarks>
public sealed class PriceTests
{
    [Theory]
    [InlineData(0.000001)]
    [InlineData(1)]
    [InlineData(27350)]
    [InlineData(1234.56)]
    public void A_positive_value_inside_the_range_is_a_price(decimal value)
    {
        Assert.True(Price.TryCreate(value, out var price));
        Assert.Equal(value, price.Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-0.01)]
    public void A_non_positive_value_is_not_a_price(decimal value)
    {
        // Zero is a provider saying "no data", not a trade. Accepting it puts
        // a hundred-per-cent drawdown into the series.
        Assert.False(Price.TryCreate(value, out _));
        Assert.Throws<DomainValidationException>(() => Price.Create(value));
    }

    [Fact]
    public void A_value_beyond_the_ceiling_is_rejected()
    {
        Assert.False(Price.TryCreate(Price.MaxValue + 1m, out _));
        Assert.True(Price.TryCreate(Price.MaxValue, out _));
    }

    [Fact]
    public void A_value_with_more_scale_than_is_stored_is_rejected()
    {
        // Rounding at persistence rather than here would mean a bar that
        // passed its invariant check in memory could violate it after a round
        // trip.
        var tooPrecise = 1.0000001m;

        Assert.Equal(Price.MaxScale + 1, tooPrecise.Scale);
        Assert.False(Price.TryCreate(tooPrecise, out _));
    }

    [Fact]
    public void Prices_compare_by_value()
    {
        var low = Price.Create(10m);
        var high = Price.Create(20m);

        Assert.True(low < high);
        Assert.True(high > low);
        Assert.True(low <= Price.Create(10m));
        Assert.True(high >= Price.Create(20m));
        Assert.Equal(-1, low.CompareTo(high));
    }

    [Fact]
    public void Trailing_zeros_do_not_change_equality()
    {
        // decimal keeps scale, so 10 and 10.00 are distinct representations of
        // one number. A series that treated them as different values would
        // report a revision every time a provider changed its formatting.
        Assert.Equal(Price.Create(10m), Price.Create(10.00m));
    }

    [Fact]
    public void A_price_converts_to_its_decimal()
    {
        decimal value = Price.Create(27350m);

        Assert.Equal(27350m, value);
    }
}
