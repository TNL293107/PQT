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

    /// <summary>
    /// Composes the factors of every action going ex on one session, so that
    /// their product is the session's true factor.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Multiplying standalone factors is exact for cash dividends and share
    /// distributions together — <c>(P − D)/P × 1/(1+s) = (P − D)/((1+s)P)</c>,
    /// which is FPT on 27 May 2016. It is not exact once a rights issue shares
    /// the session. A holder of one share before MBB's 11 August 2026 session
    /// received 0.15 new shares and the right to buy 0.1 more at 10,000₫, all
    /// counted on the shares held at the record date, and so ends with 1.25
    /// shares worth <c>P + 0.1S</c>. Standalone factors divide by
    /// <c>1.15 × 1.1 = 1.265</c> instead — 1.2% off, inside the band, and
    /// invisible to every check downstream.
    /// </para>
    /// <para>
    /// The session factor is <c>(P − ΣD + Σ rᵢSᵢ) / ((1 + Σs + Σ rᵢ) P)</c>,
    /// times any split, on the Vietnamese convention that every ratio refers to
    /// the holding at the record date. The first rights issue absorbs the
    /// difference from the standalone product. Every other action keeps the
    /// factor it has alone, so a stored row still reads as its own action, and
    /// the product of the stored rows is what the series is read through.
    /// </para>
    /// </remarks>
    /// <param name="standalone">
    /// Every action going ex on the session that a factor could be computed
    /// for, with that factor.
    /// </param>
    /// <param name="previousClose">The last close before the ex-date.</param>
    /// <param name="factors">The factor to store for each action.</param>
    /// <param name="problem">
    /// Why the session has no usable factor. Reported rather than thrown, like
    /// every other refusal here: one session that cannot be composed must not
    /// cost the instrument's other sessions their factors.
    /// </param>
    /// <returns><see langword="true"/> when the session composed.</returns>
    public static bool TryComposeSession(
        IReadOnlyList<(CorporateAction Action, AdjustmentFactor Factor)> standalone,
        Price previousClose,
        out IReadOnlyDictionary<CorporateActionId, AdjustmentFactor> factors,
        [NotNullWhen(false)] out string? problem)
    {
        ArgumentNullException.ThrowIfNull(standalone);

        var composed = standalone.ToDictionary(pair => pair.Action.Id, pair => pair.Factor);
        factors = composed;
        problem = null;

        var absorber = standalone
            .Where(pair => pair.Action.Type == CorporateActionType.RightsIssue)
            .OrderBy(pair => pair.Action.Id.Value)
            .Select(pair => pair.Action)
            .FirstOrDefault();

        if (absorber is null || standalone.Count == 1)
        {
            return true;
        }

        var close = previousClose.Value;
        var cash = 0m;
        var distributed = 0m;
        var offered = 0m;
        var subscribed = 0m;
        var splitPrice = 1m;
        var splitShares = 1m;

        foreach (var (action, factor) in standalone)
        {
            switch (action.Type)
            {
                case CorporateActionType.CashDividend:
                    cash += action.CashAmount!.Value;
                    break;
                case CorporateActionType.RightsIssue:
                    offered += action.Ratio!.Value;
                    subscribed += action.Ratio!.Value * action.CashAmount!.Value;
                    break;
                case CorporateActionType.StockSplit or CorporateActionType.ReverseSplit:
                    splitPrice *= factor.Price;
                    splitShares *= factor.Shares;
                    break;
                default:
                    distributed += action.Ratio!.Value;
                    break;
            }
        }

        // One cash dividend per session is all the record can hold, and it is
        // already below the close, so this cannot fail on stored actions. It is
        // checked because the composition is public and a caller can hand it
        // anything.
        var remaining = close - cash + subscribed;

        if (remaining <= 0m)
        {
            problem =
                $"The session's cash entitlements of {Format(cash)} leave nothing of a close of "
                + $"{Format(close)}. One of them is in the wrong unit or belongs to another session.";
            return false;
        }

        var shares = 1m + distributed + offered;
        var sessionPrice = remaining / (shares * close) * splitPrice;
        var sessionShares = shares * splitShares;

        var othersPrice = 1m;
        var othersShares = 1m;

        foreach (var (action, factor) in standalone)
        {
            if (action.Id != absorber.Id)
            {
                othersPrice *= factor.Price;
                othersShares *= factor.Shares;
            }
        }

        if (!AdjustmentFactor.TryCreate(
                sessionPrice / othersPrice, sessionShares / othersShares, out var absorbed))
        {
            problem = "The session's actions compose to no usable factor.";
            return false;
        }

        composed[absorber.Id] = absorbed;
        return true;
    }

    private static string Format(decimal value) => value.ToString(CultureInfo.InvariantCulture);
}
