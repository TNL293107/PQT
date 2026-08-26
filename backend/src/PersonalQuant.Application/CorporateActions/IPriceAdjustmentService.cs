using PersonalQuant.Domain.CorporateActions;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.Application.CorporateActions;

/// <summary>
/// Turns the corporate actions recorded against an instrument into the factors
/// its series is read through.
/// </summary>
/// <remarks>
/// <para>
/// Idempotent and safe to run on every import. A factor that still describes
/// its action is left alone; one whose action has been amended or cancelled is
/// replaced or removed. Running it twice changes nothing the second time.
/// </para>
/// <para>
/// It is also where Phase 3's promise is kept. An open price-limit finding on
/// an action's ex-date is exactly the discontinuity that action explains, and
/// closing it here is a recorded resolution rather than somebody editing a
/// row.
/// </para>
/// </remarks>
public interface IPriceAdjustmentService
{
    /// <summary>
    /// Recomputes every factor for one instrument.
    /// </summary>
    /// <param name="instrumentId">The instrument.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>What the run did.</returns>
    Task<AdjustmentRun> RecomputeAsync(
        InstrumentId instrumentId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Why a factor could not be computed for an action.
/// </summary>
/// <remarks>
/// Reported rather than thrown, and reported per action rather than per run.
/// One action with a dividend recorded in the wrong unit must not stop the
/// other nine from adjusting the series.
/// </remarks>
/// <param name="ActionId">The action.</param>
/// <param name="Type">What the issuer did.</param>
/// <param name="ExDate">Its ex-date.</param>
/// <param name="Detail">Why no factor could be computed.</param>
public sealed record AdjustmentRejection(
    CorporateActionId ActionId,
    CorporateActionType Type,
    DateOnly ExDate,
    string Detail);

/// <summary>
/// What one recompute did.
/// </summary>
/// <remarks>
/// Computed and unchanged are separated for the same reason stored and revised
/// are in ingestion: a run that recomputes everything on its second pass has a
/// staleness check that does not work, and only the split between the two
/// says so.
/// </remarks>
/// <param name="InstrumentId">The instrument.</param>
/// <param name="ActionsConsidered">Actions recorded against it.</param>
/// <param name="Computed">Factors written or replaced.</param>
/// <param name="Unchanged">Factors that still described their action.</param>
/// <param name="Removed">Factors dropped because their action no longer rescales anything.</param>
/// <param name="IssuesExplained">Open quality findings the actions accounted for.</param>
/// <param name="Rejections">Actions no factor could be computed for.</param>
public sealed record AdjustmentRun(
    InstrumentId InstrumentId,
    int ActionsConsidered,
    int Computed,
    int Unchanged,
    int Removed,
    int IssuesExplained,
    IReadOnlyList<AdjustmentRejection> Rejections)
{
    /// <summary>A run over an instrument with no actions.</summary>
    /// <param name="instrumentId">The instrument.</param>
    /// <returns>An empty run.</returns>
    public static AdjustmentRun Nothing(InstrumentId instrumentId) =>
        new(instrumentId, 0, 0, 0, 0, 0, []);
}
