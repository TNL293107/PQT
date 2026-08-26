using PersonalQuant.Domain.CorporateActions;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.Application.CorporateActions;

/// <summary>
/// Reads and records corporate actions and the factors derived from them.
/// </summary>
/// <remarks>
/// <para>
/// One port for both, because the two are written together: recomputing a
/// factor reads the action it came from, and both land in the same
/// transaction. Splitting them would only create a way for an adjustment to
/// commit without the action that justifies it.
/// </para>
/// <para>
/// Actions are never deleted — an issuer that calls one off has it cancelled,
/// which is a fact worth keeping. Adjustments are, and that difference is the
/// point: an action is something that happened, a factor is something this
/// system computed, and only the second is safe to throw away and derive
/// again.
/// </para>
/// </remarks>
public interface ICorporateActionRepository
{
    /// <summary>
    /// Lists every action recorded against an instrument, oldest ex-date first.
    /// </summary>
    /// <remarks>
    /// Including cancelled ones. The adjustment engine has to see a
    /// cancellation to remove the factor it produced, and a caller reviewing
    /// history has to see that something was called off rather than never
    /// announced.
    /// </remarks>
    /// <param name="instrumentId">The instrument.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The actions.</returns>
    Task<IReadOnlyList<CorporateAction>> ListAsync(
        InstrumentId instrumentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the action an issuer announced for an instrument, type and
    /// ex-date.
    /// </summary>
    /// <remarks>
    /// The natural key, and what makes re-importing a source idempotent. One
    /// issuer does not pay two cash dividends going ex on the same day; a
    /// second row for the same three values is the same event arriving again.
    /// </remarks>
    /// <param name="instrumentId">The instrument.</param>
    /// <param name="type">What the issuer did.</param>
    /// <param name="exDate">The ex-date.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The action, or <see langword="null"/> when unknown.</returns>
    Task<CorporateAction?> FindAsync(
        InstrumentId instrumentId,
        CorporateActionType type,
        DateOnly exDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the factors currently stored for an instrument, oldest ex-date
    /// first.
    /// </summary>
    /// <param name="instrumentId">The instrument.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The adjustments.</returns>
    Task<IReadOnlyList<PriceAdjustment>> ListAdjustmentsAsync(
        InstrumentId instrumentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stages a new action. Call
    /// <see cref="Abstractions.IUnitOfWork.SaveChangesAsync"/> to persist it.
    /// </summary>
    /// <param name="action">The action to add.</param>
    void Add(CorporateAction action);

    /// <summary>
    /// Stages a new factor. Call
    /// <see cref="Abstractions.IUnitOfWork.SaveChangesAsync"/> to persist it.
    /// </summary>
    /// <param name="adjustment">The adjustment to add.</param>
    void AddAdjustment(PriceAdjustment adjustment);

    /// <summary>
    /// Stages the removal of a factor that no longer describes its action.
    /// </summary>
    /// <remarks>
    /// The one delete in the system, and it is safe because an adjustment is
    /// derived rather than observed. Recomputing produces it again from the
    /// action and the close it was measured against, and keeping a superseded
    /// factor would mean every read had to work out which one was current.
    /// </remarks>
    /// <param name="adjustment">The adjustment to remove.</param>
    void RemoveAdjustment(PriceAdjustment adjustment);
}
