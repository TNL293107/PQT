using System.ComponentModel.DataAnnotations;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.MarketData;

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
    /// Gets or sets the CSV trading calendar to read, or an empty string to
    /// leave it unregistered.
    /// </summary>
    /// <remarks>
    /// Nothing is seeded in its place. Vietnam's calendar cannot be derived —
    /// Tet follows the lunar calendar and substitute days are set by annual
    /// decree — and a partial calendar is worse than none, because the system
    /// would believe it covers the year and report a week of real closures as
    /// missing sessions.
    /// </remarks>
    public string TradingCalendarPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the CSV corporate action file to read, or an empty string
    /// to leave it unregistered.
    /// </summary>
    /// <remarks>
    /// With no source, no action is recorded and every series reads back
    /// unadjusted — correct, and the same thing as a series with no actions,
    /// which is why the adjusted read reports how many factors it applied.
    /// </remarks>
    public string CorporateActionPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the directory holding the universe definition and
    /// membership CSV files, or an empty string to leave the source
    /// unregistered.
    /// </summary>
    /// <remarks>
    /// A directory rather than a path, because the source is two files that
    /// have to agree: what the universes are and which span of their history
    /// this directory claims to hold, and the history itself. With no source,
    /// no universe is recorded and every constituent read is unknown — which
    /// is the honest state, and the coverage review has nothing to review.
    /// </remarks>
    public string UniverseDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the base address of the Vietcap public chart endpoint, or
    /// an empty string to leave that source unregistered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Empty by default, like every other source. A deployment that has not
    /// been pointed at a provider calls nothing, which is the only safe
    /// default for a source that reaches a third party over the network.
    /// </para>
    /// <para>
    /// <strong>This source serves prices already adjusted for corporate
    /// actions.</strong> It declares that, and a series from it is a different
    /// dataset from a raw one — see ADR-015.
    /// </para>
    /// </remarks>
    public string VietcapBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the source code the scheduled ingestion pass reads from,
    /// or an empty string to let selection decide.
    /// </summary>
    /// <remarks>
    /// Empty is correct while exactly one registered source can serve the
    /// scheduled request. It stops being correct the moment two can: the pass
    /// names no source, selection reports the ambiguity rather than picking by
    /// registration order, and every run is skipped with both candidates
    /// named. Naming one here is how an operator resolves that — deliberately,
    /// in configuration, rather than by composition order.
    /// </remarks>
    public string IngestionSource { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the calendar and instrument
    /// imports run once at start-up.
    /// </summary>
    /// <remarks>
    /// Off by default, like migration and seeding, and for the same reason: a
    /// deployed environment should decide for itself when it reads an external
    /// source. Each import is skipped anyway if no source is configured for it.
    /// </remarks>
    public bool ImportReferenceDataOnStartup { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the host ingests market data on
    /// a timer.
    /// </summary>
    /// <remarks>
    /// Off by default. A fresh clone should not begin calling an external
    /// source because somebody started the API.
    /// </remarks>
    public bool IngestOnSchedule { get; set; }

    /// <summary>Gets or sets how often the ingestion loop wakes, in minutes.</summary>
    [Range(1, 10_080)]
    public int IngestionPeriodMinutes { get; set; } = 60;

    /// <summary>
    /// Gets or sets how long the loop waits before its first pass, in seconds.
    /// </summary>
    /// <remarks>
    /// Long enough for migration and seeding to finish. Ingesting against a
    /// schema that is still being created would fail every instrument on the
    /// first pass and log a wall of noise that means nothing.
    /// </remarks>
    [Range(0, 3_600)]
    public int IngestionStartupDelaySeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets the bar resolution the loop ingests, in minutes.
    /// </summary>
    /// <remarks>
    /// One resolution per deployment, and daily by default. A loop that
    /// ingested every resolution would multiply provider calls by six for data
    /// nothing reads yet.
    /// </remarks>
    [Range(1, 1_440)]
    public int IngestionBarIntervalMinutes { get; set; } = 1_440;

    /// <summary>
    /// Gets or sets how many instruments one pass may cover.
    /// </summary>
    /// <remarks>
    /// A bound, not a target. Each instrument is a provider call under the
    /// spacing policy, so an unbounded universe would make one pass longer
    /// than the period between passes.
    /// </remarks>
    [Range(1, 5_000)]
    public int IngestionUniverseLimit { get; set; } = 250;

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

    /// <summary>
    /// Returns the bar resolution the scheduled loop ingests.
    /// </summary>
    /// <remarks>
    /// Validated here rather than trusted: the setting is a number in a file,
    /// and a value that is not a declared resolution would otherwise reach the
    /// pipeline and be skipped once per instrument per pass.
    /// </remarks>
    /// <returns>The resolution.</returns>
    /// <exception cref="DomainValidationException">
    /// The configured value is not a resolution this system records.
    /// </exception>
    /// <summary>
    /// Reads the source the scheduled pass should name.
    /// </summary>
    /// <remarks>
    /// Returns false with a reason rather than throwing. The setting matters
    /// only while the schedule runs, and a stale or mistyped value must not be
    /// able to stop a deployment that had no intention of ingesting anything.
    /// </remarks>
    /// <param name="source">The parsed code, or null when none is configured.</param>
    /// <param name="problem">Why the configured value is unusable.</param>
    /// <returns><see langword="true"/> when the setting is usable or absent.</returns>
    public bool TryBuildIngestionSource(out SourceCode? source, out string? problem)
    {
        source = null;
        problem = null;

        if (string.IsNullOrWhiteSpace(IngestionSource))
        {
            // Naming none is correct while exactly one registered source can
            // serve the request.
            return true;
        }

        if (SourceCode.TryCreate(IngestionSource, out var parsed))
        {
            source = parsed;
            return true;
        }

        problem = $"'{IngestionSource}' is not a usable market data source code.";
        return false;
    }

    public BarInterval BuildIngestionInterval()
    {
        var interval = (BarInterval)IngestionBarIntervalMinutes;

        return interval.IsDeclared()
            ? interval
            : throw new DomainValidationException(
                $"{IngestionBarIntervalMinutes} minutes is not a bar resolution this system records.");
    }
}
