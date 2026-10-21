using PersonalQuant.Application.CorporateActions;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Domain.CorporateActions;
using PersonalQuant.Domain.Fundamentals;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Application.Fundamentals;

/// <summary>Reads an issuer's point-in-time ratios for a session (ADR-030).</summary>
public interface IFundamentalRatioService
{
    /// <summary>Computes the ratios as they could be known when a session opened.</summary>
    /// <param name="instrumentId">The issuer's instrument.</param>
    /// <param name="session">The session; its close prices the ratios.</param>
    /// <param name="knownAsOfUtc">
    /// A dataset's observation instant (ADR-031), or null for what PQT holds
    /// now. Statements and actions are then read at the earlier of it and the
    /// session's 00:00 UTC, and only what this system had recorded by it is
    /// read, the close included.
    /// </param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The ratios, each known or absent with a reason.</returns>
    Task<FundamentalRatios> ReadAsync(
        InstrumentId instrumentId,
        DateOnly session,
        DateTimeOffset? knownAsOfUtc = null,
        CancellationToken cancellationToken = default);

    /// <summary>Computes the ratios at many sessions, loading the issuer's rows once.</summary>
    /// <remarks>Each result equals <see cref="ReadAsync"/> for its session.</remarks>
    /// <param name="instrumentId">The issuer's instrument.</param>
    /// <param name="sessions">The sessions, in the order the results are wanted.</param>
    /// <param name="knownAsOfUtc">The observation instant, as for <see cref="ReadAsync"/>.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>One result per session, in the same order.</returns>
    Task<IReadOnlyList<FundamentalRatios>> ReadSeriesAsync(
        InstrumentId instrumentId,
        IReadOnlyList<DateOnly> sessions,
        DateTimeOffset? knownAsOfUtc = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Why a ratio has no value (ADR-030).</summary>
public enum RatioAbsence
{
    /// <summary>Not specified.</summary>
    Unspecified = 0,

    /// <summary>No balance sheet is known by the session.</summary>
    NoBalanceSheet = 1,

    /// <summary>The latest balance sheet states no usable charter capital.</summary>
    NoCharterCapital = 2,

    /// <summary>No quarter's profit is known.</summary>
    NoQuarterlyProfit = 3,

    /// <summary>One of the four trailing quarters is not known.</summary>
    MissingQuarter = 4,

    /// <summary>No bar for the session.</summary>
    NoClose = 5,

    /// <summary>Trailing profit is a loss, so there is no earnings multiple.</summary>
    TrailingLoss = 6,

    /// <summary>Equity to the parent is zero or negative.</summary>
    EquityNotPositive = 7,

    /// <summary>Equity is not known at both ends of the trailing year.</summary>
    EquityNotKnownAtBothEnds = 8,

    /// <summary>
    /// The corporate action history does not reach back over the trailing year
    /// (ratio version 1 only; ADR-032 replaced the test with a recorded sweep).
    /// </summary>
    NoDividendCoverage = 9,

    /// <summary>
    /// No recorded sweep vouches for every action of the issuer over the
    /// trailing year, so "nothing paid" cannot be told from "not recorded" (ADR-032).
    /// </summary>
    DividendHistoryIncomplete = 10,
}

/// <summary>One ratio: a value, or the reason there is none.</summary>
/// <param name="Value">The value, or null.</param>
/// <param name="Absent">Why there is no value, in words, or null when there is one.</param>
/// <param name="Reason">Why there is no value, as a code, or null when there is one.</param>
public sealed record RatioValue(decimal? Value, string? Absent, RatioAbsence? Reason = null)
{
    /// <summary>A known value.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The ratio.</returns>
    public static RatioValue Known(decimal value) => new(value, null);

    /// <summary>An absent value.</summary>
    /// <param name="reason">Why, as a code.</param>
    /// <param name="detail">Why, in words.</param>
    /// <returns>The ratio.</returns>
    public static RatioValue Missing(RatioAbsence reason, string detail) => new(null, detail, reason);

    /// <summary>The same absence, carried to a ratio built on this one.</summary>
    /// <returns>The ratio.</returns>
    public RatioValue Carried() => new(null, Absent, Reason);
}

/// <summary>An issuer's ratios at a session, and what they were built from.</summary>
/// <param name="InstrumentId">The issuer's instrument.</param>
/// <param name="Session">The session.</param>
/// <param name="KnownAsOfUtc">The instant the statements and actions were read at.</param>
/// <param name="Scope">The statements used: consolidated, or separate for an issuer with no consolidated values known.</param>
/// <param name="Close">The raw close of the session.</param>
/// <param name="Shares">Shares outstanding at the session, adjusted for actions since the balance sheet.</param>
/// <param name="NetProfitTtm">Net profit to the parent over the four latest quarters.</param>
/// <param name="EpsTtm">Earnings per share over those quarters.</param>
/// <param name="BookValuePerShare">Equity to the parent per share.</param>
/// <param name="PriceToEarnings">Close over EPS.</param>
/// <param name="PriceToBook">Close over book value per share.</param>
/// <param name="ReturnOnEquity">Trailing profit over average equity to the parent.</param>
/// <param name="DividendYield">Trailing cash dividends per share over the close.</param>
/// <param name="BalanceSheet">The quarter whose balance sheet was used.</param>
/// <param name="BalanceSheetReports">The reports that stated that balance sheet's values, ordered by id.</param>
/// <param name="ProfitQuarters">The four quarters summed, oldest first.</param>
/// <param name="ActionsApplied">Share-count actions applied to the balance sheet's count.</param>
/// <param name="ActionsWithheld">Actions in the window not yet announced, or never dated.</param>
/// <param name="Findings">What the share-count checks found, in words; empty when they found nothing.</param>
public sealed record FundamentalRatios(
    InstrumentId InstrumentId,
    DateOnly Session,
    DateTimeOffset KnownAsOfUtc,
    FinancialStatementScope? Scope,
    RatioValue Close,
    RatioValue Shares,
    RatioValue NetProfitTtm,
    RatioValue EpsTtm,
    RatioValue BookValuePerShare,
    RatioValue PriceToEarnings,
    RatioValue PriceToBook,
    RatioValue ReturnOnEquity,
    RatioValue DividendYield,
    FiscalQuarter? BalanceSheet,
    IReadOnlyList<FinancialReportId> BalanceSheetReports,
    IReadOnlyList<FiscalQuarter> ProfitQuarters,
    int ActionsApplied,
    int ActionsWithheld,
    IReadOnlyList<string> Findings);

/// <summary>
/// Computes point-in-time ratios on read (ADR-030).
/// </summary>
/// <remarks>
/// <para>
/// Nothing is stored. Each read goes through the as-of statement read, the
/// session's raw close and the actions announced by then, so a restatement or
/// a late-announced action changes every later ratio and no earlier one.
/// </para>
/// <para>
/// A cancelled action counts nowhere, at any session. No cancellation date is
/// recorded, and an action called off before its ex-date never changed a
/// share, while one "cancelled" after it is a correction of the record. Only
/// actions that went ex by the session are read, so excluding it borrows
/// nothing from the future.
/// </para>
/// </remarks>
/// <param name="fundamentals">The point-in-time statement read.</param>
/// <param name="bars">The session's close.</param>
/// <param name="actions">Share-count factors and cash dividends.</param>
/// <param name="coverage">The sweeps that say over which ex-dates an issuer's actions are complete.</param>
public sealed class FundamentalRatioService(
    IFundamentalsReader fundamentals,
    IBarRepository bars,
    ICorporateActionRepository actions,
    ICorporateActionCoverageRepository coverage) : IFundamentalRatioService
{
    /// <summary>
    /// The rules the ratios are computed under, as a dataset records them
    /// (ADR-031). ADR-030 with its two checks is 1. Version 2 (ADR-032) adds
    /// the finding for a capital step with no action, and reads dividend
    /// coverage from a recorded sweep instead of the earliest action.
    /// </summary>
    public const int RatioVersion = 2;

    /// <summary>The par value of a Vietnamese common share, in đồng.</summary>
    public const decimal ParValue = 10_000m;

    /// <summary>The widest a capital step may miss an action's factor and still be that action.</summary>
    /// <remarks>
    /// Two percent: employee share issues move capital by about one percent a
    /// year, and the smallest stock dividend seen is five. A smaller factor
    /// narrows it to half its own size, so two actions are never confused.
    /// </remarks>
    internal const decimal StepTolerance = 0.02m;

    /// <summary>How far profit per par share may stray from reported EPS before the share count is doubted.</summary>
    /// <remarks>
    /// Reported EPS deducts the bonus and welfare fund and is weighted over
    /// the period, so it runs below profit per share by up to a sixth. Twice
    /// either way is far outside that, and well inside a wrong par or unit.
    /// </remarks>
    internal const decimal ParCheckBand = 2m;

    /// <inheritdoc />
    public async Task<FundamentalRatios> ReadAsync(
        InstrumentId instrumentId,
        DateOnly session,
        DateTimeOffset? knownAsOfUtc = null,
        CancellationToken cancellationToken = default) =>
        (await ReadSeriesAsync(instrumentId, [session], knownAsOfUtc, cancellationToken).ConfigureAwait(false))[0];

    /// <inheritdoc />
    public async Task<IReadOnlyList<FundamentalRatios>> ReadSeriesAsync(
        InstrumentId instrumentId,
        IReadOnlyList<DateOnly> sessions,
        DateTimeOffset? knownAsOfUtc = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        if (knownAsOfUtc is { Offset.Ticks: not 0 })
        {
            throw new ArgumentException("The observation instant must be UTC.", nameof(knownAsOfUtc));
        }

        var history = await fundamentals.LoadAsync(instrumentId, knownAsOfUtc, cancellationToken).ConfigureAwait(false);
        var corporateActions = await actions.ListAsync(instrumentId, cancellationToken).ConfigureAwait(false);

        // A sweep recorded after the observation instant was not known then.
        var sweeps = (await coverage.ListAsync(instrumentId, cancellationToken).ConfigureAwait(false))
            .Where(sweep => knownAsOfUtc is not { } observed || sweep.RecordedAtUtc <= observed)
            .ToList();
        var cancelled = corporateActions.Where(action => action.IsCancelled).Select(action => action.Id).ToHashSet();
        var adjustments = (await actions.ListAdjustmentsAsync(instrumentId, cancellationToken).ConfigureAwait(false))
            .Where(adjustment => !cancelled.Contains(adjustment.CorporateActionId))
            .ToList();

        var results = new List<FundamentalRatios>(sessions.Count);

        foreach (var session in sessions)
        {
            var close = await CloseAsync(instrumentId, session, knownAsOfUtc, cancellationToken).ConfigureAwait(false);
            results.Add(Compute(instrumentId, session, knownAsOfUtc, history, corporateActions, adjustments, sweeps, close));
        }

        return results;
    }

    private static FundamentalRatios Compute(
        InstrumentId instrumentId,
        DateOnly session,
        DateTimeOffset? knownAsOfUtc,
        FundamentalsHistory history,
        IReadOnlyList<CorporateAction> corporateActions,
        IReadOnlyList<PriceAdjustment> adjustments,
        IReadOnlyList<CorporateActionCoverage> sweeps,
        RatioValue close)
    {
        // Known before the session opened: 00:00 UTC is 07:00 in Hanoi. A
        // dataset's observation instant bounds it, so a dataset reproduced for
        // a past date cannot see a report filed after it (ADR-031).
        var opened = new DateTimeOffset(session.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var bounded = knownAsOfUtc is { } observed && observed < opened;
        var knownAsOf = bounded ? knownAsOfUtc!.Value : opened;

        // Actions are dated, not timed; one announced on the session's day
        // counts, as before, unless the observation instant falls earlier.
        var announcedBy = bounded ? DateOnly.FromDateTime(knownAsOf.UtcDateTime) : session;

        var consolidated = history.Read(FinancialStatementScope.Consolidated, knownAsOf);
        var statements = consolidated.Values.Count > 0
            ? consolidated
            : history.Read(FinancialStatementScope.Separate, knownAsOf);

        var findings = new List<string>();

        if (corporateActions.Count == 0)
        {
            findings.Add("No corporate action is recorded for this issuer; the share count is not adjusted.");
        }

        var lines = StatementLines.From(statements.Values, statements.Scope);
        var balance = lines.LatestBalanceSheet();

        var (shares, applied, withheld, registeredEarly) = SharesAt(lines, balance, adjustments, session, announcedBy, findings);

        if (!registeredEarly)
        {
            CheckUnexplainedStep(lines, balance, adjustments, session, announcedBy, findings);
        }
        var (profit, quarters) = lines.TrailingProfit();
        CheckPar(lines, quarters, findings);

        var eps = Divide(profit, shares);
        var bookValue = balance is { } quarter && lines.EquityToParent(quarter) is { } equity
            ? Divide(RatioValue.Known(equity), shares)
            : RatioValue.Missing(RatioAbsence.NoBalanceSheet, "no balance sheet known");

        var priceToEarnings = eps.Value is { } perShare
            ? perShare > 0m
                ? Divide(close, eps)
                : RatioValue.Missing(RatioAbsence.TrailingLoss, "trailing loss; no earnings multiple")
            : eps.Carried();

        var priceToBook = bookValue.Value is { } book
            ? book > 0m
                ? Divide(close, bookValue)
                : RatioValue.Missing(RatioAbsence.EquityNotPositive, "book value is not positive")
            : bookValue.Carried();

        var returnOnEquity = ReturnOnEquity(lines, profit, quarters);
        var dividendYield = DividendYield(corporateActions, adjustments, sweeps, session, announcedBy, close);

        IReadOnlyList<FinancialReportId> balanceReports = balance is { } sheet
            ? [.. statements.Values
                .Where(value => value.Statement == FinancialStatement.BalanceSheet && value.Period == sheet && value.Template == lines.Template)
                .Select(value => value.ReportId)
                .Distinct()
                .OrderBy(id => id.Value)]
            : [];

        return new FundamentalRatios(
            instrumentId,
            session,
            knownAsOf,
            statements.Values.Count > 0 ? statements.Scope : null,
            close,
            shares,
            profit,
            eps,
            bookValue,
            priceToEarnings,
            priceToBook,
            returnOnEquity,
            dividendYield,
            balance,
            balanceReports,
            quarters,
            applied,
            withheld,
            findings);
    }

    private async Task<RatioValue> CloseAsync(
        InstrumentId instrumentId,
        DateOnly session,
        DateTimeOffset? knownAsOfUtc,
        CancellationToken cancellationToken)
    {
        var opened = new DateTimeOffset(session.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var next = new DateTimeOffset(session.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        Price? close;

        if (knownAsOfUtc is { } observed)
        {
            // The close as this system held it then, as the dataset's bars are
            // read: a bar recorded later is absent, not filled from today's.
            if (!BarQuery.TryCreate(instrumentId, BarInterval.OneDay, opened, next, 1, out var query, out var problem, adjusted: false, knownAsOfUtc: observed))
            {
                throw new InvalidOperationException($"A close read produced an invalid bar query: {problem}");
            }

            close = (await bars.QueryAsOfAsync(query, cancellationToken).ConfigureAwait(false))
                .LastOrDefault(revision => DateOnly.FromDateTime(revision.OpenedAtUtc.UtcDateTime) == session)?.Close;
        }
        else
        {
            var bar = await bars.FindLastBeforeAsync(instrumentId, BarInterval.OneDay, next, cancellationToken).ConfigureAwait(false);
            close = bar is not null && DateOnly.FromDateTime(bar.OpenedAtUtc.UtcDateTime) == session ? bar.Close : null;
        }

        return close is { } price
            ? RatioValue.Known(price.Value)
            : RatioValue.Missing(RatioAbsence.NoClose, "no close on the session");
    }

    /// <summary>The share count at the session, checked against the capital the balance sheets show.</summary>
    /// <remarks>
    /// <para>
    /// The balance sheet's capital counts every action registered by its
    /// quarter end, and an action that goes ex later is applied on top. That
    /// assumes registration keeps pace with ex-dates, which ADR-030 requires
    /// be checked rather than trusted. Each announced share action is checked
    /// against the step in capital around it:
    /// </para>
    /// <list type="bullet">
    /// <item><description>an action that went ex in the year to the balance sheet, whose step the capital
    /// still lacks, was registered late, and is applied, with a finding;</description></item>
    /// <item><description>an action that goes ex after the balance sheet, whose step the capital already
    /// shows, was registered early, and is not applied twice, with a finding;</description></item>
    /// <item><description>a step that matches no action is reported and changes nothing.</description></item>
    /// </list>
    /// </remarks>
    private static (RatioValue Shares, int Applied, int Withheld, bool RegisteredEarly) SharesAt(
        StatementLines lines,
        FiscalQuarter? balance,
        IReadOnlyList<PriceAdjustment> adjustments,
        DateOnly session,
        DateOnly announcedBy,
        List<string> findings)
    {
        if (balance is not { } quarter)
        {
            return (RatioValue.Missing(RatioAbsence.NoBalanceSheet, "no balance sheet known"), 0, 0, false);
        }

        if (lines.CharterCapital(quarter) is not { } capital || capital <= 0m)
        {
            return (RatioValue.Missing(RatioAbsence.NoCharterCapital, "the balance sheet states no usable charter capital"), 0, 0, false);
        }

        var balanceEnd = StatementLines.QuarterEnd(quarter);
        var capitalQuarters = lines.CapitalQuarters();

        bool Announced(PriceAdjustment adjustment) => adjustment.AnnouncedOn is { } announced && announced <= announcedBy;

        var shareActions = adjustments
            .Where(adjustment => adjustment.Factor.Shares != 1m && adjustment.ExDate <= session && Announced(adjustment))
            .ToList();

        decimal Explained(DateOnly after, DateOnly through) => shareActions
            .Where(action => action.ExDate > after && action.ExDate <= through)
            .Aggregate(1m, (factor, action) => factor * action.Factor.Shares);

        var window = adjustments.Where(adjustment => adjustment.ExDate > balanceEnd && adjustment.ExDate <= session).ToList();
        var applied = new List<PriceAdjustment>();
        var registeredEarly = false;

        // After the balance sheet: applied, unless its capital already shows them.
        var previous = capitalQuarters
            .Select(candidate => (FiscalQuarter?)candidate)
            .LastOrDefault(candidate => candidate!.Value.CompareTo(quarter) < 0);

        foreach (var action in shareActions.Where(action => action.ExDate > balanceEnd))
        {
            if (previous is { } prior && lines.CharterCapital(prior) is { } before)
            {
                var step = capital / before;
                var explained = Explained(StatementLines.QuarterEnd(prior), balanceEnd);

                if (Near(step, explained * action.Factor.Shares, action.Factor.Shares) && !Matches(step, explained))
                {
                    findings.Add(
                        $"The {action.Factor.Shares:0.####}x action of {action.ExDate:yyyy-MM-dd} is already in {quarter}'s capital "
                            + $"(x{step:0.####} since {prior}); it is not applied twice.");
                    registeredEarly = true;
                    continue;
                }
            }

            applied.Add(action);
        }

        // Within the year to the balance sheet: applied if the capital lacks them.
        var yearBefore = StatementLines.QuarterEnd(quarter.YearEarlier);

        foreach (var action in shareActions.Where(action => action.ExDate > yearBefore && action.ExDate <= balanceEnd))
        {
            var earlier = capitalQuarters
                .Select(candidate => (FiscalQuarter?)candidate)
                .LastOrDefault(candidate => StatementLines.QuarterEnd(candidate!.Value) < action.ExDate);

            if (earlier is not { } before || lines.CharterCapital(before) is not { } opening)
            {
                continue;
            }

            var step = capital / opening;
            var explained = Explained(StatementLines.QuarterEnd(before), balanceEnd);

            if (Matches(step, explained))
            {
                continue;
            }

            if (Near(step, explained / action.Factor.Shares, action.Factor.Shares))
            {
                applied.Add(action);
                findings.Add(
                    $"The {action.Factor.Shares:0.####}x action of {action.ExDate:yyyy-MM-dd} is not yet in {quarter}'s capital "
                        + $"(x{step:0.####} since {before}); it is applied as registered late.");
                continue;
            }

            findings.Add(
                $"Capital moved x{step:0.####} from {before} to {quarter}; the actions announced explain x{explained:0.####}. "
                    + "Nothing was changed.");
        }

        var shares = applied.Aggregate(capital / ParValue, (count, action) => count * action.Factor.Shares);
        return (RatioValue.Known(decimal.Round(shares, 0)), applied.Count, window.Count(adjustment => !Announced(adjustment)), registeredEarly);
    }

    /// <summary>
    /// Reports a charter-capital step that no announced action can explain (ADR-032).
    /// </summary>
    /// <remarks>
    /// <para>
    /// ADR-030 checks a step only against an action near it. A step with no
    /// action at all, such as ACB's 15% in 2023Q2, changed every per-share
    /// figure without a word. This compares the latest balance sheet with the
    /// one before it that states capital, and when they differ by more than
    /// <see cref="StepTolerance"/> and no share action announced by the session
    /// went ex between the two quarter ends, says so.
    /// </para>
    /// <para>
    /// A finding only: the count is the balance sheet's, which is probably
    /// right, and no action is invented to explain it. A step with an action
    /// near it, or one an action going ex later was registered early into, is
    /// left to ADR-030's checks, so no step is reported twice.
    /// </para>
    /// </remarks>
    private static void CheckUnexplainedStep(
        StatementLines lines,
        FiscalQuarter? balance,
        IReadOnlyList<PriceAdjustment> adjustments,
        DateOnly session,
        DateOnly announcedBy,
        List<string> findings)
    {
        if (balance is not { } quarter || lines.CharterCapital(quarter) is not ({ } capital and > 0m))
        {
            return;
        }

        var previous = lines.CapitalQuarters()
            .Select(candidate => (FiscalQuarter?)candidate)
            .LastOrDefault(candidate => candidate!.Value.CompareTo(quarter) < 0);

        if (previous is not { } prior || lines.CharterCapital(prior) is not ({ } before and > 0m))
        {
            return;
        }

        var step = capital / before;

        if (Math.Abs(step - 1m) <= StepTolerance)
        {
            return;
        }

        var priorEnd = StatementLines.QuarterEnd(prior);
        var balanceEnd = StatementLines.QuarterEnd(quarter);

        var near = adjustments.Any(adjustment =>
            adjustment.Factor.Shares != 1m
            && adjustment.ExDate > priorEnd
            && adjustment.ExDate <= balanceEnd
            && adjustment.ExDate <= session
            && adjustment.AnnouncedOn is { } announced && announced <= announcedBy);

        if (near)
        {
            return;
        }

        findings.Add(
            $"Capital stepped {(step - 1m) * 100m:+0.0;-0.0}% between {prior} and {quarter} "
                + $"(₫{before:N0} → ₫{capital:N0}) with no corporate action that explains it; the share count is the balance sheet's.");
    }

    /// <summary>Checks the share count's par against the EPS the issuer reports (ADR-030).</summary>
    private static void CheckPar(StatementLines lines, IReadOnlyList<FiscalQuarter> quarters, List<string> findings)
    {
        foreach (var quarter in quarters.Reverse())
        {
            if (lines.ReportedEps(quarter) is not ({ } reported and > 0m)
                || lines.QuarterProfit(quarter) is not ({ } profit and > 0m)
                || lines.CharterCapital(quarter) is not ({ } capital and > 0m))
            {
                continue;
            }

            var implied = profit / (capital / ParValue);
            var ratio = implied / reported;

            if (ratio > ParCheckBand || ratio < 1m / ParCheckBand)
            {
                findings.Add(
                    $"{quarter}: profit per ₫{ParValue:N0} of capital is {implied:N0}, the form reports EPS {reported:N0}; "
                        + "the share count, a unit or the reported EPS is in doubt.");
            }

            return;
        }
    }

    /// <summary>Whether a capital step is the one the actions explain, within employee issues and rounding.</summary>
    private static bool Matches(decimal observed, decimal expected) =>
        expected != 0m && Math.Abs((observed / expected) - 1m) < StepTolerance;

    /// <summary>Whether a capital step is one action's, told apart from the step without it.</summary>
    private static bool Near(decimal observed, decimal expected, decimal factor)
    {
        var tolerance = Math.Min(StepTolerance, Math.Abs(factor - 1m) / 2m);
        return expected != 0m && Math.Abs((observed / expected) - 1m) < tolerance;
    }

    private static RatioValue ReturnOnEquity(StatementLines lines, RatioValue profit, IReadOnlyList<FiscalQuarter> quarters)
    {
        if (profit.Value is not { } trailing || quarters.Count == 0)
        {
            return profit.Carried();
        }

        var latest = quarters[^1];

        if (lines.EquityToParent(latest) is not { } closing || lines.EquityToParent(latest.YearEarlier) is not { } opening)
        {
            return RatioValue.Missing(RatioAbsence.EquityNotKnownAtBothEnds, "equity is not known at both ends of the trailing year");
        }

        var average = (closing + opening) / 2m;
        return average > 0m
            ? RatioValue.Known(trailing / average)
            : RatioValue.Missing(RatioAbsence.EquityNotPositive, "average equity is not positive");
    }

    private static RatioValue DividendYield(
        IReadOnlyList<CorporateAction> corporateActions,
        IReadOnlyList<PriceAdjustment> adjustments,
        IReadOnlyList<CorporateActionCoverage> sweeps,
        DateOnly session,
        DateOnly announcedBy,
        RatioValue close)
    {
        if (close.Value is not { } price || price <= 0m)
        {
            return close.Carried();
        }

        var from = session.AddDays(-365);

        // A zero yield is a statement that nothing was paid, which only a sweep
        // of every notice over the whole trailing year can make (ADR-032).
        if (!sweeps.Any(sweep => sweep.Covers(from, session)))
        {
            return RatioValue.Missing(
                RatioAbsence.DividendHistoryIncomplete,
                sweeps.Count == 0
                    ? "no sweep of this issuer's corporate actions is recorded"
                    : $"no recorded sweep covers every action from {from:yyyy-MM-dd} to {session:yyyy-MM-dd}");
        }

        var perShare = 0m;

        foreach (var dividend in corporateActions.Where(action =>
            action.Type == CorporateActionType.CashDividend
            && !action.IsCancelled
            && action.ExDate > from
            && action.ExDate <= session
            && action.AnnouncedOn is { } announced && announced <= announcedBy
            && action.CashAmount is > 0m))
        {
            // Stated on today's share basis: a bonus issue after the dividend
            // means today's share received less per share than the headline.
            var since = adjustments
                .Where(adjustment => adjustment.ExDate > dividend.ExDate && adjustment.ExDate <= session
                    && adjustment.AnnouncedOn is { } known && known <= announcedBy)
                .Aggregate(1m, (factor, adjustment) => factor * adjustment.Factor.Shares);

            perShare += dividend.CashAmount!.Value / since;
        }

        return RatioValue.Known(perShare / price);
    }

    private static RatioValue Divide(RatioValue numerator, RatioValue denominator)
    {
        if (numerator.Value is not { } top)
        {
            return numerator.Carried();
        }

        if (denominator.Value is not { } bottom || bottom == 0m)
        {
            return denominator.Value is null ? denominator.Carried() : RatioValue.Missing(RatioAbsence.Unspecified, "division by zero");
        }

        return RatioValue.Known(top / bottom);
    }
}
