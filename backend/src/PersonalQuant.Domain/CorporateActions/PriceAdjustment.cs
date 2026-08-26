using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Domain.CorporateActions;

/// <summary>
/// The factor one corporate action contributes to a series, frozen at the
/// moment it was computed.
/// </summary>
/// <remarks>
/// <para>
/// The <c>ADJUSTED</c> half of the roadmap's <c>RAW → adjustment → ADJUSTED</c>.
/// Raw bars are never rewritten; this sits beside them and is applied on read,
/// which is what makes an adjustment error correctable by recomputing a handful
/// of rows rather than destructive to a decade of prices.
/// </para>
/// <para>
/// The reference close is stored alongside the factor, not just used to derive
/// it. Two of the five formulas depend on what the price was, so without the
/// number they were computed from the arithmetic cannot be checked afterwards —
/// and "the factor looks wrong" is unanswerable.
/// </para>
/// <para>
/// One adjustment per action, so two actions sharing an ex-date each keep their
/// own factor and the day's effect is their product. Vietnamese issuers pair a
/// cash dividend with a stock dividend routinely, and a single combined row
/// would make it impossible to say which half was wrong.
/// </para>
/// </remarks>
public sealed class PriceAdjustment
{
    // EF Core materialises through this constructor and sets the properties
    // directly; it is never used by application code.
    private PriceAdjustment()
    {
    }

    private PriceAdjustment(
        CorporateActionId corporateActionId,
        InstrumentId instrumentId,
        DateOnly exDate,
        AdjustmentFactor factor,
        Price referenceClose,
        int actionVersion,
        int adjustmentVersion,
        DateTimeOffset computedAtUtc)
    {
        CorporateActionId = corporateActionId;
        InstrumentId = instrumentId;
        ExDate = exDate;
        Factor = factor;
        ReferenceClose = referenceClose;
        ActionVersion = actionVersion;
        AdjustmentVersion = adjustmentVersion;
        ComputedAtUtc = computedAtUtc;
    }

    /// <summary>Gets the action this factor came from.</summary>
    public CorporateActionId CorporateActionId { get; private set; }

    /// <summary>Gets the instrument the factor applies to.</summary>
    public InstrumentId InstrumentId { get; private set; }

    /// <summary>
    /// Gets the ex-date. Bars opening before it are rescaled; bars on or after
    /// it are already ex.
    /// </summary>
    public DateOnly ExDate { get; private set; }

    /// <summary>Gets what to multiply historical prices and volumes by.</summary>
    public AdjustmentFactor Factor { get; private set; }

    /// <summary>Gets the close the factor was computed against.</summary>
    public Price ReferenceClose { get; private set; } = default!;

    /// <summary>
    /// Gets the version of the action the factor was computed from.
    /// </summary>
    /// <remarks>
    /// When it no longer matches the action's own version, the source has
    /// restated something and this factor describes an event that has since
    /// changed. Finding those is a comparison rather than a re-adjustment of
    /// the whole series.
    /// </remarks>
    public int ActionVersion { get; private set; }

    /// <summary>Gets the version of the adjustment rules that computed it.</summary>
    public int AdjustmentVersion { get; private set; }

    /// <summary>Gets the instant it was computed, in UTC.</summary>
    public DateTimeOffset ComputedAtUtc { get; private set; }

    /// <summary>
    /// Records the factor an action implies.
    /// </summary>
    /// <param name="action">The action it came from.</param>
    /// <param name="factor">What to multiply historical data by.</param>
    /// <param name="referenceClose">The close the factor was computed against.</param>
    /// <param name="adjustmentVersion">The rule version that computed it.</param>
    /// <param name="computedAtUtc">The instant it was computed.</param>
    /// <returns>The new adjustment.</returns>
    /// <exception cref="DomainValidationException">A supplied value is invalid.</exception>
    public static PriceAdjustment For(
        CorporateAction action,
        AdjustmentFactor factor,
        Price referenceClose,
        int adjustmentVersion,
        DateTimeOffset computedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (adjustmentVersion <= DataRules.Unvalidated)
        {
            throw new DomainValidationException(
                "An adjustment must record the rule version that computed it.");
        }

        if (factor.IsIdentity)
        {
            // A stored row that multiplies by one is noise that every read has
            // to carry. An action producing it is one that should not have
            // been recorded as an action at all.
            throw new DomainValidationException(
                $"A {action.Type} on {action.ExDate:yyyy-MM-dd} rescales nothing, so there is no adjustment to record.");
        }

        return new PriceAdjustment(
            action.Id,
            action.InstrumentId,
            action.ExDate,
            factor,
            referenceClose,
            action.Version,
            adjustmentVersion,
            computedAtUtc);
    }

    /// <summary>
    /// Reports whether the factor still describes the action it came from.
    /// </summary>
    /// <param name="action">The action to compare against.</param>
    /// <returns><see langword="true"/> when the factor is current.</returns>
    public bool IsCurrentFor(CorporateAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        return !action.IsCancelled
            && ActionVersion == action.Version
            && AdjustmentVersion == DataRules.AdjustmentVersion;
    }

    /// <summary>
    /// Reports whether a bar opening at an instant is before the ex-date and so
    /// needs rescaling.
    /// </summary>
    /// <remarks>
    /// The comparison the whole adjustment rests on, and the one that is off by
    /// one session if the ex-date is. A bar opening <em>on</em> the ex-date
    /// already trades without the entitlement and is left alone.
    /// </remarks>
    /// <param name="openedAtUtc">The bar's opening instant.</param>
    /// <returns><see langword="true"/> when the bar predates the action.</returns>
    public bool AppliesTo(DateTimeOffset openedAtUtc) =>
        DateOnly.FromDateTime(openedAtUtc.UtcDateTime) < ExDate;
}
