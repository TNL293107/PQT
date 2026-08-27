using System.Globalization;
using Microsoft.Extensions.Logging;
using PersonalQuant.Application.Abstractions;
using PersonalQuant.Application.Diagnostics;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Domain.CorporateActions;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Application.CorporateActions;

/// <summary>
/// Default <see cref="IPriceAdjustmentService"/>.
/// </summary>
/// <remarks>
/// <para>
/// The engine, and it is deliberately small. The arithmetic lives in
/// <see cref="AdjustmentFactors"/> where it can be checked against a worked
/// example; what is left here is deciding which factors are stale, finding the
/// close each one is measured against, and committing the result.
/// </para>
/// <para>
/// Raw bars are never touched. The output is a handful of rows beside them, so
/// an adjustment error is corrected by recomputing those rows rather than by
/// rewriting a decade of prices — which is the difference between a mistake
/// and a disaster.
/// </para>
/// </remarks>
/// <param name="actions">Corporate actions and the factors derived from them.</param>
/// <param name="bars">The canonical series, for the close each factor measures against.</param>
/// <param name="issues">Open quality findings an action may account for.</param>
/// <param name="unitOfWork">Commits the run.</param>
/// <param name="clock">Supplies the computation instant.</param>
/// <param name="logger">Logger for adjustment telemetry.</param>
internal sealed class PriceAdjustmentService(
    ICorporateActionRepository actions,
    IBarRepository bars,
    IDataQualityRepository issues,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILogger<PriceAdjustmentService> logger) : IPriceAdjustmentService
{
    /// <inheritdoc />
    public async Task<AdjustmentRun> RecomputeAsync(
        InstrumentId instrumentId,
        CancellationToken cancellationToken = default)
    {
        if (instrumentId.IsEmpty)
        {
            return AdjustmentRun.Nothing(instrumentId);
        }

        var recorded = await actions
            .ListAsync(instrumentId, cancellationToken)
            .ConfigureAwait(false);

        if (recorded.Count == 0)
        {
            return AdjustmentRun.Nothing(instrumentId);
        }

        var existing = (await actions
            .ListAdjustmentsAsync(instrumentId, cancellationToken)
            .ConfigureAwait(false))
            .ToDictionary(adjustment => adjustment.CorporateActionId);

        var computedAtUtc = clock.UtcNow;
        var rejections = new List<AdjustmentRejection>();
        var computed = 0;
        var unchanged = 0;
        var removed = 0;
        var explained = 0;

        foreach (var action in recorded)
        {
            existing.TryGetValue(action.Id, out var stored);

            if (!action.AffectsPrice)
            {
                // Cancelled, or a type that never rescaled anything. Either
                // way the factor it used to contribute has to go, or the
                // series stays adjusted for an event that is not happening.
                if (stored is not null)
                {
                    actions.RemoveAdjustment(stored);
                    removed++;
                }

                continue;
            }

            if (stored is not null && stored.IsCurrentFor(action))
            {
                unchanged++;
                continue;
            }

            var outcome = await ComputeAsync(action, computedAtUtc, cancellationToken)
                .ConfigureAwait(false);

            if (outcome.Rejection is { } rejection)
            {
                rejections.Add(rejection);
                continue;
            }

            // Replaced rather than mutated: the stored row records the action
            // version and the close it was measured against, and both change
            // together or not at all.
            if (stored is not null)
            {
                actions.RemoveAdjustment(stored);
            }

            actions.AddAdjustment(outcome.Adjustment!);
            computed++;

            explained += await ExplainFindingsAsync(action, cancellationToken)
                .ConfigureAwait(false);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var run = new AdjustmentRun(
            instrumentId, recorded.Count, computed, unchanged, removed, explained, rejections);

        ApplicationLog.PriceAdjustmentsRecomputed(
            logger, recorded.Count, computed, unchanged, removed, explained, rejections.Count);

        foreach (var rejection in rejections)
        {
            ApplicationLog.PriceAdjustmentRejected(
                logger, rejection.Type, rejection.ExDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), rejection.Detail);
        }

        return run;
    }

    /// <summary>
    /// Computes one action's factor, against the last close before its
    /// ex-date.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The reference close is the last daily bar that opened <em>strictly
    /// before</em> the ex-date. That is the price the market last saw with the
    /// entitlement attached, and it is what every published adjustment formula
    /// measures against.
    /// </para>
    /// <para>
    /// An action with no price before it cannot be adjusted for and is reported
    /// rather than skipped. It usually means the action predates the ingested
    /// history, which is worth knowing: the series is correct from the ex-date
    /// onwards and simply has nothing earlier to rescale.
    /// </para>
    /// </remarks>
    private async Task<(PriceAdjustment? Adjustment, AdjustmentRejection? Rejection)> ComputeAsync(
        CorporateAction action,
        DateTimeOffset computedAtUtc,
        CancellationToken cancellationToken)
    {
        var exDateUtc = new DateTimeOffset(
            action.ExDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var previous = await bars
            .FindLastBeforeAsync(
                action.InstrumentId, BarInterval.OneDay, exDateUtc, cancellationToken)
            .ConfigureAwait(false);

        if (previous is null)
        {
            return (null, Reject(
                action,
                "No daily bar is stored before the ex-date, so there is no close to measure against "
                + "and nothing earlier to rescale."));
        }

        if (!AdjustmentFactors.TryCompute(action, previous.Close, out var factor, out var problem))
        {
            return (null, Reject(action, problem));
        }

        return (
            PriceAdjustment.For(
                action, factor, previous.Close, DataRules.AdjustmentVersion, computedAtUtc),
            null);
    }

    /// <summary>
    /// Closes the quality findings this action accounts for.
    /// </summary>
    /// <remarks>
    /// The loop Phase 3 left open. A price-limit breach on an ex-date is the
    /// discontinuity the action caused, and explaining it says so in the record
    /// rather than leaving a finding nobody can ever close.
    /// </remarks>
    private async Task<int> ExplainFindingsAsync(
        CorporateAction action,
        CancellationToken cancellationToken)
    {
        var session = new DateTimeOffset(
            action.ExDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var candidates = await issues
            .ListAsync(
                action.InstrumentId,
                BarInterval.OneDay,
                session,
                session.AddDays(1),
                cancellationToken)
            .ConfigureAwait(false);

        var explained = 0;

        foreach (var issue in candidates)
        {
            // Only the breach, and only while it is open. A missing session is
            // not something a corporate action explains, and a finding somebody
            // has already dismissed stays dismissed.
            if (issue.Kind != DataQualityIssueKind.PriceLimitBreach || !issue.IsOpen)
            {
                continue;
            }

            var tracked = await issues.FindAsync(issue.Id, cancellationToken).ConfigureAwait(false);

            if (tracked is null || !tracked.IsOpen)
            {
                continue;
            }

            tracked.Explain(
                $"A {action.Type} with an ex-date of {action.ExDate:yyyy-MM-dd}, recorded from {action.Source}.",
                clock.UtcNow);

            explained++;
        }

        return explained;
    }

    private static AdjustmentRejection Reject(CorporateAction action, string detail) =>
        new(action.Id, action.Type, action.ExDate, detail);
}
