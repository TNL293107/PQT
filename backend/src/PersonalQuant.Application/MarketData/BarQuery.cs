using System.Diagnostics.CodeAnalysis;
using PersonalQuant.Domain.CorporateActions;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Application.MarketData;

/// <summary>
/// A validated request to read part of a series.
/// </summary>
/// <remarks>
/// <para>
/// Bounded by construction, for the reason instrument search is: this is
/// reachable from an anonymous caller, and a series is the one table in the
/// system that grows without limit. An unbounded read of one instrument's
/// one-minute history is a way to make the database do arbitrary work on
/// request.
/// </para>
/// <para>
/// The range is optional and the bound is not. Asking for "the last 300 daily
/// bars" is the common case and needs no dates; asking for a specific window
/// still cannot ask for more than the bound allows.
/// </para>
/// </remarks>
public sealed record BarQuery
{
    /// <summary>Bars returned when the caller does not ask for a specific number.</summary>
    public const int DefaultLimit = 300;

    /// <summary>Most bars a caller may request in one read.</summary>
    public const int MaxLimit = 5_000;

    private BarQuery(
        InstrumentId instrumentId,
        BarInterval interval,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        int limit,
        bool adjusted,
        DateTimeOffset? knownAsOfUtc,
        AnnouncementPolicy announcementPolicy)
    {
        InstrumentId = instrumentId;
        Interval = interval;
        FromUtc = fromUtc;
        ToUtc = toUtc;
        Limit = limit;
        Adjusted = adjusted;
        KnownAsOfUtc = knownAsOfUtc;
        AnnouncementPolicy = announcementPolicy;
    }

    /// <summary>Gets the instrument to read.</summary>
    public InstrumentId InstrumentId { get; }

    /// <summary>Gets the resolution to read.</summary>
    public BarInterval Interval { get; }

    /// <summary>Gets the inclusive start of the window, when one was given.</summary>
    public DateTimeOffset? FromUtc { get; }

    /// <summary>Gets the exclusive end of the window, when one was given.</summary>
    public DateTimeOffset? ToUtc { get; }

    /// <summary>Gets the maximum number of bars to return.</summary>
    public int Limit { get; }

    /// <summary>
    /// Gets a value indicating whether prices are rescaled for corporate
    /// actions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Defaults to <see langword="true"/>, and the default is the point of the
    /// phase. An unadjusted series makes every return computed across a split
    /// silently wrong, and a caller who has not thought about it should get the
    /// series that is right rather than the one that printed.
    /// </para>
    /// <para>
    /// A caller who genuinely wants what printed — reconciling against a
    /// broker statement, or checking a price the exchange published — asks for
    /// it, and the answer says which it returned.
    /// </para>
    /// </remarks>
    public bool Adjusted { get; }

    /// <summary>
    /// Gets the observation instant to read the series as of, or
    /// <see langword="null"/> for the current values.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Observation time, not event time. It selects <em>what this system
    /// believed</em> at that instant, which is the only reading a backtest may
    /// use: a strategy simulated on a Tuesday cannot see a correction the
    /// provider published on the Friday.
    /// </para>
    /// <para>
    /// <see langword="null"/> is the current series and is byte-identical to
    /// the behaviour before point-in-time reads existed. An instant earlier
    /// than a period's first observation yields no bar for that period — never
    /// the current value, which would be exactly the leak this exists to stop.
    /// </para>
    /// <para>
    /// Corporate actions are filtered by announcement date against this same
    /// instant, so an adjusted as-of read is point-in-time in its prices
    /// <em>and</em> in its adjustments. Which reading of an unknown
    /// announcement date it used is
    /// <see cref="AnnouncementPolicy"/>'s, and the answer states it. See
    /// ADR-018 and ADR-022.
    /// </para>
    /// </remarks>
    public DateTimeOffset? KnownAsOfUtc { get; }

    /// <summary>
    /// Gets what an as-of read does with an action whose announcement date is
    /// unknown.
    /// </summary>
    /// <remarks>
    /// Has no effect without <see cref="KnownAsOfUtc"/>: a read of the current
    /// series applies everything this system currently knows, which is what
    /// "current" means, so there is nothing for the policy to decide.
    /// </remarks>
    public AnnouncementPolicy AnnouncementPolicy { get; }

    /// <summary>
    /// Validates a read request.
    /// </summary>
    /// <param name="instrumentId">The instrument to read.</param>
    /// <param name="interval">The resolution to read.</param>
    /// <param name="fromUtc">The inclusive start of the window, or null.</param>
    /// <param name="toUtc">The exclusive end of the window, or null.</param>
    /// <param name="limit">The requested bound, or null for <see cref="DefaultLimit"/>.</param>
    /// <param name="query">The validated query when successful.</param>
    /// <param name="problem">A caller-safe explanation when validation fails.</param>
    /// <param name="adjusted">Whether to rescale for corporate actions.</param>
    /// <returns><see langword="true"/> when the request is usable.</returns>
    public static bool TryCreate(
        InstrumentId instrumentId,
        BarInterval interval,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        int? limit,
        [NotNullWhen(true)] out BarQuery? query,
        [NotNullWhen(false)] out string? problem,
        bool adjusted = true,
        DateTimeOffset? knownAsOfUtc = null,
        AnnouncementPolicy announcementPolicy = AnnouncementPolicyExtensions.Default)
    {
        query = null;

        if (instrumentId.IsEmpty)
        {
            problem = "An instrument is required.";
            return false;
        }

        if (!interval.IsDeclared())
        {
            problem = "The bar resolution is not one this system records.";
            return false;
        }

        if (!announcementPolicy.IsDeclared())
        {
            problem = "The announcement policy is not one this system understands.";
            return false;
        }

        var resolvedLimit = limit ?? DefaultLimit;

        if (resolvedLimit is < 1 or > MaxLimit)
        {
            problem = $"The bar limit must be between 1 and {MaxLimit}.";
            return false;
        }

        var normalisedFrom = fromUtc?.ToUniversalTime();
        var normalisedTo = toUtc?.ToUniversalTime();

        if (normalisedFrom is { } start && normalisedTo is { } end && end <= start)
        {
            problem = "The window must end after it starts.";
            return false;
        }

        query = new BarQuery(
            instrumentId,
            interval,
            normalisedFrom,
            normalisedTo,
            resolvedLimit,
            adjusted,
            knownAsOfUtc?.ToUniversalTime(),
            announcementPolicy);
        problem = null;
        return true;
    }
}
