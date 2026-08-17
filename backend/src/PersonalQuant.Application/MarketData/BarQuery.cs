using System.Diagnostics.CodeAnalysis;
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
        int limit)
    {
        InstrumentId = instrumentId;
        Interval = interval;
        FromUtc = fromUtc;
        ToUtc = toUtc;
        Limit = limit;
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
    /// Validates a read request.
    /// </summary>
    /// <param name="instrumentId">The instrument to read.</param>
    /// <param name="interval">The resolution to read.</param>
    /// <param name="fromUtc">The inclusive start of the window, or null.</param>
    /// <param name="toUtc">The exclusive end of the window, or null.</param>
    /// <param name="limit">The requested bound, or null for <see cref="DefaultLimit"/>.</param>
    /// <param name="query">The validated query when successful.</param>
    /// <param name="problem">A caller-safe explanation when validation fails.</param>
    /// <returns><see langword="true"/> when the request is usable.</returns>
    public static bool TryCreate(
        InstrumentId instrumentId,
        BarInterval interval,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        int? limit,
        [NotNullWhen(true)] out BarQuery? query,
        [NotNullWhen(false)] out string? problem)
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

        query = new BarQuery(instrumentId, interval, normalisedFrom, normalisedTo, resolvedLimit);
        problem = null;
        return true;
    }
}
