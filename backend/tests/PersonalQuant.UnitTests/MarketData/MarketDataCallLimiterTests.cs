using PersonalQuant.Application.Abstractions;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.UnitTests.MarketData.Fakes;

namespace PersonalQuant.UnitTests.MarketData;

/// <summary>
/// Verifies that calls to one source are spaced and calls to different sources
/// are not.
/// </summary>
/// <remarks>
/// Backfilling an instrument universe is a loop. Without a gate it issues
/// hundreds of calls in a second and is refused for the rest of the hour.
/// </remarks>
public sealed class MarketDataCallLimiterTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 26, 3, 0, 0, TimeSpan.Zero);
    private static readonly SourceCode First = SourceCode.Create("ONE");
    private static readonly SourceCode Second = SourceCode.Create("TWO");

    [Fact]
    public async Task The_first_call_to_a_source_waits_for_nothing()
    {
        using var limiter = CreateLimiter(TimeSpan.FromMilliseconds(200), out var clock, out var delays);

        await limiter.WaitForTurnAsync(First, TestContext.Current.CancellationToken);

        Assert.Empty(delays.Waits);
        _ = clock;
    }

    [Fact]
    public async Task A_second_call_too_soon_waits_out_the_remaining_gap()
    {
        using var limiter = CreateLimiter(TimeSpan.FromMilliseconds(200), out var clock, out var delays);

        await limiter.WaitForTurnAsync(First, TestContext.Current.CancellationToken);

        // 50ms of the 200ms gap has elapsed, so 150ms remain.
        clock.UtcNow = Start.AddMilliseconds(50);

        // Act
        await limiter.WaitForTurnAsync(First, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(TimeSpan.FromMilliseconds(150), Assert.Single(delays.Waits));
    }

    [Fact]
    public async Task A_second_call_after_the_gap_waits_for_nothing()
    {
        using var limiter = CreateLimiter(TimeSpan.FromMilliseconds(200), out var clock, out var delays);

        await limiter.WaitForTurnAsync(First, TestContext.Current.CancellationToken);
        clock.UtcNow = Start.AddMilliseconds(500);

        // Act
        await limiter.WaitForTurnAsync(First, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(delays.Waits);
    }

    [Fact]
    public async Task A_slow_source_does_not_hold_up_a_fast_one()
    {
        // Per source, not global: two providers have unrelated limits.
        using var limiter = CreateLimiter(TimeSpan.FromMilliseconds(200), out var clock, out var delays);

        await limiter.WaitForTurnAsync(First, TestContext.Current.CancellationToken);
        clock.UtcNow = Start.AddMilliseconds(10);

        // Act
        await limiter.WaitForTurnAsync(Second, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(delays.Waits);
    }

    [Fact]
    public async Task Spacing_of_zero_disables_the_gate_entirely()
    {
        using var limiter = CreateLimiter(TimeSpan.Zero, out _, out var delays);

        await limiter.WaitForTurnAsync(First, TestContext.Current.CancellationToken);
        await limiter.WaitForTurnAsync(First, TestContext.Current.CancellationToken);

        Assert.Empty(delays.Waits);
    }

    private static MarketDataCallLimiter CreateLimiter(
        TimeSpan spacing,
        out FakeClock clock,
        out RecordingDelays delays)
    {
        var policy = new IngestionPolicy { MinimumCallSpacing = spacing }.Validated();

        clock = new FakeClock(Start);
        delays = new RecordingDelays();

        return new MarketDataCallLimiter(policy, clock, delays);
    }

    /// <summary>
    /// Records the waits without performing them, so the gate's arithmetic can
    /// be asserted rather than timed.
    /// </summary>
    private sealed class RecordingDelays : IDelayScheduler
    {
        public List<TimeSpan> Waits { get; } = [];

        public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken = default)
        {
            Waits.Add(duration);

            return Task.CompletedTask;
        }
    }
}
