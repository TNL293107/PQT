using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.UnitTests.MarketData;

/// <summary>
/// Verifies interval arithmetic and period alignment.
/// </summary>
/// <remarks>
/// Alignment is the check that stops a whole series being silently shifted.
/// A provider returning 09:03 for a five-minute series is either sending a
/// partial bar or has offset everything, and both look plausible once stored.
/// </remarks>
public sealed class BarIntervalTests
{
    [Theory]
    [InlineData(BarInterval.OneMinute, 1)]
    [InlineData(BarInterval.FiveMinutes, 5)]
    [InlineData(BarInterval.FifteenMinutes, 15)]
    [InlineData(BarInterval.ThirtyMinutes, 30)]
    [InlineData(BarInterval.OneHour, 60)]
    [InlineData(BarInterval.OneDay, 1440)]
    public void An_interval_lasts_its_declared_number_of_minutes(BarInterval interval, int minutes) =>
        Assert.Equal(TimeSpan.FromMinutes(minutes), interval.ToDuration());

    [Fact]
    public void An_undeclared_interval_has_no_duration()
    {
        Assert.False(BarInterval.Unspecified.IsDeclared());
        Assert.Throws<ArgumentOutOfRangeException>(() => BarInterval.Unspecified.ToDuration());

        // An enum holds any integer of its underlying type, so a value read
        // back from a database has to be checked rather than assumed.
        Assert.False(((BarInterval)7).IsDeclared());
    }

    [Fact]
    public void A_daily_bar_aligns_only_to_midnight_utc()
    {
        var midnight = new DateTimeOffset(2026, 8, 25, 0, 0, 0, TimeSpan.Zero);

        Assert.True(BarInterval.OneDay.IsAligned(midnight));
        Assert.False(BarInterval.OneDay.IsAligned(midnight.AddHours(2)));
    }

    [Fact]
    public void A_five_minute_bar_aligns_only_to_a_five_minute_boundary()
    {
        var onBoundary = new DateTimeOffset(2026, 8, 25, 2, 15, 0, TimeSpan.Zero);

        Assert.True(BarInterval.FiveMinutes.IsAligned(onBoundary));
        Assert.False(BarInterval.FiveMinutes.IsAligned(onBoundary.AddMinutes(3)));
        Assert.False(BarInterval.FiveMinutes.IsAligned(onBoundary.AddSeconds(1)));
    }

    [Fact]
    public void An_instant_carrying_a_non_zero_offset_is_never_aligned()
    {
        // The same moment written in local time. Accepting it would make
        // alignment depend on where the process happens to run.
        var local = new DateTimeOffset(2026, 8, 25, 7, 0, 0, TimeSpan.FromHours(7));

        Assert.Equal(
            new DateTimeOffset(2026, 8, 25, 0, 0, 0, TimeSpan.Zero).UtcTicks,
            local.UtcTicks);
        Assert.False(BarInterval.OneDay.IsAligned(local));
    }

    [Fact]
    public void A_vietnamese_session_falls_inside_one_utc_day()
    {
        // The assumption the daily-bar convention rests on: at UTC+7 the whole
        // session lies within the UTC day of the same date, so the trading
        // date and the UTC date agree.
        var openLocal = new DateTimeOffset(2026, 8, 25, 9, 0, 0, TimeSpan.FromHours(7));
        var closeLocal = new DateTimeOffset(2026, 8, 25, 15, 0, 0, TimeSpan.FromHours(7));

        Assert.Equal(25, openLocal.UtcDateTime.Day);
        Assert.Equal(25, closeLocal.UtcDateTime.Day);
    }
}
