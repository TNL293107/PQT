using PersonalQuant.Domain.CorporateActions;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Application.MarketData;

/// <summary>
/// One bar as a caller reads it, raw or rescaled for corporate actions.
/// </summary>
/// <remarks>
/// <para>
/// A projection rather than the <see cref="OhlcvBar"/> aggregate, and that is
/// the whole design of the adjusted series. Raw bars are never rewritten: the
/// factors are stored beside them and applied here, on the way out, so an
/// adjustment error is corrected by recomputing a handful of factor rows rather
/// than by rewriting a decade of prices.
/// </para>
/// <para>
/// Prices are plain decimals. An adjusted price is a computed quantity rather
/// than something that traded — a deep chain of factors can put it below the
/// smallest value a real price may hold — and forcing it through the
/// <see cref="Price"/> guard would mean either refusing to return a legitimate
/// historical series or quietly clamping it.
/// </para>
/// <para>
/// Turnover is never rescaled. It is the cash that actually changed hands, not
/// a per-share quantity, and multiplying it by a price factor would produce a
/// number that means nothing.
/// </para>
/// </remarks>
/// <param name="OpenedAtUtc">The instant the period opened.</param>
/// <param name="Open">The first traded price, rescaled when adjusted.</param>
/// <param name="High">The highest traded price, rescaled when adjusted.</param>
/// <param name="Low">The lowest traded price, rescaled when adjusted.</param>
/// <param name="Close">The last traded price, rescaled when adjusted.</param>
/// <param name="Volume">Units traded, rescaled when adjusted.</param>
/// <param name="Turnover">Cash value traded, always as recorded.</param>
/// <param name="Source">Where the bar came from.</param>
/// <param name="Revision">How many times the source has restated the period.</param>
/// <param name="PriceFactor">What the prices were multiplied by. One when raw.</param>
/// <param name="ShareFactor">What the volume was multiplied by. One when raw.</param>
public sealed record SeriesBar(
    DateTimeOffset OpenedAtUtc,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume,
    decimal? Turnover,
    SourceCode Source,
    int Revision,
    decimal PriceFactor,
    decimal ShareFactor)
{
    /// <summary>Gets a value indicating whether anything was rescaled.</summary>
    public bool IsAdjusted => PriceFactor != 1m || ShareFactor != 1m;

    /// <summary>
    /// Projects a bar exactly as it printed.
    /// </summary>
    /// <param name="bar">The stored bar.</param>
    /// <returns>The unrescaled projection.</returns>
    public static SeriesBar Raw(OhlcvBar bar)
    {
        ArgumentNullException.ThrowIfNull(bar);

        return new SeriesBar(
            bar.OpenedAtUtc,
            bar.Open.Value,
            bar.High.Value,
            bar.Low.Value,
            bar.Close.Value,
            bar.Volume,
            bar.Turnover,
            bar.Source,
            bar.Revision,
            1m,
            1m);
    }

    /// <summary>
    /// Projects a bar rescaled by the actions that came after it.
    /// </summary>
    /// <remarks>
    /// The four prices are multiplied by the same factor, so their ordering —
    /// and therefore the bar's own invariants — survives untouched. That is why
    /// a rescaled bar can never contradict itself the way a partially adjusted
    /// one would.
    /// </remarks>
    /// <param name="bar">The stored bar.</param>
    /// <param name="factor">The cumulative factor of every later action.</param>
    /// <returns>The rescaled projection.</returns>
    public static SeriesBar Adjusted(OhlcvBar bar, AdjustmentFactor factor)
    {
        ArgumentNullException.ThrowIfNull(bar);

        if (factor.IsIdentity)
        {
            return Raw(bar);
        }

        return new SeriesBar(
            bar.OpenedAtUtc,
            Rescale(bar.Open, factor),
            Rescale(bar.High, factor),
            Rescale(bar.Low, factor),
            Rescale(bar.Close, factor),
            factor.ApplyToVolume(bar.Volume),
            bar.Turnover,
            bar.Source,
            bar.Revision,
            factor.Price,
            factor.Shares);
    }

    private static decimal Rescale(Price price, AdjustmentFactor factor) =>
        decimal.Round(price.Value * factor.Price, Price.MaxScale, MidpointRounding.ToEven);
}
