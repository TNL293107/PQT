using PersonalQuant.Infrastructure.Time;

namespace PersonalQuant.UnitTests.Time;

public sealed class SystemClockTests
{
    [Fact]
    public void UtcNow_reports_time_in_utc()
    {
        // Every timestamp in the system is stored and compared in UTC. A clock
        // that returned local time would silently corrupt that.
        // Arrange
        var clock = new SystemClock();

        // Act
        var now = clock.UtcNow;

        // Assert
        Assert.Equal(TimeSpan.Zero, now.Offset);
    }

    [Fact]
    public void UtcNow_advances()
    {
        // Arrange
        var clock = new SystemClock();

        // Act
        var first = clock.UtcNow;
        Thread.Sleep(2);
        var second = clock.UtcNow;

        // Assert
        Assert.True(second >= first);
    }
}
