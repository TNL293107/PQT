using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Domain.CorporateActions;

/// <summary>
/// What one corporate action does to the prices and volumes recorded before
/// its ex-date.
/// </summary>
/// <remarks>
/// <para>
/// Two numbers, not one, because a corporate action can move the price without
/// changing the share count and vice versa. A cash dividend lowers the price
/// and leaves volume alone; a two-for-one split halves the price and doubles
/// the shares. Collapsing them would make every split's volume series wrong by
/// exactly the split ratio.
/// </para>
/// <para>
/// Both are multipliers applied to <em>historical</em> data:
/// <c>adjusted = raw × factor</c> for every bar opening before the ex-date. A
/// bar on or after the ex-date is already ex and is left as it printed.
/// </para>
/// </remarks>
public readonly record struct AdjustmentFactor
{
    /// <summary>Digits kept on a factor.</summary>
    /// <remarks>
    /// Ten, which is more than a published ratio ever carries and enough that
    /// the rounding of a long chain of factors stays far below a tick. The
    /// factors are multiplied together across a decade, so precision lost here
    /// compounds.
    /// </remarks>
    public const int Scale = 10;

    private AdjustmentFactor(decimal price, decimal shares)
    {
        Price = price;
        Shares = shares;
    }

    /// <summary>A factor that changes nothing.</summary>
    public static AdjustmentFactor Identity { get; } = new(1m, 1m);

    /// <summary>Gets the multiplier applied to prices recorded before the ex-date.</summary>
    public decimal Price { get; }

    /// <summary>Gets the multiplier applied to volumes recorded before the ex-date.</summary>
    public decimal Shares { get; }

    /// <summary>Gets a value indicating whether the factor leaves the series unchanged.</summary>
    public bool IsIdentity => Price == 1m && Shares == 1m;

    /// <summary>
    /// Creates a factor, throwing when either multiplier is unusable.
    /// </summary>
    /// <param name="price">The price multiplier.</param>
    /// <param name="shares">The share multiplier.</param>
    /// <returns>The factor.</returns>
    /// <exception cref="DomainValidationException">A multiplier is not usable.</exception>
    public static AdjustmentFactor Create(decimal price, decimal shares) =>
        TryCreate(price, shares, out var factor)
            ? factor
            : throw new DomainValidationException(
                $"({Format(price)}, {Format(shares)}) is not a usable adjustment factor.");

    /// <summary>
    /// Attempts to create a factor.
    /// </summary>
    /// <param name="price">The price multiplier.</param>
    /// <param name="shares">The share multiplier.</param>
    /// <param name="factor">The factor when successful.</param>
    /// <returns><see langword="true"/> when both multipliers are usable.</returns>
    public static bool TryCreate(decimal price, decimal shares, out AdjustmentFactor factor)
    {
        factor = default;

        // Strictly positive. A factor of zero would erase the history it was
        // meant to rescale, and a negative one has no meaning at all.
        if (price <= 0m || shares <= 0m)
        {
            return false;
        }

        factor = new AdjustmentFactor(Round(price), Round(shares));
        return true;
    }

    /// <summary>
    /// Combines this factor with another, for two actions on the same series.
    /// </summary>
    /// <remarks>
    /// Multiplication, and it commutes — which is what makes a day carrying
    /// both a cash dividend and a stock dividend produce the same series
    /// whichever order the two are applied in. Vietnamese issuers pair them
    /// routinely.
    /// </remarks>
    /// <param name="other">The factor to combine with.</param>
    /// <returns>The combined factor.</returns>
    public AdjustmentFactor Combine(AdjustmentFactor other) =>
        new(Round(Price * other.Price), Round(Shares * other.Shares));

    /// <summary>
    /// Applies the price multiplier, refusing a result that is not a price.
    /// </summary>
    /// <remarks>
    /// A deep adjustment chain can drive an old, small price below the smallest
    /// value a price may hold. Reporting that rather than rounding to zero is
    /// the difference between a visible limit and a series that quietly becomes
    /// free.
    /// </remarks>
    /// <param name="raw">The price as it printed.</param>
    /// <param name="adjusted">The rescaled price when it is still a price.</param>
    /// <returns><see langword="true"/> when the result is usable.</returns>
    public bool TryApply(Price raw, [NotNullWhen(true)] out Price adjusted) =>
        // Fully qualified: the price multiplier is called Price too, and inside
        // this type the property wins over the type it multiplies.
        MarketData.Price.TryCreate(
            decimal.Round(raw.Value * Price, MarketData.Price.MaxScale, MidpointRounding.ToEven),
            out adjusted);

    /// <summary>
    /// Applies the share multiplier to a volume.
    /// </summary>
    /// <remarks>
    /// Rounded to a whole number: a volume is a count of shares, and half a
    /// share never traded.
    /// </remarks>
    /// <param name="raw">The volume as it printed.</param>
    /// <returns>The rescaled volume.</returns>
    public long ApplyToVolume(long raw) =>
        (long)decimal.Round(raw * Shares, 0, MidpointRounding.AwayFromZero);

    /// <inheritdoc />
    public override string ToString() =>
        $"price ×{Format(Price)}, shares ×{Format(Shares)}";

    private static decimal Round(decimal value) =>
        decimal.Round(value, Scale, MidpointRounding.ToEven);

    private static string Format(decimal value) =>
        value.ToString("0.##########", CultureInfo.InvariantCulture);
}
