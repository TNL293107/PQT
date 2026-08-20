using System.ComponentModel.DataAnnotations;
using PersonalQuant.Application.MarketData;

namespace PersonalQuant.Infrastructure.Configuration;

/// <summary>
/// Market data ingestion settings, bound from the <c>MarketData</c>
/// configuration section and validated at application start.
/// </summary>
/// <remarks>
/// <para>
/// Durations are expressed as plain numbers rather than as
/// <see cref="TimeSpan"/>. Configuration binding of a TimeSpan depends on the
/// string format used in the file, and a value silently bound as zero is a
/// retry policy that never waits.
/// </para>
/// <para>
/// This class validates; <see cref="IngestionPolicy"/> is what the pipeline
/// uses. Keeping them separate is what allows the application layer to hold no
/// configuration dependency at all.
/// </para>
/// </remarks>
public sealed class MarketDataOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "MarketData";

    /// <summary>Gets or sets how many times one request may be attempted.</summary>
    [Range(1, 10)]
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Gets or sets the wait before the second attempt, in milliseconds.</summary>
    [Range(1, 60_000)]
    public int InitialBackoffMilliseconds { get; set; } = 500;

    /// <summary>Gets or sets the factor each subsequent wait is multiplied by.</summary>
    [Range(1.0, 10.0)]
    public double BackoffMultiplier { get; set; } = 2.0;

    /// <summary>Gets or sets the longest wait between attempts, in milliseconds.</summary>
    [Range(1, 300_000)]
    public int MaxBackoffMilliseconds { get; set; } = 30_000;

    /// <summary>Gets or sets how long a single provider call may take, in seconds.</summary>
    [Range(1, 600)]
    public int ProviderTimeoutSeconds { get; set; } = 30;

    /// <summary>Gets or sets the minimum gap between calls to one source, in milliseconds.</summary>
    [Range(0, 60_000)]
    public int MinimumCallSpacingMilliseconds { get; set; } = 200;

    /// <summary>Gets or sets how far back a first run reaches, in days.</summary>
    [Range(1, 10_000)]
    public int InitialBackfillDays { get; set; } = 365;

    /// <summary>
    /// Gets or sets the directory the file provider reads, or an empty string
    /// to leave it unregistered.
    /// </summary>
    /// <remarks>
    /// Empty by default. A deployment with no provider configured ingests
    /// nothing and records a skipped run saying so, which is a better default
    /// than silently reading whatever happens to be in a conventional path.
    /// </remarks>
    public string FileProviderDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the CSV symbol list the file instrument source reads, or
    /// an empty string to leave it unregistered.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="FileProviderDirectory"/> because the two are
    /// separate sources. A deployment may have a symbol list and no prices, or
    /// prices and no symbol list, and one setting for both would force it to
    /// pretend otherwise.
    /// </remarks>
    public string InstrumentListPath { get; set; } = string.Empty;

    /// <summary>
    /// Converts the validated settings into the policy the pipeline uses.
    /// </summary>
    /// <returns>The ingestion policy.</returns>
    public IngestionPolicy BuildPolicy() =>
        new IngestionPolicy
        {
            MaxAttempts = MaxAttempts,
            InitialBackoff = TimeSpan.FromMilliseconds(InitialBackoffMilliseconds),
            BackoffMultiplier = BackoffMultiplier,
            MaxBackoff = TimeSpan.FromMilliseconds(MaxBackoffMilliseconds),
            ProviderTimeout = TimeSpan.FromSeconds(ProviderTimeoutSeconds),
            MinimumCallSpacing = TimeSpan.FromMilliseconds(MinimumCallSpacingMilliseconds),
            InitialBackfill = TimeSpan.FromDays(InitialBackfillDays),
        }.Validated();
}
