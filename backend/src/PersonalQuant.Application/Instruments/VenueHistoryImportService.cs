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
/// <param name="unitOfWork">Commits the run.</param>
/// <param name="clock">Supplies the audit timestamps.</param>
internal sealed class VenueHistoryImportService(
    IEnumerable<IVenueHistoryProvider> providers,
    IInstrumentRepository instruments,
    IExchangeRepository exchanges,
    IVenueHistoryRepository history,
    IBarRepository bars,
    IDataQualityRepository issues,
    IUnitOfWork unitOfWork,
    IClock clock) : IVenueHistoryImportService
{
    /// <summary>The most open findings re-judged per instrument in one run.</summary>
    private const int MaxFindingsPerInstrument = 1_000;

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
        var recorded = new List<Instrument>();
        var unchanged = 0;

        foreach (var group in rows.GroupBy(row => row.Symbol.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            var outcome = await ApplyAsync(
                    group.Key, [.. group], provider.Code, venues, occurredAtUtc, cancellationToken)
                .ConfigureAwait(false);

            switch (outcome)
            {
                case { Rejection: { } rejection }:
                    rejections.Add(rejection);
                    break;
                case { Recorded: { } instrument }:
                    recorded.Add(instrument);
                    break;
                default:
                    unchanged++;
                    break;
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var rejudged = 0;

        foreach (var instrument in recorded)
        {
            rejudged += await RejudgeAsync(instrument, venues.Values, cancellationToken).ConfigureAwait(false);
        }

        if (rejudged > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new VenueHistoryImportReport(
            provider.Code.Value, rows.Count, recorded.Count, unchanged, rejudged, rejections);
    }

    private async Task<(Instrument? Recorded, VenueHistoryRejection? Rejection)> ApplyAsync(
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

        var existing = (await history.ListAsync([instrument.Id], cancellationToken).ConfigureAwait(false))
            .OrderBy(spell => spell.EffectiveFrom ?? DateOnly.MinValue)
            .Select(spell => (spell.ExchangeId, spell.EffectiveFrom, spell.EffectiveTo))
            .ToList();

        if (existing.Count > 0)
        {
            return existing.SequenceEqual(stated)
                ? (null, null)
                : Refuse(symbol, VenueHistoryRejectionReason.ContradictsRecordedHistory,
                    $"{instrument.Ticker} already has a recorded venue history, and this one differs. "
                        + "History is not rewritten by an import.");
        }

        foreach (var (exchangeId, from, to) in stated)
        {
            history.Add(InstrumentVenueSpell.Record(instrument.Id, exchangeId, from, to, occurredAtUtc));
        }

        return (instrument, null);
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
        var byId = venues.ToDictionary(venue => venue.Id);

        var open = await issues
            .ListOpenAsync(instrument.Id, BarInterval.OneDay, MaxFindingsPerInstrument, cancellationToken)
            .ConfigureAwait(false);

        var rejudged = 0;

        foreach (var finding in open.Where(issue => issue.Kind == DataQualityIssueKind.PriceLimitBreach))
        {
            var session = DateOnly.FromDateTime(finding.SessionAtUtc.UtcDateTime);

            if (!byId.TryGetValue(timeline.On(session), out var venue) || venue.DailyPriceLimit is not { } band)
            {
                continue;
            }

            var window = await bars
                .ListForUpdateAsync(
                    instrument.Id,
                    BarInterval.OneDay,
                    finding.SessionAtUtc.AddDays(-14),
                    finding.SessionAtUtc.AddDays(1),
                    cancellationToken)
                .ConfigureAwait(false);

            var today = window.FirstOrDefault(bar => bar.OpenedAtUtc == finding.SessionAtUtc);
            var before = window.LastOrDefault(bar => bar.OpenedAtUtc < finding.SessionAtUtc);

            if (today is null || before is null
                || !band.Permits(before.Close.Value, today.Close.Value, BarQualityInspector.PriceLimitTolerance))
            {
                continue;
            }

            var tracked = await issues.FindAsync(finding.Id, cancellationToken).ConfigureAwait(false);

            if (tracked is not { IsOpen: true })
            {
                continue;
            }

            tracked.Explain(
                $"Inside {venue.Code}'s {band} band: the session traded on {venue.Code}, by the recorded venue history.",
                clock.UtcNow);

            rejudged++;
        }

        return rejudged;
    }

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

    private static (Instrument? Recorded, VenueHistoryRejection? Rejection) Refuse(
        string symbol,
        VenueHistoryRejectionReason reason,
        string detail) =>
        (null, new VenueHistoryRejection(symbol, reason, detail));

    private static string Day(DateOnly? date) =>
        date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "none";
}
