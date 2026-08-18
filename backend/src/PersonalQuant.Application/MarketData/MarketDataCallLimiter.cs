using System.Collections.Concurrent;
using PersonalQuant.Application.Abstractions;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Application.MarketData;

/// <summary>
/// Keeps calls to one source from being made faster than it allows.
/// </summary>
/// <remarks>
/// Per source, not global. Two providers have unrelated limits, and one shared
/// gate would either throttle the fast one to the slow one's rate or exceed
/// the slow one's.
/// </remarks>
public interface IMarketDataCallLimiter
{
    /// <summary>
    /// Waits until a call to a source is permitted.
    /// </summary>
    /// <param name="source">The source about to be called.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A task that completes when the call may be made.</returns>
    Task WaitForTurnAsync(SourceCode source, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="IMarketDataCallLimiter"/>: a minimum gap between
/// consecutive calls to the same source.
/// </summary>
/// <remarks>
/// <para>
/// Spacing rather than a quota over a window. It is what providers actually
/// enforce, it needs no bookkeeping that has to be aged out, and it cannot
/// produce the burst-then-stall pattern a windowed quota does — where a
/// backfill fires an hour's allowance in the first second and then waits
/// fifty-nine minutes.
/// </para>
/// <para>
/// Registered as a singleton: the gate is only meaningful if every caller
/// passes through the same one. Each source gets its own lock, so a slow
/// provider cannot hold up a fast one.
/// </para>
/// </remarks>
/// <param name="policy">Supplies the minimum spacing.</param>
/// <param name="clock">Reads the current instant.</param>
/// <param name="delays">Performs the wait.</param>
internal sealed class MarketDataCallLimiter(
    IngestionPolicy policy,
    IClock clock,
    IDelayScheduler delays) : IMarketDataCallLimiter, IDisposable
{
    private readonly ConcurrentDictionary<string, SourceGate> _gates = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public async Task WaitForTurnAsync(
        SourceCode source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (policy.MinimumCallSpacing <= TimeSpan.Zero)
        {
            return;
        }

        var gate = _gates.GetOrAdd(source.Value, static _ => new SourceGate());

        // The lock is held across the wait on purpose: it serialises callers
        // of one source so that two of them cannot both observe the same last
        // call time and then both proceed.
        await gate.Lock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var now = clock.UtcNow;

            if (gate.LastCallUtc is { } last)
            {
                var elapsed = now - last;
                var remaining = policy.MinimumCallSpacing - elapsed;

                if (remaining > TimeSpan.Zero)
                {
                    await delays.DelayAsync(remaining, cancellationToken).ConfigureAwait(false);
                    now = clock.UtcNow;
                }
            }

            gate.LastCallUtc = now;
        }
        finally
        {
            gate.Lock.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var gate in _gates.Values)
        {
            gate.Lock.Dispose();
        }

        _gates.Clear();
    }

    private sealed class SourceGate
    {
        public SemaphoreSlim Lock { get; } = new(1, 1);

        public DateTimeOffset? LastCallUtc { get; set; }
    }
}
