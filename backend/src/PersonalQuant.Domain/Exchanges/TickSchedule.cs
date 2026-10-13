using PersonalQuant.Domain.Common;

namespace PersonalQuant.Domain.Exchanges;

/// <summary>
/// The smallest price step a venue quotes a security in, by price band.
/// </summary>
/// <remarks>
/// <para>
/// Read where a computed price has to become one the venue could print. The
/// case that needs it first is the reference price on an ex-date: the exchange
/// restates the previous close for the entitlement and rounds the result to the
/// tick, so a name that closes exactly at the published reference did not move.
/// Without the rounding, FPT's 47,500 restated for its 2016 entitlements is
/// 40,434.78, a price HOSE cannot quote, and a close at the 40,450 reference
/// would read as a rise of 0.04%.
/// </para>
/// <para>
/// The schedules are the venues' published equity rules, held in code like the
/// statutory price bands are seeded from code: HOSE quotes in 10 ₫ below
/// 10,000 ₫, 50 ₫ from 10,000 ₫ and 100 ₫ from 50,000 ₫; HNX and UPCOM quote
/// every share in 100 ₫. ETFs, covered warrants and bonds follow other steps,
/// and indices are calculated rather than quoted, so only equities are given a
/// schedule — anything else is left unrounded rather than rounded wrongly.
/// </para>
/// <para>
/// The reference is rounded to the <strong>nearest</strong> tick. That is not
/// written in the excerpts of HOSE's trading rules this system has seen, so it
/// is taken from reference prices HOSE actually set: HPG 28,933 → 28,950 and
/// 30,333 → 30,350 rule out rounding down, and VJC 97,928 → 97,900 rules out
/// rounding up (2017 ex-dates, worked at chungkhoanonline.com.vn). An exact
/// half tick is rounded up; no case of one has been seen.
/// </para>
/// </remarks>
public sealed class TickSchedule
{
    private static readonly TickSchedule Hose = new([(0m, 10m), (10_000m, 50m), (50_000m, 100m)]);

    private static readonly TickSchedule Hundred = new([(0m, 100m)]);

    private readonly IReadOnlyList<(decimal From, decimal Tick)> _bands;

    private TickSchedule(IReadOnlyList<(decimal From, decimal Tick)> bands) => _bands = bands;

    /// <summary>
    /// Gets the equity schedule of a venue, when this system knows it.
    /// </summary>
    /// <param name="venue">The venue.</param>
    /// <returns>The schedule, or <see langword="null"/> for a venue nobody transcribed.</returns>
    public static TickSchedule? ForEquitiesOn(ExchangeCode venue)
    {
        ArgumentNullException.ThrowIfNull(venue);

        return venue.Value switch
        {
            "HOSE" => Hose,
            "HNX" or "UPCOM" => Hundred,
            _ => null,
        };
    }

    /// <summary>Gets the step a price is quoted in.</summary>
    /// <param name="price">A positive price.</param>
    /// <returns>The tick of the band the price falls in.</returns>
    /// <exception cref="DomainValidationException">The price is not positive.</exception>
    public decimal TickAt(decimal price)
    {
        if (price <= 0m)
        {
            throw new DomainValidationException("A tick is only defined for a positive price.");
        }

        return _bands.Last(band => price >= band.From).Tick;
    }

    /// <summary>Rounds a price to the nearest tick of its band.</summary>
    /// <remarks>
    /// The band is chosen by the unrounded price. A price just under a band
    /// boundary can round onto it — 49,980 becomes 50,000 — which is a price
    /// the upper band quotes too, so the result is always quotable.
    /// </remarks>
    /// <param name="price">A positive price.</param>
    /// <returns>The nearest quotable price; an exact half tick rounds up.</returns>
    /// <exception cref="DomainValidationException">The price is not positive.</exception>
    public decimal RoundToTick(decimal price)
    {
        var tick = TickAt(price);

        return decimal.Round(price / tick, 0, MidpointRounding.AwayFromZero) * tick;
    }
}
