namespace PersonalQuant.Application.Abstractions;

/// <summary>
/// Waits for a period of time.
/// </summary>
/// <remarks>
/// <para>
/// Injected for the same reason <see cref="IClock"/> is. Backoff and rate
/// limiting are behaviour worth testing — that the second attempt waits longer
/// than the first, that two calls to one source are spaced — and a test that
/// proves it by actually sleeping takes as long as the policy says to wait.
/// </para>
/// <para>
/// A test substitute records what it was asked to wait for and returns
/// immediately, so the retry ladder can be asserted in milliseconds.
/// </para>
/// </remarks>
public interface IDelayScheduler
{
    /// <summary>
    /// Waits, or returns immediately when the duration is not positive.
    /// </summary>
    /// <param name="duration">How long to wait.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A task that completes when the wait is over.</returns>
    Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken = default);
}
