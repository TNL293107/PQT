using PersonalQuant.Application.MarketData;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.UnitTests.MarketData;

/// <summary>
/// Verifies how a client's interval string is read.
/// </summary>
/// <remarks>
/// Clients write <c>1d</c> or <c>15m</c>, which is what a chart control and a
/// command bar both use. The enum's own names are accepted too, so a response
/// can be fed straight back into a request.
/// </remarks>
public sealed class BarIntervalParserTests
{
    [Theory]
    [InlineData("1m", BarInterval.OneMinute)]
    [InlineData("5m", BarInterval.FiveMinutes)]
    [InlineData("15m", BarInterval.FifteenMinutes)]
    [InlineData("30m", BarInterval.ThirtyMinutes)]
    [InlineData("1h", BarInterval.OneHour)]
    [InlineData("1d", BarInterval.OneDay)]
    [InlineData("EOD", BarInterval.OneDay)]
    [InlineData("  1D  ", BarInterval.OneDay)]
    public void An_alias_names_a_resolution(string value, BarInterval expected)
    {
        Assert.True(BarIntervalParser.TryParse(value, out var interval));
        Assert.Equal(expected, interval);
    }

    [Fact]
    public void The_name_a_response_carries_parses_back()
    {
        // A client that echoes what it was given must get the same resolution.
        Assert.True(BarIntervalParser.TryParse(BarInterval.FifteenMinutes.ToString(), out var interval));
        Assert.Equal(BarInterval.FifteenMinutes, interval);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_absent_value_falls_back_to_the_daily_series(string? value)
    {
        Assert.True(BarIntervalParser.TryParse(value, out var interval));
        Assert.Equal(BarInterval.OneDay, interval);
    }

    [Theory]
    [InlineData("2h")]
    [InlineData("weekly")]
    [InlineData("Unspecified")]
    [InlineData("7")]
    public void An_unknown_or_undeclared_value_is_refused(string value) =>
        Assert.False(BarIntervalParser.TryParse(value, out _));

    [Fact]
    public void The_accepted_aliases_are_describable_for_an_error_message()
    {
        var described = BarIntervalParser.DescribeAccepted();

        Assert.Contains("1d", described, StringComparison.Ordinal);
        Assert.Contains("15m", described, StringComparison.Ordinal);
    }
}
