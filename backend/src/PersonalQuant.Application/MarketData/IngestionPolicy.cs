using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Application.MarketData;

/// <summary>
/// How hard the pipeline tries, how fast it is allowed to ask, and how far
/// back it starts.
/// </summary>
/// <remarks>
/// <para>
/// One object rather than six constructor parameters spread across the
/// pipeline, because these settings are only meaningful together: a retry
/// budget without a timeout is unbounded, and a timeout without call spacing
/// is a way to be rate-limited faster.
/// </para>
/// <para>
/// It lives in the application layer and holds no configuration binding of its
/// own. Infrastructure reads settings and hands a validated instance in, which
/// keeps the layer that decides the policy separate from the one that reads it
/// off disk.
/// </para>
/// </remarks>
public sealed record IngestionPolicy
{
    /// <summary>The settings used when nothing overrides them.</summary>
    public static IngestionPolicy Default { get; } = new();

    /// <summary>
    /// Gets how many times a single request may be attempted, including the
    /// first.
    /// </summary>
    /// <remarks>
    /// Three, because the failures worth retrying are brief — a dropped
    /// connection, a rate-limit response, a restart at the provider — and a
    /// source that is genuinely down is not persuaded by a fourth attempt. It
    /// is persuaded by the run being recorded as failed and re-run later.
    /// </remarks>
    public int MaxAttempts { get; init; } = 3;

    /// <summary>Gets the wait before the second attempt.</summary>
    public TimeSpan InitialBackoff { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Gets the factor each subsequent wait is multiplied by.</summary>
    public double BackoffMultiplier { get; init; } = 2.0;

    /// <summary>Gets the longest wait between attempts.</summary>
    public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets how long a single provider call may take before it is abandoned.
    /// </summary>
    /// <remarks>
    /// Without this a provider that accepts a connection and then never
    /// answers holds the run open indefinitely, and a nightly schedule quietly
    /// stops producing data while every component reports itself healthy.
    /// </remarks>
    public TimeSpan ProviderTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets the minimum gap between two calls to the same source.
    /// </summary>
    /// <remarks>
    /// Rate limiting expressed as spacing rather than as a quota, because
    /// spacing is what a provider actually enforces and it needs no window
    /// bookkeeping. Backfilling an instrument universe is a loop that would
    /// otherwise issue hundreds of calls in a second and be refused for the
    /// rest of the hour.
    /// </remarks>
    public TimeSpan MinimumCallSpacing { get; init; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Gets how far back a first run reaches when there is no checkpoint.
    /// </summary>
    /// <remarks>
    /// Only ever used once per instrument, interval and source. Every run
    /// after it resumes from where the last one stopped.
    /// </remarks>
    public TimeSpan InitialBackfill { get; init; } = TimeSpan.FromDays(365);

    /// <summary>
    /// Validates the settings, throwing when they cannot be used.
    /// </summary>
    /// <remarks>
    /// Called once at composition. A policy validated at start-up fails a
    /// deployment; one validated on first use fails a scheduled job at 2am.
    /// </remarks>
    /// <returns>The same policy, so it can be used in an expression.</returns>
    /// <exception cref="DomainValidationException">A setting is unusable.</exception>
    public IngestionPolicy Validated()
    {
        if (MaxAttempts is < 1 or > 10)
        {
            throw new DomainValidationException("Ingestion must allow between 1 and 10 attempts.");
        }

        if (InitialBackoff <= TimeSpan.Zero || InitialBackoff > MaxBackoff)
        {
            throw new DomainValidationException(
                "The initial backoff must be positive and no longer than the maximum backoff.");
        }

        if (BackoffMultiplier < 1.0)
        {
            throw new DomainValidationException("The backoff multiplier may not shrink the wait.");
        }

        if (ProviderTimeout <= TimeSpan.Zero)
        {
            throw new DomainValidationException("The provider timeout must be positive.");
        }

        if (MinimumCallSpacing < TimeSpan.Zero)
        {
            throw new DomainValidationException("The minimum call spacing may not be negative.");
        }

        return InitialBackfill <= TimeSpan.Zero
            ? throw new DomainValidationException("The initial backfill must be positive.")
            : this;
    }

    /// <summary>
    /// Returns how long to wait before an attempt.
    /// </summary>
    /// <remarks>
    /// Exponential and capped. There is deliberately no jitter: a single
    /// process ingesting one instrument at a time has no thundering herd to
    /// spread, and a non-deterministic delay would make the retry path
    /// untestable for no benefit. Jitter belongs here the day ingestion runs
    /// in parallel across a universe.
    /// </remarks>
    /// <param name="attempt">The attempt about to be made, counting from one.</param>
    /// <returns>The wait before it, or zero for the first attempt.</returns>
    public TimeSpan BackoffBefore(int attempt)
    {
        if (attempt <= 1)
        {
            return TimeSpan.Zero;
        }

        var scaled = InitialBackoff * Math.Pow(BackoffMultiplier, attempt - 2);

        return scaled > MaxBackoff ? MaxBackoff : scaled;
    }

    /// <summary>
    /// Returns the first instant a run should reach back to when nothing has
    /// been ingested yet.
    /// </summary>
    /// <param name="interval">The resolution being ingested.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <returns>An instant on a period boundary.</returns>
    public DateTimeOffset InitialFrom(BarInterval interval, DateTimeOffset nowUtc) =>
        FloorTo(nowUtc - InitialBackfill, interval);

    /// <summary>
    /// Rounds an instant down to the start of the period containing it.
    /// </summary>
    /// <param name="instant">The instant to round.</param>
    /// <param name="interval">The resolution to round to.</param>
    /// <returns>The period's opening instant, in UTC.</returns>
    public static DateTimeOffset FloorTo(DateTimeOffset instant, BarInterval interval)
    {
        var ticks = interval.ToDuration().Ticks;
        var utc = instant.ToUniversalTime();

        return new DateTimeOffset(utc.UtcTicks - (utc.UtcTicks % ticks), TimeSpan.Zero);
    }
}
