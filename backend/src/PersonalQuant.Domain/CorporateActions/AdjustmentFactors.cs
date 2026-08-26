using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Domain.CorporateActions;

/// <summary>
/// Turns a corporate action into the factor that rescales the prices before it.
/// </summary>
/// <remarks>
/// <para>
/// The arithmetic of the whole phase, in one place and free of any dependency,
/// so it can be checked against a worked example rather than inferred from a
/// pipeline. Every formula below is the standard one; what makes them easy to
/// get wrong is not the algebra but the meaning of the ratio each type carries.
/// </para>
/// <para>
/// Two of the five need the previous close. A split rescales by a published
/// ratio and needs nothing else, but a cash dividend and a rights issue move
/// the price by an amount that depends on what the price was — which is why the
/// factor is computed once, from the close that was in front of the ex-date,
/// and then stored.
/// </para>
/// </remarks>
public static class AdjustmentFactors
{
    /// <summary>
    /// Computes the factor an action implies.
    /// </summary>
    /// <param name="action">The action.</param>
    /// <param name="previousClose">
    /// The last close recorded before the ex-date, which the cash-based types
    /// measure against.
    /// </param>
    /// <param name="factor">The factor when one could be computed.</param>
    /// <param name="problem">A caller-safe explanation when one could not.</param>
    /// <returns><see langword="true"/> when a factor was computed.</returns>
    public static bool TryCompute(
        CorporateAction action,
        Price previousClose,
        out AdjustmentFactor factor,
        [NotNullWhen(false)] out string? problem)
    {
        ArgumentNullException.ThrowIfNull(action);

        factor = AdjustmentFactor.Identity;
        problem = null;

        if (action.IsCancelled)
        {
            problem = "The action was cancelled and rescales nothing.";
            return false;
        }

        if (!action.Type.AffectsPrice())
        {
            problem = $"A {action.Type} does not rescale historical prices.";
            return false;
        }

        return action.Type switch
        {
            CorporateActionType.CashDividend => TryCashDividend(action, previousClose, out factor, out problem),
            CorporateActionType.RightsIssue => TryRightsIssue(action, previousClose, out factor, out problem),
            CorporateActionType.StockSplit or CorporateActionType.ReverseSplit =>
                TrySplit(action, out factor, out problem),
            _ => TryShareDistribution(action, out factor, out problem),
        };
    }

    /// <summary>
    /// A dividend lowers the price by the cash leaving the company and changes
    /// no share count.
    /// </summary>
    /// <remarks>
    /// <c>price × (P − D) / P</c>, shares unchanged. A dividend at or above the
    /// previous close is refused: the arithmetic would produce a factor of zero
    /// or a negative one, and the real explanation is a dividend recorded in
    /// the wrong unit — dong per share against a price in thousands, which is a
    /// mistake Vietnamese data invites.
    /// </remarks>
    private static bool TryCashDividend(
        CorporateAction action,
        Price previousClose,
        out AdjustmentFactor factor,
        [NotNullWhen(false)] out string? problem)
    {
        factor = AdjustmentFactor.Identity;

        var dividend = action.CashAmount!.Value;

        if (dividend >= previousClose.Value)
        {
            problem =
                $"A dividend of {Format(dividend)} is not below the previous close of {Format(previousClose.Value)}. "
                + "The two are probably in different units.";
            return false;
        }

        problem = null;
        factor = AdjustmentFactor.Create(
            (previousClose.Value - dividend) / previousClose.Value, 1m);

        return true;
    }

    /// <summary>
    /// A split rescales price and share count by exactly the published ratio.
    /// </summary>
    /// <remarks>
    /// <c>price × 1/r</c>, <c>shares × r</c>, where <c>r</c> is shares after
    /// per share before. A two-for-one split has <c>r = 2</c>: the historical
    /// price halves and the historical volume doubles.
    /// </remarks>
    private static bool TrySplit(
        CorporateAction action,
        out AdjustmentFactor factor,
        [NotNullWhen(false)] out string? problem)
    {
        problem = null;

        var ratio = action.Ratio!.Value;

        factor = AdjustmentFactor.Create(1m / ratio, ratio);
        return true;
    }

    /// <summary>
    /// A stock dividend or bonus issue adds shares without cash changing hands.
    /// </summary>
    /// <remarks>
    /// <c>price × 1/(1+r)</c>, <c>shares × (1+r)</c>, where <c>r</c> is the
    /// <em>additional</em> shares per existing share. A 10% stock dividend has
    /// <c>r = 0.1</c>, not 1.1 — reading it as a split ratio would leave the
    /// series adjusted by a factor of ten.
    /// </remarks>
    private static bool TryShareDistribution(
        CorporateAction action,
        out AdjustmentFactor factor,
        [NotNullWhen(false)] out string? problem)
    {
        problem = null;

        var multiplier = 1m + action.Ratio!.Value;

        factor = AdjustmentFactor.Create(1m / multiplier, multiplier);
        return true;
    }

    /// <summary>
    /// A rights issue prices new shares below the market, so the adjustment
    /// depends on the discount as well as the ratio.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The theoretical ex-rights price: <c>TERP = (P + r×S) / (1 + r)</c>, where
    /// <c>r</c> is new shares offered per existing share and <c>S</c> is the
    /// subscription price. The factor is <c>TERP / P</c> for price and
    /// <c>1 + r</c> for shares.
    /// </para>
    /// <para>
    /// Treating a rights issue as a bonus issue of the same ratio — ignoring
    /// the subscription price — overstates the drop, and treating it as no
    /// event at all understates it. In Vietnam, where rights issues at a deep
    /// discount are routine, either mistake is worth several per cent of a
    /// year's return.
    /// </para>
    /// </remarks>
    private static bool TryRightsIssue(
        CorporateAction action,
        Price previousClose,
        out AdjustmentFactor factor,
        [NotNullWhen(false)] out string? problem)
    {
        factor = AdjustmentFactor.Identity;

        var ratio = action.Ratio!.Value;
        var subscription = action.CashAmount!.Value;

        if (subscription >= previousClose.Value)
        {
            // Rights are issued at a discount. A subscription price at or above
            // the market means nobody would take them up, and the usual cause
            // is the two prices being in different units.
            problem =
                $"A subscription price of {Format(subscription)} is not below the previous close of "
                + $"{Format(previousClose.Value)}. Rights are issued at a discount.";
            return false;
        }

        problem = null;

        var shares = 1m + ratio;
        var theoretical = (previousClose.Value + (ratio * subscription)) / shares;

        factor = AdjustmentFactor.Create(theoretical / previousClose.Value, shares);
        return true;
    }

    private static string Format(decimal value) => value.ToString(CultureInfo.InvariantCulture);
}
