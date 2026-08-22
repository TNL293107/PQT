using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using PersonalQuant.Domain.Common;

namespace PersonalQuant.Domain.Exchanges;

/// <summary>
/// The furthest a security may move from its reference price in one session,
/// as a fraction.
/// </summary>
/// <remarks>
/// <para>
/// A structural feature of the Vietnamese market rather than a risk setting:
/// HOSE allows ±7%, HNX ±10% and UPCOM ±15%, and the exchange rejects orders
/// outside the band. That makes it the sharpest data-quality test available —
/// a close that moved further than the venue permits did not happen the way
/// the numbers claim.
/// </para>
/// <para>
/// Held as a fraction rather than a percentage so that arithmetic on it needs
/// no conversion. Sub-basis-point precision is refused: a limit is a published
/// round number, and a value carrying six decimals is a computation that has
/// been mistaken for a rule.
/// </para>
/// </remarks>
public readonly record struct PriceLimit
{
    /// <summary>Digits permitted after the decimal point of the fraction.</summary>
    /// <remarks>Four gives a basis point, which is finer than any venue publishes.</remarks>
    public const int MaxScale = 4;

    private PriceLimit(decimal fraction) => Fraction = fraction;

    /// <summary>Gets the limit as a fraction, so ±7% is <c>0.07</c>.</summary>
    public decimal Fraction { get; }

    /// <summary>Gets the limit as a percentage, so ±7% is <c>7</c>.</summary>
    public decimal Percent => Fraction * 100m;

    /// <summary>
    /// Creates a limit from a percentage, throwing when it is not one.
    /// </summary>
    /// <param name="percent">The published percentage, such as <c>7</c>.</param>
    /// <returns>The parsed limit.</returns>
    /// <exception cref="DomainValidationException">The value is not a usable limit.</exception>
    public static PriceLimit FromPercent(decimal percent) =>
        TryFromPercent(percent, out var limit)
            ? limit
            : throw new DomainValidationException(
                $"{percent.ToString(CultureInfo.InvariantCulture)} is not a valid daily price limit.");

    /// <summary>
    /// Attempts to create a limit from a percentage.
    /// </summary>
    /// <param name="percent">The published percentage.</param>
    /// <param name="limit">The parsed limit when successful.</param>
    /// <returns><see langword="true"/> when the value is a usable limit.</returns>
    public static bool TryFromPercent(decimal percent, out PriceLimit limit)
    {
        limit = default;

        // A limit of zero would halt the security, and one of a hundred per
        // cent or more is not a limit. Both are configuration errors rather
        // than market structure.
        if (percent is <= 0m or >= 100m)
        {
            return false;
        }

        var fraction = percent / 100m;

        if (fraction.Scale > MaxScale)
        {
            return false;
        }

        limit = new PriceLimit(fraction);
        return true;
    }

    /// <summary>
    /// Reports whether a move from one price to another stayed inside the
    /// band.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured against the previous close, which is the reference price the
    /// Vietnamese venues use. The comparison is inclusive: a security that
    /// closes exactly at its ceiling has not breached anything, and doing so
    /// is an ordinary day for a stock in demand.
    /// </para>
    /// <para>
    /// A tolerance is applied on top of the band. The reference price is
    /// rounded to a tick before the band is computed, so the realised move can
    /// exceed the nominal percentage by a fraction of a per cent without
    /// anything being wrong.
    /// </para>
    /// </remarks>
    /// <param name="previousClose">The previous session's close.</param>
    /// <param name="close">This session's close.</param>
    /// <param name="tolerance">
    /// Extra fractional room allowed for tick rounding, such as
    /// <c>0.005</c>.
    /// </param>
    /// <returns><see langword="true"/> when the move is within the band.</returns>
    public bool Permits(decimal previousClose, decimal close, decimal tolerance)
    {
        if (previousClose <= 0m)
        {
            // Nothing to measure against. A bar with no usable predecessor is
            // not a breach; it is a series that has just started.
            return true;
        }

        var move = Math.Abs((close - previousClose) / previousClose);

        return move <= Fraction + tolerance;
    }

    /// <inheritdoc />
    public override string ToString() =>
        $"±{Percent.ToString("0.##", CultureInfo.InvariantCulture)}%";
}
