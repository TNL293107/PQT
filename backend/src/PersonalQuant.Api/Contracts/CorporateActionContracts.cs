using PersonalQuant.Domain.CorporateActions;

namespace PersonalQuant.Api.Contracts;

/// <summary>
/// What one corporate action did to a series, when it did anything.
/// </summary>
/// <remarks>
/// The reference close is published alongside the multipliers because two of
/// the five formulas depend on it. Without the number the factor was measured
/// against, "that factor looks wrong" is unanswerable from outside.
/// </remarks>
/// <param name="PriceFactor">What prices before the ex-date are multiplied by.</param>
/// <param name="ShareFactor">What volumes before the ex-date are multiplied by.</param>
/// <param name="ReferenceClose">The close the factor was computed against.</param>
/// <param name="ComputedAtUtc">When it was computed.</param>
public sealed record PriceAdjustmentResponse(
    decimal PriceFactor,
    decimal ShareFactor,
    decimal ReferenceClose,
    DateTimeOffset ComputedAtUtc);

/// <summary>
/// One corporate action on the wire.
/// </summary>
/// <remarks>
/// <para>
/// The ratio means a different quantity for each type — shares after per share
/// before for a split, additional shares per share held for a stock dividend,
/// new shares offered per existing share for a rights issue — so a client that
/// displays it must display the type beside it.
/// </para>
/// <para>
/// <c>adjustment</c> is absent when the action rescales nothing: a share
/// issuance, a symbol change, one that was cancelled, or one no factor could be
/// computed for because no price precedes its ex-date.
/// </para>
/// </remarks>
/// <param name="ActionId">The action's identifier.</param>
/// <param name="Type">CashDividend, StockSplit, RightsIssue, and so on.</param>
/// <param name="ExDate">The first date the security traded without the entitlement.</param>
/// <param name="RecordDate">When the register closed, when the source stated it.</param>
/// <param name="PaymentDate">When cash or shares arrived, when the source stated it.</param>
/// <param name="AnnouncedOn">When the action became public, when the source stated it.</param>
/// <param name="Ratio">The ratio, whose meaning depends on the type.</param>
/// <param name="CashAmount">The cash amount, whose meaning depends on the type.</param>
/// <param name="Source">Where the record came from.</param>
/// <param name="Version">How many times it has been restated.</param>
/// <param name="IsCancelled">Whether the issuer called it off.</param>
/// <param name="Note">Why it was cancelled or last amended.</param>
/// <param name="Adjustment">What it did to the series, when it did anything.</param>
public sealed record CorporateActionResponse(
    Guid ActionId,
    string Type,
    DateOnly ExDate,
    DateOnly? RecordDate,
    DateOnly? PaymentDate,
    DateOnly? AnnouncedOn,
    decimal? Ratio,
    decimal? CashAmount,
    string Source,
    int Version,
    bool IsCancelled,
    string? Note,
    PriceAdjustmentResponse? Adjustment)
{
    /// <summary>Projects an action and its factor onto the wire contract.</summary>
    /// <param name="action">The action.</param>
    /// <param name="adjustment">The factor it produced, when it produced one.</param>
    /// <returns>The response representation.</returns>
    public static CorporateActionResponse From(
        CorporateAction action,
        PriceAdjustment? adjustment)
    {
        ArgumentNullException.ThrowIfNull(action);

        return new CorporateActionResponse(
            action.Id.Value,
            action.Type.ToString(),
            action.ExDate,
            action.RecordDate,
            action.PaymentDate,
            action.AnnouncedOn,
            action.Ratio,
            action.CashAmount,
            action.Source.Value,
            action.Version,
            action.IsCancelled,
            action.Note,
            adjustment is null
                ? null
                : new PriceAdjustmentResponse(
                    adjustment.Factor.Price,
                    adjustment.Factor.Shares,
                    adjustment.ReferenceClose.Value,
                    adjustment.ComputedAtUtc));
    }
}

/// <summary>
/// The corporate actions recorded against one instrument.
/// </summary>
/// <param name="InstrumentId">The instrument.</param>
/// <param name="Count">How many actions are in this response.</param>
/// <param name="Results">The actions, oldest ex-date first.</param>
public sealed record CorporateActionsResponse(
    Guid InstrumentId,
    int Count,
    IReadOnlyList<CorporateActionResponse> Results);
