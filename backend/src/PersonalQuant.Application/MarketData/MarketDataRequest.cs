using System.Diagnostics.CodeAnalysis;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Application.MarketData;

/// <summary>
/// A validated request for one instrument's bars over one range.
/// </summary>
/// <remarks>
/// <para>
/// The unit of work the whole pipeline is built around: one instrument, one
/// resolution, one range, one source. Everything downstream — the raw batch,
/// the audit record, the checkpoint — is keyed the same way, so a run can
/// always be traced end to end.
/// </para>
/// <para>
/// The range is half-open: <c>FromUtc</c> is included and <c>ToUtc</c> is not.
/// Two adjacent requests therefore tile the timeline exactly, with no period
/// belonging to both and none belonging to neither. A closed range would
/// duplicate a bar at every seam.
/// </para>
/// <para>
/// It carries the ticker and exchange alongside the identifier because a
/// provider is addressed in its own symbology and knows nothing about this
/// system's keys — while everything the response turns into is stored against
/// the identifier.
/// </para>
/// </remarks>
public sealed record MarketDataRequest
{
    /// <summary>
    /// Most bars a single request may cover.
    /// </summary>
    /// <remarks>
    /// Providers page, rate-limit and time out. A caller asking for ten years
    /// of one-minute bars in one call is not going to get them, and letting the
    /// request be made means finding that out through a timeout rather than a
    /// clear refusal. Callers that want more chunk the range.
    /// </remarks>
    public const int MaxPeriods = 50_000;

    private MarketDataRequest(
        InstrumentId instrumentId,
        Ticker ticker,
        ExchangeCode exchangeCode,
        BarInterval interval,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc)
    {
        InstrumentId = instrumentId;
        Ticker = ticker;
        ExchangeCode = exchangeCode;
        Interval = interval;
        FromUtc = fromUtc;
        ToUtc = toUtc;
    }

    /// <summary>Gets the canonical instrument the bars will be stored against.</summary>
    public InstrumentId InstrumentId { get; }

    /// <summary>Gets the exchange ticker, for addressing the provider.</summary>
    public Ticker Ticker { get; }

    /// <summary>Gets the venue, for providers that need it to disambiguate.</summary>
    public ExchangeCode ExchangeCode { get; }

    /// <summary>Gets the resolution requested.</summary>
    public BarInterval Interval { get; }

    /// <summary>Gets the inclusive start of the range, in UTC.</summary>
    public DateTimeOffset FromUtc { get; }

    /// <summary>Gets the exclusive end of the range, in UTC.</summary>
    public DateTimeOffset ToUtc { get; }

    /// <summary>Gets how many periods the range spans.</summary>
    public int Periods => (int)((ToUtc - FromUtc).Ticks / Interval.ToDuration().Ticks);

    /// <summary>
    /// Validates a request.
    /// </summary>
    /// <param name="instrumentId">The instrument to fetch.</param>
    /// <param name="ticker">Its exchange ticker.</param>
    /// <param name="exchangeCode">Its venue.</param>
    /// <param name="interval">The resolution to fetch.</param>
    /// <param name="fromUtc">The inclusive start of the range.</param>
    /// <param name="toUtc">The exclusive end of the range.</param>
    /// <param name="request">The validated request when successful.</param>
    /// <param name="problem">A caller-safe explanation when validation fails.</param>
    /// <returns><see langword="true"/> when the request is usable.</returns>
    public static bool TryCreate(
        InstrumentId instrumentId,
        Ticker? ticker,
        ExchangeCode? exchangeCode,
        BarInterval interval,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        [NotNullWhen(true)] out MarketDataRequest? request,
        [NotNullWhen(false)] out string? problem)
    {
        request = null;

        if (instrumentId.IsEmpty)
        {
            problem = "An instrument is required.";
            return false;
        }

        if (ticker is null || exchangeCode is null)
        {
            problem = "A ticker and an exchange are required to address a provider.";
            return false;
        }

        if (!interval.IsDeclared())
        {
            problem = "The bar resolution is not one this system records.";
            return false;
        }

        if (fromUtc.Offset != TimeSpan.Zero || toUtc.Offset != TimeSpan.Zero)
        {
            problem = "The range must be expressed in UTC.";
            return false;
        }

        if (toUtc <= fromUtc)
        {
            problem = "The range must end after it starts.";
            return false;
        }

        // Aligning both edges is what makes the half-open range tile cleanly.
        // An unaligned edge would put a period partly inside two requests, and
        // deduplication would then depend on which ran first.
        if (!interval.IsAligned(fromUtc) || !interval.IsAligned(toUtc))
        {
            problem = "The range must start and end on a period boundary.";
            return false;
        }

        var periods = (toUtc - fromUtc).Ticks / interval.ToDuration().Ticks;

        if (periods > MaxPeriods)
        {
            problem = $"A request may not cover more than {MaxPeriods} periods.";
            return false;
        }

        request = new MarketDataRequest(
            instrumentId, ticker, exchangeCode, interval, fromUtc, toUtc);
        problem = null;
        return true;
    }

    /// <summary>
    /// Reports whether an instant falls inside the requested range.
    /// </summary>
    /// <param name="openedAtUtc">The instant to test.</param>
    /// <returns><see langword="true"/> when the instant is in range.</returns>
    public bool Covers(DateTimeOffset openedAtUtc) => openedAtUtc >= FromUtc && openedAtUtc < ToUtc;
}
