using PersonalQuant.Application.Abstractions;

namespace PersonalQuant.Infrastructure.Time;

/// <summary>
/// <see cref="IDelayScheduler"/> backed by the runtime timer.
/// </summary>
/// <remarks>
/// The only implementation that actually waits. Everything that schedules a
/// wait — retry backoff, provider call spacing — goes through the abstraction
/// so those policies can be asserted in a unit test without the test taking as
/// long as the policy says to wait.
/// </remarks>
internal sealed class SystemDelayScheduler : IDelayScheduler
{
    /// <inheritdoc />
    public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken = default) =>
        duration <= TimeSpan.Zero
            ? Task.CompletedTask
            : Task.Delay(duration, cancellationToken);
}
