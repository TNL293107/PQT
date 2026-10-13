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
public sealed class MarketDataOptions : IValidatableObject
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
    /// Gets or sets the CSV declaring how far the trading calendar was
    /// transcribed, per venue, or an empty string to declare nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Separate from the calendar itself because it answers a different
    /// question. The closures say which days a venue was shut; this says which
    /// span somebody actually went through and transcribed, and only the second
    /// can tell a year with no holidays from a year nobody has read.
    /// </para>
    /// <para>
    /// Empty means no claim, and every completeness figure then reports
    /// unmeasurable. That is the honest state — and better than the guess it
    /// replaces, which read the furthest recorded closure as the calendar's
    /// reach and was wrong in both directions.
    /// </para>
    /// </remarks>
    public string TradingCalendarCoveragePath { get; set; } = string.Empty;

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
    /// Gets or sets the CSV of the venues instruments traded on over time, or
    /// an empty string to leave it unregistered.
    /// </summary>
    /// <remarks>
    /// With no source, every instrument is judged by its current venue for every
    /// session — right for a name that never moved, and wrong by the band's
    /// width for one that did.
    /// </remarks>
    public string VenueHistoryPath { get; set; } = string.Empty;

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
    /// Gets or sets the base address of the CafeF public price-history
    /// endpoint, or an empty string to leave that source unregistered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Empty by default, like every other source. A deployment that has not
    /// been pointed at a provider calls nothing, which is the only safe default
    /// for a source that reaches a third party over the network.
    /// </para>
    /// <para>
    /// <strong>This source serves raw prices.</strong> It is the one the schema
    /// was designed for, and it cannot share a series with the adjusted source
    /// above — ingestion refuses that, in both directions. See ADR-021.
    /// </para>
    /// </remarks>
    public string CafefBaseUrl { get; set; } = string.Empty;

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
    /// Gets or sets how long one ingestion pass may hold its series before the
    /// lock expires on its own, in seconds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The expiry exists for the process that dies holding the lock — killed,
    /// or cut off from Redis before it could release. Without one, a crashed
    /// pass would keep its series closed until somebody deleted the key by hand.
    /// </para>
    /// <para>
    /// Ten minutes, because a pass is one provider call. Under the defaults its
    /// worst case is three attempts of thirty seconds with two backoffs of at
    /// most thirty — two and a half minutes — plus the database work, which is
    /// seconds. Ten minutes is four times that, so a slow pass is never
    /// overtaken by a second one, and short enough that a crashed pass blocks
    /// the series for less than one default schedule period. A
    /// backfill is many passes and takes the lock once per pass, so its length
    /// does not count against this.
    /// </para>
    /// <para>
    /// The pass is not able to renew the lock, so the expiry must exceed the
    /// longest pass the retry policy permits. That is checked against the
    /// policy here rather than left to whoever raises the timeout.
    /// </para>
    /// </remarks>
    [Range(30, 86_400)]
    public int IngestionLockTtlSeconds { get; set; } = 600;

    /// <summary>
    /// Returns how long a pass may hold its series.
    /// </summary>
    /// <returns>The lock expiry.</returns>
    public TimeSpan BuildIngestionLockTtl() => TimeSpan.FromSeconds(IngestionLockTtlSeconds);

    /// <summary>
    /// Returns the longest one pass can spend calling its source under the
    /// configured retry policy.
    /// </summary>
    /// <remarks>
    /// Every attempt timing out, with the longest permitted wait between each,
    /// and the call spacing the limiter may wait before every attempt — which
    /// a setting allows up to a minute, and so cannot be dismissed as small.
    /// </remarks>
    /// <returns>The worst-case fetch duration.</returns>
    public TimeSpan LongestFetch() =>
        ((TimeSpan.FromSeconds(ProviderTimeoutSeconds)
            + TimeSpan.FromMilliseconds(MinimumCallSpacingMilliseconds)) * MaxAttempts)
        + (TimeSpan.FromMilliseconds(MaxBackoffMilliseconds) * (MaxAttempts - 1));

    /// <inheritdoc />
    /// <remarks>
    /// A lock that expires mid-pass admits a second pass over the same series
    /// while the first is still writing — the race the lock exists to stop,
    /// arriving silently and only on the slowest days. The bound is a setting
    /// that has to agree with three others, so it is checked against them
    /// rather than trusted.
    /// </remarks>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // Twice the fetch, not just more than it: after the source answers, the
        // pass still validates, merges and commits, and the lease is never
        // renewed. A bound with no margin is one that holds until the slowest
        // day, which is the day it matters.
        var required = LongestFetch() * 2;

        if (BuildIngestionLockTtl() < required)
        {
            yield return new ValidationResult(
                $"IngestionLockTtlSeconds ({IngestionLockTtlSeconds}) must be at least {required.TotalSeconds:0}s: "
                    + "twice the longest a pass may spend calling its source (MaxAttempts x "
                    + "(ProviderTimeoutSeconds + the call spacing), plus the backoffs between them), "
                    + "leaving room for the writes after it. Otherwise the lock can expire while "
                    + "the pass is still writing.",
                [nameof(IngestionLockTtlSeconds)]);
        }
    }

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
    /// Gets or sets the tickers the scheduled pass may ingest, comma
    /// separated, or an empty string for every listed instrument.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The guard that keeps a first run against a real provider to the
    /// securities it was meant to cover. The ingestion universe is otherwise
    /// every listed instrument, and reference-data seeding alone puts ten HOSE
    /// equities in the master — so an unrestricted first pass fetches and
    /// stores years of a third party's data for nine securities nobody asked
    /// about.
    /// </para>
    /// <para>
    /// A limit by count cannot express this: it depends on an ordering nobody
    /// chose, and the instrument it happens to keep is whichever sorts first.
    /// </para>
    /// </remarks>
    public string IngestionTickers { get; set; } = string.Empty;

    /// <summary>
    /// Reads the tickers the scheduled pass is restricted to.
    /// </summary>
    /// <param name="tickers">
    /// The set to keep, upper-cased, or empty when the pass is unrestricted.
    /// </param>
    /// <returns><see langword="true"/> when at least one ticker is named.</returns>
    public bool TryBuildIngestionTickers(out IReadOnlySet<string> tickers)
    {
        var named = IngestionTickers
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(entry => entry.ToUpperInvariant())
            .ToHashSet(StringComparer.Ordinal);

        tickers = named;
        return named.Count > 0;
    }

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
