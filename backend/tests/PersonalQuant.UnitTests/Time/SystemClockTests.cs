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
}
