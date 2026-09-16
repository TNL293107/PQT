using System.Globalization;
using Microsoft.Extensions.Logging;
using PersonalQuant.Application.Abstractions;
using PersonalQuant.Application.Diagnostics;
using PersonalQuant.Application.Exchanges;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Domain.CorporateActions;
using PersonalQuant.Domain.Exchanges;
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
/// <param name="instruments">Resolves the instrument's venue and asset class.</param>
/// <param name="exchanges">Supplies the venue's price limit.</param>
/// <param name="unitOfWork">Commits the run.</param>
/// <param name="clock">Supplies the computation instant.</param>
/// <param name="logger">Logger for adjustment telemetry.</param>
internal sealed class PriceAdjustmentService(
    ICorporateActionRepository actions,
    IBarRepository bars,
    IDataQualityRepository issues,
    IInstrumentRepository instruments,
    IExchangeRepository exchanges,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILogger<PriceAdjustmentService> logger) : IPriceAdjustmentService
{
    /// <summary>
    /// Extra fractional room allowed on top of a venue's band, matching
    /// <see cref="MarketData.IBarQualityInspector"/>'s.
    /// </summary>
    /// <remarks>
    /// The same number for the same reason: prices are rounded to a tick, so a
    /// realised move can exceed the nominal band slightly with nothing wrong.
    /// The two checks are opposite sides of one question and must not disagree
    /// about what counts as an ordinary day.
    /// </remarks>
    private const decimal PriceLimitTolerance = 0.005m;

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

        // Every live adjustment for this instrument, grouped by the session it
        // goes ex on. The discontinuity check below reads a whole ex-date at
        // once, so it has to see the actions that were already current as well
        // as the ones recomputed here - a group missing the half that did not
        // change would be judged against a factor the series is not read
        // through.
        var byExDate = new Dictionary<DateOnly, List<(CorporateAction Action, PriceAdjustment Adjustment)>>();

        // The ex-dates something actually moved on. Checking only these keeps
        // the read count where it was: a recompute that changes nothing asks
        // the database nothing.
        var touched = new HashSet<DateOnly>();

        var computed = 0;
        var unchanged = 0;
        var removed = 0;
        var explained = 0;
        var raised = 0;

        // Resolved once for the whole run rather than per action. Both are
        // needed only by the discontinuity check, and an instrument whose
        // venue is not held simply skips it — a missing venue is not a reason
        // to refuse to adjust a series.
        var band = await FindPriceLimitAsync(instrumentId, cancellationToken)
            .ConfigureAwait(false);

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
                Group(byExDate, action, stored);
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
            Group(byExDate, action, outcome.Adjustment!);
            touched.Add(action.ExDate);
            computed++;

            explained += await ExplainFindingsAsync(action, cancellationToken)
                .ConfigureAwait(false);
        }

        // After the loop, not inside it. An action is only contradicted by the
        // prices once everything going ex with it has been computed, and the
        // order the actions arrive in is not something this can depend on.
        foreach (var exDate in touched.Order())
        {
            raised += await RaiseIfUnsupportedAsync(
                    instrumentId,
                    exDate,
                    byExDate[exDate],
                    band,
                    computedAtUtc,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var run = new AdjustmentRun(
            instrumentId, recorded.Count, computed, unchanged, removed, explained, raised, rejections);

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

    /// <summary>
    /// Finds the daily band the instrument's venue enforces, when there is one
    /// to check against.
    /// </summary>
    /// <remarks>
    /// Null for an instrument whose venue is unknown, whose venue publishes no
    /// band, or that is calculated rather than traded. An index has no band to
    /// breach, so an action recorded against one cannot be checked this way and
    /// is left alone rather than flagged on every volatile day.
    /// </remarks>
    private async Task<PriceLimit?> FindPriceLimitAsync(
        InstrumentId instrumentId,
        CancellationToken cancellationToken)
    {
        var instrument = await instruments
            .FindByIdAsync(instrumentId, cancellationToken)
            .ConfigureAwait(false);

        if (instrument is null || instrument.AssetType == AssetType.Index)
        {
            return null;
        }

        var exchange = await exchanges
            .FindByIdAsync(instrument.ExchangeId, cancellationToken)
            .ConfigureAwait(false);

        return exchange?.DailyPriceLimit;
    }

    /// <summary>
    /// Raises a finding when the actions going ex on a session claim to explain
    /// a move the prices never made.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The gap Phase 4 recorded against itself. An action rescales every bar
    /// before its ex-date, so one transcribed wrongly — a ratio of 2 where the
    /// source said 1.2, a dividend in đồng where it meant thousands, an ex-date
    /// off by a week — corrupts a decade of history while leaving a series that
    /// still looks smooth. Nothing downstream can detect it, because the
    /// adjustment is what made it smooth.
    /// </para>
    /// <para>
    /// The test is the venue's own band, applied to the adjusted move. Rescale
    /// the last close before the ex-date by the factor and the ex-date's close
    /// should sit within one ordinary day of it; that is precisely what the
    /// factor claims. A discrepancy larger than a session's permitted move did
    /// not come from the market.
    /// </para>
    /// <para>
    /// <strong>The unit is the ex-date, not the action.</strong> Several
    /// entitlements detaching on one session is ordinary in Vietnam — FPT on
    /// 27 May 2016 paid a cash dividend and a stock dividend together — and each
    /// of them rescales the same prices, so what the series is read through is
    /// the product of their factors. Reading them one at a time was the first
    /// thing a real reproduction broke: the dividend alone implied a close 13%
    /// above what printed, and a correctly transcribed pair was reported as a
    /// transcription error. A check that cries wolf on the ordinary case is
    /// worse than no check, because the findings it buries are the real ones.
    /// </para>
    /// <para>
    /// It is a finding, not a refusal. The factor is stored either way, for the
    /// reason a suspect bar is: refusing it would lose the record of what the
    /// source said, and an unexplained discontinuity somebody can see beats a
    /// silent absence.
    /// </para>
    /// </remarks>
    private async Task<int> RaiseIfUnsupportedAsync(
        InstrumentId instrumentId,
        DateOnly exDate,
        List<(CorporateAction Action, PriceAdjustment Adjustment)> sharing,
        PriceLimit? band,
        DateTimeOffset detectedAtUtc,
        CancellationToken cancellationToken)
    {
        if (band is not { } limit || sharing.Count == 0)
        {
            return 0;
        }

        var session = new DateTimeOffset(
            exDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var onExDate = await bars
            .ListForUpdateAsync(
                instrumentId,
                BarInterval.OneDay,
                session,
                session.AddDays(1),
                cancellationToken)
            .ConfigureAwait(false);

        // No bar on the ex-date is not evidence either way. The action may
        // predate the ingested history, or the session may simply not have been
        // fetched yet.
        if (onExDate.Count == 0)
        {
            return 0;
        }

        // Every action going ex on this session rescales the same prices, so
        // what the series is read through is their product. Each factor was
        // measured against the same close - the last one before the ex-date -
        // which is what makes multiplying them the right composition rather
        // than an approximation of one.
        var reference = sharing[0].Adjustment.ReferenceClose.Value;
        var combined = sharing.Aggregate(1m, (running, pair) => running * pair.Adjustment.Factor.Price);

        var expected = reference * combined;
        var observed = onExDate[0].Close.Value;

        if (limit.Permits(expected, observed, PriceLimitTolerance))
        {
            return 0;
        }

        var existing = await issues
            .ListAsync(
                instrumentId,
                BarInterval.OneDay,
                session,
                session.AddDays(1),
                cancellationToken)
            .ConfigureAwait(false);

        // One finding per instrument, resolution, session and kind. Raising it
        // again on every recompute would bury the ones nobody has looked at,
        // and would undo a dismissal.
        if (existing.Any(issue => issue.Kind == DataQualityIssueKind.ActionWithoutDiscontinuity))
        {
            return 0;
        }

        issues.Add(DataQualityIssue.Raise(
            instrumentId,
            BarInterval.OneDay,
            session,
            DataQualityIssueKind.ActionWithoutDiscontinuity,
            Describe(sharing, expected, observed, limit),
            DataRules.ValidationVersion,
            detectedAtUtc));

        return 1;
    }

    /// <summary>
    /// Adds an action and its factor to the group for its ex-date.
    /// </summary>
    private static void Group(
        Dictionary<DateOnly, List<(CorporateAction Action, PriceAdjustment Adjustment)>> byExDate,
        CorporateAction action,
        PriceAdjustment adjustment)
    {
        if (!byExDate.TryGetValue(action.ExDate, out var sharing))
        {
            sharing = [];
            byExDate[action.ExDate] = sharing;
        }

        sharing.Add((action, adjustment));
    }

    /// <summary>
    /// States what the session's actions claimed and what the prices did.
    /// </summary>
    /// <remarks>
    /// One action and several read differently on purpose. When two entitlements
    /// detach together the finding cannot say which of them is wrong — only that
    /// the pair does not match the print — and wording it as though it knew
    /// would send the reader to the wrong row.
    /// </remarks>
    private static string Describe(
        List<(CorporateAction Action, PriceAdjustment Adjustment)> sharing,
        decimal expected,
        decimal observed,
        PriceLimit limit)
    {
        var tail =
            $"but {Format(observed)} was recorded — further from it than {limit} allows in a session. ";

        if (sharing.Count == 1)
        {
            return $"A {sharing[0].Action.Type} implies a close of {Format(expected)} on its ex-date, "
                + tail
                + "The ratio, the amount or the ex-date is likely transcribed wrongly.";
        }

        var types = string.Join(
            ", ", sharing.Select(pair => pair.Action.Type).Order().Select(type => type.ToString()));

        return $"{sharing.Count} actions going ex together ({types}) imply a close of "
            + $"{Format(expected)}, "
            + tail
            + "One of their ratios, amounts or ex-dates is likely transcribed wrongly, or an "
            + "action that also went ex that session has not been recorded.";
    }

    private static string Format(decimal value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);

    private static AdjustmentRejection Reject(CorporateAction action, string detail) =>
        new(action.Id, action.Type, action.ExDate, detail);
}
