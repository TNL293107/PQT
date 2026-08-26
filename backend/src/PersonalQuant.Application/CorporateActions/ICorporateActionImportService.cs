using PersonalQuant.Domain.CorporateActions;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Application.CorporateActions;

/// <summary>
/// An external source of corporate actions.
/// </summary>
/// <remarks>
/// Separate from the instrument and market data sources because it is usually
/// a different publication — an exchange's disclosure feed rather than a price
/// vendor — and because a deployment may have prices and no action history,
/// which is worth knowing rather than papering over.
/// </remarks>
public interface ICorporateActionProvider
{
    /// <summary>Gets the code actions from this source are attributed to.</summary>
    SourceCode Code { get; }

    /// <summary>
    /// Reads every action the source knows about.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The actions.</returns>
    /// <exception cref="MarketData.MarketDataProviderException">
    /// The source could not be read.
    /// </exception>
    Task<IReadOnlyList<ProviderCorporateAction>> ListActionsAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// One action exactly as a source reported it.
/// </summary>
/// <remarks>
/// Primitives, and the symbol rather than an identifier: a source names
/// securities in its own symbology, and resolving that to a canonical
/// instrument is the import's job rather than the provider's.
/// </remarks>
/// <param name="Symbol">The source's spelling of the symbol.</param>
/// <param name="Type">What the issuer did, by name.</param>
/// <param name="ExDate">The first date the security trades without the entitlement.</param>
/// <param name="Ratio">The ratio, where the type requires one.</param>
/// <param name="CashAmount">The cash amount, where the type requires one.</param>
/// <param name="RecordDate">When the register closes, when the source states it.</param>
/// <param name="PaymentDate">When cash or shares arrive, when the source states it.</param>
/// <param name="AnnouncedOn">When the action became public, when the source states it.</param>
public sealed record ProviderCorporateAction(
    string Symbol,
    string Type,
    DateOnly ExDate,
    decimal? Ratio = null,
    decimal? CashAmount = null,
    DateOnly? RecordDate = null,
    DateOnly? PaymentDate = null,
    DateOnly? AnnouncedOn = null);

/// <summary>
/// Why an action row could not be recorded.
/// </summary>
public enum CorporateActionRejectionReason
{
    /// <summary>The symbol could not be resolved to an instrument.</summary>
    UnknownInstrument = 1,

    /// <summary>The type was missing or is not one this system records.</summary>
    UnknownType = 2,

    /// <summary>The ratio or cash amount contradicted the type.</summary>
    InconsistentAmounts = 3,

    /// <summary>A date contradicted the ex-date.</summary>
    InconsistentDates = 4,

    /// <summary>The same action appeared more than once in one import.</summary>
    DuplicateWithinImport = 5,
}

/// <summary>One action row that was refused, and why.</summary>
/// <param name="Row">The row as the source reported it.</param>
/// <param name="Reason">Why it was refused.</param>
/// <param name="Detail">A short, specific explanation.</param>
public sealed record CorporateActionRejection(
    ProviderCorporateAction Row,
    CorporateActionRejectionReason Reason,
    string Detail);

/// <summary>
/// What one corporate action import did.
/// </summary>
/// <remarks>
/// Amended is separated from created and unchanged because it is the one that
/// invalidates factors already in the series. A run that amends nothing needs
/// no recompute; a run that amends anything does.
/// </remarks>
/// <param name="Source">The source that was read.</param>
/// <param name="RowsRead">Rows the source returned.</param>
/// <param name="Created">Actions recorded for the first time.</param>
/// <param name="Amended">Actions the source restated.</param>
/// <param name="Unchanged">Actions already held, unchanged.</param>
/// <param name="Rejections">Rows that were refused, with reasons.</param>
public sealed record CorporateActionImportReport(
    string Source,
    int RowsRead,
    int Created,
    int Amended,
    int Unchanged,
    IReadOnlyList<CorporateActionRejection> Rejections)
{
    /// <summary>Gets how many rows were refused.</summary>
    public int Rejected => Rejections.Count;

    /// <summary>Gets a value indicating whether any factor may now be stale.</summary>
    public bool RequiresRecompute => Created > 0 || Amended > 0;
}

/// <summary>
/// Populates the corporate action record from an external source, then brings
/// the adjustment factors back into line with it.
/// </summary>
public interface ICorporateActionImportService
{
    /// <summary>
    /// Reads the configured source and reconciles it against what is held.
    /// </summary>
    /// <remarks>
    /// Additive and idempotent. An action already held with the same ratio and
    /// cash amount is left alone; one the source has restated is amended, which
    /// bumps its version and makes the factor derived from it stale. Nothing is
    /// deleted — an action the source has stopped publishing may simply have
    /// fallen out of its window, and inferring a cancellation from an absence
    /// would silently un-adjust a series.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>What the run did.</returns>
    /// <exception cref="MarketData.MarketDataProviderException">
    /// The source could not be read.
    /// </exception>
    /// <exception cref="InvalidOperationException">No source is registered.</exception>
    Task<CorporateActionImportReport> ImportAsync(CancellationToken cancellationToken = default);
}
