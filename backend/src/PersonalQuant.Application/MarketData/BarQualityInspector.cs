using System.Globalization;
using Microsoft.Extensions.Logging;
using PersonalQuant.Application.Diagnostics;
using PersonalQuant.Application.Exchanges;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Application.MarketData;

/// <summary>
/// Applies the quality rules that a single bar cannot answer on its own.
/// </summary>
/// <remarks>
/// <para>
/// Three checks, all of which need something outside the row: the previous
/// session's close, and the venue's trading calendar. Everything a bar can be
/// asked about itself is already a domain invariant enforced at construction,
/// and duplicates are impossible because the storage key forbids them.
/// </para>
/// <para>
/// Nothing is corrected and nothing is deleted. A finding is written down, the
/// bar stays, and a consumer that cannot tolerate an unexplained discontinuity
/// can see it and stop.
/// </para>
/// <para>
/// Findings are staged, never committed here. The caller owns the transaction,
/// which is what lets ingestion store a bar and the finding about it together —
/// a bar committed without its finding would look clean until something
/// re-checked it, and nothing would know to.
/// </para>
/// </remarks>
public interface IBarQualityInspector
{
    /// <summary>
    /// Checks a series over a range and records what it finds.
    /// </summary>
    /// <remarks>
    /// Idempotent. Re-running over the same range raises nothing new, because a
    /// finding already recorded for a session and kind is left alone — which is
    /// also what stops a nightly run undoing yesterday's dismissal.
    /// </remarks>
    /// <param name="instrumentId">The instrument.</param>
    /// <param name="interval">The resolution.</param>
    /// <param name="fromUtc">The inclusive start of the range.</param>
    /// <param name="toUtc">The exclusive end of the range.</param>
    /// <param name="pending">
    /// Bars staged in the caller's unit of work but not yet committed, so that
    /// ingestion can check what it has just produced in the same transaction
    /// that stores it. Empty for a standalone re-check.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>What the check found.</returns>
    Task<QualityInspection> InspectAsync(
        InstrumentId instrumentId,
        BarInterval interval,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        IReadOnlyList<OhlcvBar> pending,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// What one quality check did.
/// </summary>
/// <param name="BarsInspected">Bars the rules ran over.</param>
/// <param name="SessionsExpected">
/// Trading days the calendar says the range holds, or zero when the calendar
/// does not cover it and the number is therefore unknown.
/// </param>
/// <param name="Raised">Findings recorded by this run.</param>
/// <param name="Skipped">
/// Why the check did not run, or <see langword="null"/> when it did.
/// </param>
public sealed record QualityInspection(
    int BarsInspected,
    int SessionsExpected,
    IReadOnlyList<DataQualityIssue> Raised,
    string? Skipped)
{
    /// <summary>A check that did not run, and why.</summary>
    /// <param name="reason">Why it did not run.</param>
    /// <returns>An empty inspection.</returns>
    public static QualityInspection NotRun(string reason) => new(0, 0, [], reason);
}

/// <summary>
/// Default <see cref="IBarQualityInspector"/>.
/// </summary>
/// <remarks>
/// <para>
/// Daily bars only. A price limit governs a <em>session</em>, so comparing two
/// five-minute bars against it would flag nothing on a day a security moved its
/// full band and would flag everything on a day it gapped at the open. Missing
/// and unexpected sessions are session-scoped for the same reason. Applying the
/// rules to intraday data would produce numbers that look like quality
/// measurements and are not.
/// </para>
/// <para>
/// Indices are exempt from the price-limit check. A limit binds orders, and an
/// index is calculated rather than traded — VN-Index has no band to breach, and
/// checking it against one would raise a finding on every volatile day.
/// </para>
/// </remarks>
/// <param name="instruments">Resolves the instrument's venue and asset class.</param>
/// <param name="exchanges">Supplies the venue's price limit.</param>
/// <param name="calendar">Supplies the venue's trading days.</param>
/// <param name="bars">The canonical series.</param>
/// <param name="issues">Where findings are recorded.</param>
/// <param name="clock">Supplies the detection instant.</param>
/// <param name="logger">Logger for quality telemetry.</param>
internal sealed class BarQualityInspector(
    IInstrumentRepository instruments,
    IExchangeRepository exchanges,
    ITradingCalendar calendar,
    IBarRepository bars,
    IDataQualityRepository issues,
    Abstractions.IClock clock,
    ILogger<BarQualityInspector> logger) : IBarQualityInspector
{
    /// <summary>
    /// Extra fractional room allowed on top of a venue's band.
    /// </summary>
    /// <remarks>
    /// The reference price is rounded to a tick before the band is computed, so
    /// a realised move can exceed the nominal percentage slightly without
    /// anything being wrong. Half a per cent is comfortably above that rounding
    /// and far below the smallest move that would indicate a corporate action.
    /// </remarks>
    private const decimal PriceLimitTolerance = 0.005m;

    /// <inheritdoc />
    public async Task<QualityInspection> InspectAsync(
        InstrumentId instrumentId,
        BarInterval interval,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        IReadOnlyList<OhlcvBar> pending,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pending);

        if (interval != BarInterval.OneDay)
        {
            return QualityInspection.NotRun(
                $"The quality rules are session-scoped and do not apply to {interval} bars.");
        }

        if (toUtc <= fromUtc)
        {
            return QualityInspection.NotRun("The range ends before it starts.");
        }

        var instrument = await instruments
            .FindByIdAsync(instrumentId, cancellationToken)
            .ConfigureAwait(false);

        if (instrument is null)
        {
            return QualityInspection.NotRun("No instrument exists with that identifier.");
        }

        var exchange = await exchanges
            .FindByIdAsync(instrument.ExchangeId, cancellationToken)
            .ConfigureAwait(false);

        if (exchange is null)
        {
            return QualityInspection.NotRun("The instrument's venue is not held.");
        }

        var committed = await bars
            .ListForUpdateAsync(instrumentId, interval, fromUtc, toUtc, cancellationToken)
            .ConfigureAwait(false);

        // A staged bar cannot share a period with a committed one — the
        // ingestion merge revises in that case rather than adding — but the
        // guard costs nothing and a duplicated period here would double-count
        // every figure downstream.
        var stored = committed
            .Concat(pending.Where(bar =>
                bar.Interval == interval
                && bar.OpenedAtUtc >= fromUtc
                && bar.OpenedAtUtc < toUtc))
            .DistinctBy(bar => bar.OpenedAtUtc)
            .OrderBy(bar => bar.OpenedAtUtc)
            .ToList();

        var window = await calendar
            .LoadAsync(
                exchange.Id,
                DateOnly.FromDateTime(fromUtc.UtcDateTime),
                DateOnly.FromDateTime(toUtc.AddTicks(-1).UtcDateTime),
                cancellationToken)
            .ConfigureAwait(false);

        var existing = await issues
            .ListAsync(instrumentId, interval, fromUtc, toUtc, cancellationToken)
            .ConfigureAwait(false);

        var already = existing
            .Select(issue => (issue.SessionAtUtc, issue.Kind))
            .ToHashSet();

        var detectedAtUtc = clock.UtcNow;
        var raised = new List<DataQualityIssue>();

        RaiseCalendarFindings(
            instrumentId, interval, stored, window, already, detectedAtUtc, raised);

        await RaisePriceLimitFindingsAsync(
                instrument,
                exchange,
                interval,
                fromUtc,
                stored,
                already,
                detectedAtUtc,
                raised,
                cancellationToken)
            .ConfigureAwait(false);

        foreach (var issue in raised)
        {
            issues.Add(issue);
        }

        // Stamped whatever was found: the version says the rules ran, not that
        // they were satisfied. What they concluded is the findings themselves.
        foreach (var bar in stored)
        {
            bar.MarkValidated(DataRules.ValidationVersion);
        }

        // Zero when the calendar does not cover the range, because the number
        // is then unknown rather than zero — and reporting the weekday count
        // would assert that every weekday was a trading day, which is the
        // claim the calendar exists to refute.
        var expected = window.IsComplete ? window.TradingDays().Count() : 0;

        if (raised.Count > 0)
        {
            foreach (var group in raised.GroupBy(issue => issue.Kind))
            {
                ApplicationLog.DataQualityIssuesRaised(
                    logger,
                    instrument.Ticker.Value,
                    group.Key,
                    group.Count(),
                    group.First().Detail);
            }
        }

        return new QualityInspection(stored.Count, expected, raised, null);
    }

    /// <summary>
    /// Compares the sessions the calendar expects against the bars that exist.
    /// </summary>
    /// <remarks>
    /// Skipped entirely when the calendar does not cover the range. Every
    /// public holiday in an uncovered window looks exactly like a failed
    /// ingestion run, and raising a few hundred false findings would bury the
    /// real ones and make the completeness figure meaningless.
    /// </remarks>
    private static void RaiseCalendarFindings(
        InstrumentId instrumentId,
        BarInterval interval,
        List<OhlcvBar> stored,
        TradingCalendarWindow window,
        HashSet<(DateTimeOffset Session, DataQualityIssueKind Kind)> already,
        DateTimeOffset detectedAtUtc,
        List<DataQualityIssue> raised)
    {
        if (!window.IsComplete)
        {
            return;
        }

        var storedDates = stored
            .Select(bar => DateOnly.FromDateTime(bar.OpenedAtUtc.UtcDateTime))
            .ToHashSet();

        foreach (var date in window.TradingDays())
        {
            if (storedDates.Contains(date))
            {
                continue;
            }

            Raise(
                instrumentId,
                interval,
                ToSession(date),
                DataQualityIssueKind.MissingSession,
                $"The calendar expects {window.ExchangeId} to have traded on {date:yyyy-MM-dd}, and no bar is stored.",
                already,
                detectedAtUtc,
                raised);
        }

        foreach (var bar in stored)
        {
            var date = DateOnly.FromDateTime(bar.OpenedAtUtc.UtcDateTime);

            if (window.IsTradingDay(date))
            {
                continue;
            }

            Raise(
                instrumentId,
                interval,
                bar.OpenedAtUtc,
                DataQualityIssueKind.UnexpectedSession,
                $"A bar is stored for {date:yyyy-MM-dd}, which the calendar records as a non-trading day.",
                already,
                detectedAtUtc,
                raised);
        }
    }

    /// <summary>
    /// Compares each session's close against the one before it.
    /// </summary>
    /// <remarks>
    /// The first bar in the range is compared against the last bar stored
    /// <em>before</em> it, not skipped. A run that ingests one day at a time
    /// would otherwise never check anything, because every range would contain
    /// a single bar with no predecessor inside it.
    /// </remarks>
    private async Task RaisePriceLimitFindingsAsync(
        Instrument instrument,
        Exchange exchange,
        BarInterval interval,
        DateTimeOffset fromUtc,
        List<OhlcvBar> stored,
        HashSet<(DateTimeOffset Session, DataQualityIssueKind Kind)> already,
        DateTimeOffset detectedAtUtc,
        List<DataQualityIssue> raised,
        CancellationToken cancellationToken)
    {
        if (exchange.DailyPriceLimit is not { } limit || stored.Count == 0)
        {
            return;
        }

        if (instrument.AssetType == AssetType.Index)
        {
            return;
        }

        var predecessors = await bars
            .ListForUpdateAsync(
                instrument.Id,
                interval,
                fromUtc.AddDays(-14),
                fromUtc,
                cancellationToken)
            .ConfigureAwait(false);

        var previousClose = predecessors.Count > 0 ? predecessors[^1].Close.Value : (decimal?)null;

        foreach (var bar in stored)
        {
            if (previousClose is { } reference && !limit.Permits(reference, bar.Close.Value, PriceLimitTolerance))
            {
                var move = (bar.Close.Value - reference) / reference;

                Raise(
                    instrument.Id,
                    interval,
                    bar.OpenedAtUtc,
                    DataQualityIssueKind.PriceLimitBreach,
                    $"The close moved {move.ToString("P2", CultureInfo.InvariantCulture)} from {reference.ToString(CultureInfo.InvariantCulture)}, "
                    + $"beyond {exchange.Code}'s {limit} band. A corporate action, a bad print, a halt or a symbol change accounts for it.",
                    already,
                    detectedAtUtc,
                    raised);
            }

            previousClose = bar.Close.Value;
        }
    }

    private static void Raise(
        InstrumentId instrumentId,
        BarInterval interval,
        DateTimeOffset sessionAtUtc,
        DataQualityIssueKind kind,
        string detail,
        HashSet<(DateTimeOffset Session, DataQualityIssueKind Kind)> already,
        DateTimeOffset detectedAtUtc,
        List<DataQualityIssue> raised)
    {
        // Both a database read and this set are consulted: the read stops a
        // finding recorded last night being raised again, and the set stops one
        // raised moments ago in this same run being raised twice.
        if (!already.Add((sessionAtUtc, kind)))
        {
            return;
        }

        raised.Add(DataQualityIssue.Raise(
            instrumentId,
            interval,
            sessionAtUtc,
            kind,
            detail,
            DataRules.ValidationVersion,
            detectedAtUtc));
    }

    private static DateTimeOffset ToSession(DateOnly date) =>
        new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
