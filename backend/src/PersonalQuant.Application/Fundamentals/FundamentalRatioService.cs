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
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The ratios, each known or absent with a reason.</returns>
    Task<FundamentalRatios> ReadAsync(
        InstrumentId instrumentId,
        DateOnly session,
        CancellationToken cancellationToken = default);
}

/// <summary>One ratio: a value, or the reason there is none.</summary>
/// <param name="Value">The value, or null.</param>
/// <param name="Absent">Why there is no value, or null when there is one.</param>
public sealed record RatioValue(decimal? Value, string? Absent)
{
    /// <summary>A known value.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The ratio.</returns>
    public static RatioValue Known(decimal value) => new(value, null);

    /// <summary>An absent value.</summary>
    /// <param name="reason">Why.</param>
    /// <returns>The ratio.</returns>
    public static RatioValue Missing(string reason) => new(null, reason);
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
/// <param name="ProfitQuarters">The four quarters summed, oldest first.</param>
/// <param name="ActionsApplied">Share-count actions applied since the balance sheet.</param>
/// <param name="ActionsWithheld">Actions in the window not yet announced, or never dated.</param>
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
    IReadOnlyList<FiscalQuarter> ProfitQuarters,
    int ActionsApplied,
    int ActionsWithheld);

/// <summary>
/// Computes point-in-time ratios on read (ADR-030).
/// </summary>
/// <remarks>
/// Nothing is stored. Each read goes through the as-of statement read, the
/// session's raw close and the actions announced by then, so a restatement or
/// a late-announced action changes every later ratio and no earlier one.
/// </remarks>
/// <param name="fundamentals">The point-in-time statement read.</param>
/// <param name="bars">The session's close.</param>
/// <param name="actions">Share-count factors and cash dividends.</param>
public sealed class FundamentalRatioService(
    IFundamentalsReader fundamentals,
    IBarRepository bars,
    ICorporateActionRepository actions) : IFundamentalRatioService
{
    /// <summary>The par value of a Vietnamese common share, in đồng.</summary>
    public const decimal ParValue = 10_000m;

    /// <inheritdoc />
    public async Task<FundamentalRatios> ReadAsync(
        InstrumentId instrumentId,
        DateOnly session,
        CancellationToken cancellationToken = default)
    {
        // Known before the session opened: 00:00 UTC is 07:00 in Hanoi, the
        // cut dataset export uses for --known-as-of.
        var knownAsOf = new DateTimeOffset(session.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var consolidated = await fundamentals.ReadAsync(
            instrumentId, FinancialStatementScope.Consolidated, knownAsOf, cancellationToken: cancellationToken).ConfigureAwait(false);
        var statements = consolidated.Values.Count > 0
            ? consolidated
            : await fundamentals.ReadAsync(
                instrumentId, FinancialStatementScope.Separate, knownAsOf, cancellationToken: cancellationToken).ConfigureAwait(false);

        var close = await CloseAsync(instrumentId, session, cancellationToken).ConfigureAwait(false);
        var adjustments = await actions.ListAdjustmentsAsync(instrumentId, cancellationToken).ConfigureAwait(false);
        var corporateActions = await actions.ListAsync(instrumentId, cancellationToken).ConfigureAwait(false);

        var lines = StatementLines.From(statements.Values, statements.Scope);
        var balance = lines.LatestBalanceSheet();

        var (shares, applied, withheld) = SharesAt(lines, balance, adjustments, session);
        var (profit, quarters) = lines.TrailingProfit();

        var eps = Divide(profit, shares, "no trailing profit", "no share count");
        var bookValue = balance is { } quarter && lines.EquityToParent(quarter) is { } equity
            ? Divide(RatioValue.Known(equity), shares, string.Empty, "no share count")
            : RatioValue.Missing("no balance sheet known");

        var priceToEarnings = eps.Value is { } perShare
            ? perShare > 0m
                ? Divide(close, eps, "no close on the session", string.Empty)
                : RatioValue.Missing("trailing loss; no earnings multiple")
            : RatioValue.Missing(eps.Absent!);

        var priceToBook = bookValue.Value is { } book && book > 0m
            ? Divide(close, bookValue, "no close on the session", string.Empty)
            : RatioValue.Missing(bookValue.Absent ?? "book value is not positive");

        var returnOnEquity = ReturnOnEquity(lines, profit, quarters);
        var dividendYield = DividendYield(corporateActions, adjustments, session, close);

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
            quarters,
            applied,
            withheld);
    }

    private async Task<RatioValue> CloseAsync(InstrumentId instrumentId, DateOnly session, CancellationToken cancellationToken)
    {
        var next = new DateTimeOffset(session.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var bar = await bars.FindLastBeforeAsync(instrumentId, BarInterval.OneDay, next, cancellationToken).ConfigureAwait(false);

        return bar is not null && DateOnly.FromDateTime(bar.OpenedAtUtc.UtcDateTime) == session
            ? RatioValue.Known(bar.Close.Value)
            : RatioValue.Missing("no close on the session");
    }

    private static (RatioValue Shares, int Applied, int Withheld) SharesAt(
        StatementLines lines,
        FiscalQuarter? balance,
        IReadOnlyList<PriceAdjustment> adjustments,
        DateOnly session)
    {
        if (balance is not { } quarter)
        {
            return (RatioValue.Missing("no balance sheet known"), 0, 0);
        }

        if (lines.CharterCapital(quarter) is not { } capital || capital <= 0m)
        {
            return (RatioValue.Missing("the balance sheet states no charter capital"), 0, 0);
        }

        var balanceEnd = QuarterEnd(quarter);
        var window = adjustments.Where(adjustment => adjustment.ExDate > balanceEnd && adjustment.ExDate <= session).ToList();
        var known = window.Where(adjustment => adjustment.AnnouncedOn is { } announced && announced <= session).ToList();

        var shares = known.Aggregate(capital / ParValue, (count, adjustment) => count * adjustment.Factor.Shares);
        return (RatioValue.Known(decimal.Round(shares, 0)), known.Count(adjustment => adjustment.Factor.Shares != 1m), window.Count - known.Count);
    }

    private static RatioValue ReturnOnEquity(StatementLines lines, RatioValue profit, IReadOnlyList<FiscalQuarter> quarters)
    {
        if (profit.Value is not { } trailing || quarters.Count == 0)
        {
            return RatioValue.Missing(profit.Absent ?? "no trailing profit");
        }

        var latest = quarters[^1];

        if (lines.EquityToParent(latest) is not { } closing || lines.EquityToParent(latest.YearEarlier) is not { } opening)
        {
            return RatioValue.Missing("equity is not known at both ends of the trailing year");
        }

        var average = (closing + opening) / 2m;
        return average > 0m ? RatioValue.Known(trailing / average) : RatioValue.Missing("average equity is not positive");
    }

    private static RatioValue DividendYield(
        IReadOnlyList<CorporateAction> corporateActions,
        IReadOnlyList<PriceAdjustment> adjustments,
        DateOnly session,
        RatioValue close)
    {
        if (close.Value is not { } price || price <= 0m)
        {
            return RatioValue.Missing("no close on the session");
        }

        var from = session.AddDays(-365);
        var perShare = 0m;

        foreach (var dividend in corporateActions.Where(action =>
            action.Type == CorporateActionType.CashDividend
            && !action.IsCancelled
            && action.ExDate > from
            && action.ExDate <= session
            && action.AnnouncedOn is { } announced && announced <= session
            && action.CashAmount is > 0m))
        {
            // Stated on today's share basis: a bonus issue after the dividend
            // means today's share received less per share than the headline.
            var since = adjustments
                .Where(adjustment => adjustment.ExDate > dividend.ExDate && adjustment.ExDate <= session
                    && adjustment.AnnouncedOn is { } known && known <= session)
                .Aggregate(1m, (factor, adjustment) => factor * adjustment.Factor.Shares);

            perShare += dividend.CashAmount!.Value / since;
        }

        return RatioValue.Known(perShare / price);
    }

    private static RatioValue Divide(RatioValue numerator, RatioValue denominator, string numeratorMissing, string denominatorMissing)
    {
        if (numerator.Value is not { } top)
        {
            return RatioValue.Missing(numerator.Absent ?? numeratorMissing);
        }

        if (denominator.Value is not { } bottom || bottom == 0m)
        {
            return RatioValue.Missing(denominator.Absent ?? denominatorMissing);
        }

        return RatioValue.Known(top / bottom);
    }

    private static DateOnly QuarterEnd(FiscalQuarter quarter) =>
        new DateOnly(quarter.Year, quarter.Quarter * 3, 1).AddMonths(1).AddDays(-1);
}
