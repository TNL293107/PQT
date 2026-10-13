using System.Globalization;
using PersonalQuant.Application.Abstractions;
using PersonalQuant.Application.Exchanges;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Application.Instruments;

/// <summary>
/// Default <see cref="IVenueHistoryImportService"/>.
/// </summary>
/// <remarks>
/// <para>
/// An instrument's history is accepted whole or not at all. The spells must
/// tile time — the first open at the start, each beginning where the last
/// ended, the last open at the end and on the venue the master has the
/// instrument on now — because a history with a hole in it would answer "which
/// venue?" with a guess for the sessions in the hole.
/// </para>
/// <para>
/// Append-only, like universe membership. A history already recorded is left
/// alone when the source restates it and refused when the source contradicts
/// it: a correction to when an instrument moved changes how years of findings
/// were judged, and is made deliberately rather than by editing a file.
/// </para>
/// <para>
/// Recording a history changes what a session's band was, so the open
/// price-limit breaches of every instrument recorded are judged again on the
/// venue each session actually traded on. A move inside that venue's band is
/// explained, saying so; one outside it stays open.
/// </para>
/// </remarks>
/// <param name="providers">Every registered venue history source.</param>
/// <param name="instruments">Resolves symbols and reads current venues.</param>
/// <param name="exchanges">Resolves venue codes and bands.</param>
/// <param name="history">The venue history record.</param>
/// <param name="bars">The bars a breach is re-judged on.</param>
/// <param name="issues">The findings re-judged.</param>
/// <param name="inspector">Raises what an old venue's narrower band should have.</param>
/// <param name="unitOfWork">Commits the run.</param>
/// <param name="clock">Supplies the audit timestamps.</param>
internal sealed class VenueHistoryImportService(
    IEnumerable<IVenueHistoryProvider> providers,
    IInstrumentRepository instruments,
    IExchangeRepository exchanges,
    IVenueHistoryRepository history,
    IBarRepository bars,
    IDataQualityRepository issues,
    IBarQualityInspector inspector,
    IUnitOfWork unitOfWork,
    IClock clock) : IVenueHistoryImportService
{
    /// <summary>The earliest instant any finding or bar is looked for from.</summary>
    private static readonly DateTimeOffset Earliest = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <inheritdoc />
    public async Task<VenueHistoryImportReport> ImportAsync(CancellationToken cancellationToken = default)
    {
        var registered = providers.ToList();

        var provider = registered.Count == 1
            ? registered[0]
            : throw new InvalidOperationException(
                registered.Count == 0
                    ? "No venue history source is registered."
                    : "Several venue history sources are registered, which is not supported.");

        var rows = await provider.ListAsync(cancellationToken).ConfigureAwait(false);
        var occurredAtUtc = clock.UtcNow;
        var venues = (await exchanges.ListAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(exchange => exchange.Code.Value, StringComparer.OrdinalIgnoreCase);

        var rejections = new List<VenueHistoryRejection>();
        var held = new List<Instrument>();
        var recorded = 0;

        foreach (var group in rows.GroupBy(row => row.Symbol.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            var outcome = await ApplyAsync(
                    group.Key, [.. group], provider.Code, venues, occurredAtUtc, cancellationToken)
                .ConfigureAwait(false);

            if (outcome.Rejection is { } rejection)
            {
                rejections.Add(rejection);
                continue;
            }

            held.Add(outcome.Instrument!);
            recorded += outcome.Changed ? 1 : 0;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Every instrument with a history is judged again, not only those
        // recorded now. It is idempotent — it explains what its venue's band
        // permits and raises only what is not already raised — and it is what
        // finishes the job when a run recorded spells and stopped before
        // judging, which would otherwise leave them "unchanged" forever.
        var rejudged = 0;

        foreach (var instrument in held)
        {
            rejudged += await RejudgeAsync(instrument, venues.Values, cancellationToken).ConfigureAwait(false);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new VenueHistoryImportReport(
            provider.Code.Value, rows.Count, recorded, held.Count - recorded, rejudged, rejections);
    }

    private async Task<(Instrument? Instrument, bool Changed, VenueHistoryRejection? Rejection)> ApplyAsync(
        string symbol,
        IReadOnlyList<ProviderVenueSpell> rows,
        SourceCode source,
        Dictionary<string, Exchange> venues,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken)
    {
        var instrument = await ResolveAsync(symbol, source, cancellationToken).ConfigureAwait(false);

        if (instrument is null)
        {
            return Refuse(symbol, VenueHistoryRejectionReason.UnknownInstrument,
                $"'{symbol}' does not resolve to an instrument this system holds.");
        }

        var unknown = rows.FirstOrDefault(row => !venues.ContainsKey(row.Venue.Trim()));

        if (unknown is not null)
        {
            return Refuse(symbol, VenueHistoryRejectionReason.UnknownVenue,
                $"Line {unknown.LineNumber}: no venue is registered under '{unknown.Venue}'.");
        }

        var ordered = rows.OrderBy(row => row.EffectiveFrom ?? DateOnly.MinValue).ToList();

        if (Contiguity(ordered) is { } problem)
        {
            return Refuse(symbol, VenueHistoryRejectionReason.NotContiguous, problem);
        }

        var last = venues[ordered[^1].Venue.Trim()];

        if (last.Id != instrument.ExchangeId)
        {
            return Refuse(symbol, VenueHistoryRejectionReason.DisagreesWithCurrentVenue,
                $"The history ends on {last.Code}, and the instrument master has {instrument.Ticker} elsewhere. "
                    + $"Move it first with 'pqt instrument transfer --instrument {instrument.Ticker} --to {last.Code}'.");
        }

        var stated = ordered
            .Select(row => (venues[row.Venue.Trim()].Id, row.EffectiveFrom, row.EffectiveTo))
            .ToList();

        var tracked = (await history.ListForUpdateAsync(instrument.Id, cancellationToken).ConfigureAwait(false))
            .OrderBy(spell => spell.EffectiveFrom ?? DateOnly.MinValue)
            .ToList();

        var existing = tracked
            .Select(spell => (spell.ExchangeId, spell.EffectiveFrom, spell.EffectiveTo))
            .ToList();

        if (existing.Count == 0)
        {
            foreach (var (exchangeId, from, to) in stated)
            {
                history.Add(InstrumentVenueSpell.Record(instrument.Id, exchangeId, from, to, occurredAtUtc));
            }

            return (instrument, true, null);
        }

        if (existing.SequenceEqual(stated))
        {
            return (instrument, false, null);
        }

        // A later move: everything recorded is restated, except that the open
        // spell now has an end, and new spells follow it. That is the one way
        // an append-only history grows.
        var k = existing.Count;

        if (stated.Count > k
            && existing.Take(k - 1).SequenceEqual(stated.Take(k - 1))
            && existing[k - 1] is (var venue, var since, null)
            && stated[k - 1] is (var sameVenue, var sameSince, { } ended)
            && venue == sameVenue
            && since == sameSince)
        {
            tracked[k - 1].Close(ended);

            foreach (var (exchangeId, from, to) in stated.Skip(k))
            {
                history.Add(InstrumentVenueSpell.Record(instrument.Id, exchangeId, from, to, occurredAtUtc));
            }

            return (instrument, true, null);
        }

        return Refuse(symbol, VenueHistoryRejectionReason.ContradictsRecordedHistory,
            $"{instrument.Ticker} already has a recorded venue history, and this one differs from it other than "
                + "by a later move. History is not rewritten by an import.");
    }

    /// <summary>Says what keeps an ordered set of spells from tiling time, if anything.</summary>
    private static string? Contiguity(List<ProviderVenueSpell> ordered)
    {
        if (ordered[0].EffectiveFrom is not null)
        {
            return $"Line {ordered[0].LineNumber}: the first spell must have no start — it is the venue "
                + "the instrument was on before anything recorded.";
        }

        if (ordered[^1].EffectiveTo is not null)
        {
            return $"Line {ordered[^1].LineNumber}: the last spell must have no end — it is where the "
                + "instrument trades now.";
        }

        for (var index = 1; index < ordered.Count; index++)
        {
            var before = ordered[index - 1];
            var after = ordered[index];

            if (before.EffectiveTo is not { } end || after.EffectiveFrom != end)
            {
                return $"Line {after.LineNumber}: a spell must begin on the session the one before it ended "
                    + $"({Day(before.EffectiveTo)}), and this one begins on {Day(after.EffectiveFrom)}.";
            }

            if (after.EffectiveTo is { } next && next <= end)
            {
                return $"Line {after.LineNumber}: a spell must end after it starts.";
            }

            if (string.Equals(before.Venue.Trim(), after.Venue.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return $"Line {after.LineNumber}: two consecutive spells on {after.Venue} are one spell.";
            }
        }

        return null;
    }

    /// <summary>
    /// Judges every open price-limit breach of an instrument again, on the
    /// venue each session traded on.
    /// </summary>
    private async Task<int> RejudgeAsync(
        Instrument instrument,
        IEnumerable<Exchange> venues,
        CancellationToken cancellationToken)
    {
        if (instrument.AssetType == AssetType.Index)
        {
            return 0;
        }

        var spells = await history.ListAsync([instrument.Id], cancellationToken).ConfigureAwait(false);
        var timeline = new VenueTimeline(instrument.ExchangeId, spells);

        // Sessions in the current spell were always judged on the current
        // venue, and nothing about them changes. A stale history — the master
        // has moved the name since — answers null, and is left alone.
        if (timeline.CurrentSince is not { } since)
        {
            return 0;
        }

        var byId = venues.ToDictionary(venue => venue.Id);
        var firstOnCurrent = Midnight(since);

        // Read by period, not by count: the oldest findings are the ones on the
        // old venue, and a count would cut them off first.
        var candidates = await issues
            .ListAsync(instrument.Id, BarInterval.OneDay, Earliest, firstOnCurrent.AddDays(1), cancellationToken)
            .ConfigureAwait(false);

        var rejudged = 0;

        foreach (var finding in candidates.Where(issue => issue.IsOpen && issue.Kind == DataQualityIssueKind.PriceLimitBreach))
        {
            var session = DateOnly.FromDateTime(finding.SessionAtUtc.UtcDateTime);
            var venue = byId.GetValueOrDefault(timeline.On(session));
            string? explanation;

            if (timeline.IsFirstSessionAfterMove(session))
            {
                // A first session after a move is priced from the old venue and
                // banded by the new venue's listing-day rule, not its ordinary one.
                explanation = $"The first session on {venue?.Code.Value ?? "its new venue"} after a recorded move: "
                    + "a listing-day band applies there, not the ordinary one this finding measured against.";
            }
            else
            {
                explanation = venue is { DailyPriceLimit: { } band }
                    && await PermitsAsync(instrument.Id, finding.SessionAtUtc, band, cancellationToken).ConfigureAwait(false)
                        ? $"Inside {venue.Code}'s {band} band: the session traded on {venue.Code}, by the recorded venue history."
                        : null;
            }

            if (explanation is null
                || await issues.FindAsync(finding.Id, cancellationToken).ConfigureAwait(false) is not { IsOpen: true } tracked)
            {
                continue;
            }

            tracked.Explain(explanation, clock.UtcNow);
            rejudged++;
        }

        // And the other direction: a session on an old venue with a narrower
        // band than the current one was measured too leniently. The inspector,
        // run over the old venue's stretches, raises what it should have; its
        // own record of what is already raised keeps it from doubling anything.
        await inspector
            .InspectAsync(instrument.Id, BarInterval.OneDay, Earliest, firstOnCurrent, [], cancellationToken)
            .ConfigureAwait(false);

        return rejudged;
    }

    /// <summary>
    /// Says whether a session's close moved from the last close before it
    /// within a band, however long ago that close was.
    /// </summary>
    private async Task<bool> PermitsAsync(
        InstrumentId instrumentId,
        DateTimeOffset sessionAtUtc,
        PriceLimit band,
        CancellationToken cancellationToken)
    {
        var onSession = await bars
            .ListForUpdateAsync(instrumentId, BarInterval.OneDay, sessionAtUtc, sessionAtUtc.AddDays(1), cancellationToken)
            .ConfigureAwait(false);

        var today = onSession.Count > 0 ? onSession[0] : null;

        var before = await bars
            .FindLastBeforeAsync(instrumentId, BarInterval.OneDay, sessionAtUtc, cancellationToken)
            .ConfigureAwait(false);

        return today is not null
            && before is not null
            && band.Permits(before.Close.Value, today.Close.Value, BarQualityInspector.PriceLimitTolerance);
    }

    private static DateTimeOffset Midnight(DateOnly date) =>
        new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    /// <summary>Resolves a source's symbol the way the corporate action import does.</summary>
    private async Task<Instrument?> ResolveAsync(
        string symbol,
        SourceCode source,
        CancellationToken cancellationToken)
    {
        if (!ProviderSymbol.TryParse(symbol, out var parsed, out _)
            || !IdentifierValue.TryCreate(IdentifierScheme.ProviderSymbol, parsed.Raw, out var alias, out _))
        {
            return null;
        }

        var identifier = await instruments.FindIdentifierAsync(alias, source, cancellationToken).ConfigureAwait(false);

        return identifier is null
            ? null
            : await instruments.FindByIdAsync(identifier.InstrumentId, cancellationToken).ConfigureAwait(false);
    }

    private static (Instrument? Instrument, bool Changed, VenueHistoryRejection? Rejection) Refuse(
        string symbol,
        VenueHistoryRejectionReason reason,
        string detail) =>
        (null, false, new VenueHistoryRejection(symbol, reason, detail));

    private static string Day(DateOnly? date) =>
        date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "none";
}
