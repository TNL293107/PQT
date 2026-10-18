using PersonalQuant.Application.Fundamentals;

namespace PersonalQuant.Api.Contracts;

/// <summary>An issuer's statement values as known at an instant.</summary>
/// <param name="InstrumentId">The issuer's instrument.</param>
/// <param name="Scope">Consolidated or Separate.</param>
/// <param name="KnownAsOfUtc">The cut.</param>
/// <param name="ReportsKnown">Reports known by the cut.</param>
/// <param name="ReportsWithheld">Reports held now and filed after the cut.</param>
/// <param name="AsFiled">Values read from the issuer's own form.</param>
/// <param name="Vendor">Values read from an aggregator.</param>
/// <param name="Values">One value per template, statement, line, period and span.</param>
/// <param name="DisclosedWithoutValues">Reports known by the cut whose form was empty.</param>
public sealed record FundamentalsAsOfResponse(
    Guid InstrumentId,
    string Scope,
    DateTimeOffset KnownAsOfUtc,
    int ReportsKnown,
    int ReportsWithheld,
    int AsFiled,
    int Vendor,
    IReadOnlyList<FundamentalValueResponse> Values,
    IReadOnlyList<DisclosedReportResponse> DisclosedWithoutValues)
{
    /// <summary>Projects the read onto the wire contract.</summary>
    /// <param name="read">The read.</param>
    /// <returns>The response.</returns>
    public static FundamentalsAsOfResponse From(FundamentalsAsOf read)
    {
        ArgumentNullException.ThrowIfNull(read);

        return new FundamentalsAsOfResponse(
            read.InstrumentId.Value,
            read.Scope.ToString(),
            read.KnownAsOfUtc,
            read.ReportsKnown,
            read.ReportsWithheld,
            read.AsFiled,
            read.Vendor,
            [.. read.Values.Select(value => new FundamentalValueResponse(
                value.Template.ToString(),
                value.Statement.ToString(),
                value.LineCode,
                value.Period.ToString(),
                value.Span.ToString(),
                value.Value,
                value.Basis.ToString(),
                value.ReportKind.ToString(),
                value.KnownFromUtc,
                value.Restated))],
            [.. read.DisclosedWithoutValues.Select(report => new DisclosedReportResponse(
                report.Period.ToString(), report.Kind.ToString(), report.Template.ToString(), report.DisclosedOn))]);
    }
}

/// <summary>One statement value.</summary>
/// <param name="Template">The form the code belongs to.</param>
/// <param name="Statement">BalanceSheet, IncomeStatement, CashFlowDirect or CashFlowIndirect.</param>
/// <param name="LineCode">The form's line code.</param>
/// <param name="Period">The quarter, such as <c>2024Q2</c>.</param>
/// <param name="Span">Instant, Quarter or YearToDate.</param>
/// <param name="Value">The value.</param>
/// <param name="Basis">AsFiled or Vendor.</param>
/// <param name="ReportKind">The kind of report that stated it.</param>
/// <param name="KnownFromUtc">When that report became known.</param>
/// <param name="Restated">Whether an earlier known report stated a different value.</param>
public sealed record FundamentalValueResponse(
    string Template,
    string Statement,
    string LineCode,
    string Period,
    string Span,
    decimal Value,
    string Basis,
    string ReportKind,
    DateTimeOffset KnownFromUtc,
    bool Restated);

/// <summary>A report disclosed with an empty form.</summary>
/// <param name="Period">The quarter it closes on.</param>
/// <param name="Kind">Its kind.</param>
/// <param name="Template">Its form.</param>
/// <param name="DisclosedOn">Its disclosure date.</param>
public sealed record DisclosedReportResponse(string Period, string Kind, string Template, DateOnly DisclosedOn);

/// <summary>An issuer's point-in-time ratios for a session (ADR-030).</summary>
/// <param name="InstrumentId">The issuer's instrument.</param>
/// <param name="Session">The session.</param>
/// <param name="KnownAsOfUtc">The cut statements and actions were read at.</param>
/// <param name="Scope">The statements used, or null when none were known.</param>
/// <param name="BalanceSheet">The quarter of the balance sheet used.</param>
/// <param name="ProfitQuarters">The four quarters summed, oldest first.</param>
/// <param name="ActionsApplied">Share-count actions applied since the balance sheet.</param>
/// <param name="ActionsWithheld">Actions in the window not yet announced.</param>
/// <param name="Ratios">Each ratio, known or absent with a reason.</param>
public sealed record FundamentalRatiosResponse(
    Guid InstrumentId,
    DateOnly Session,
    DateTimeOffset KnownAsOfUtc,
    string? Scope,
    string? BalanceSheet,
    IReadOnlyList<string> ProfitQuarters,
    int ActionsApplied,
    int ActionsWithheld,
    IReadOnlyList<RatioResponse> Ratios)
{
    /// <summary>Projects the ratios onto the wire contract.</summary>
    /// <param name="ratios">The ratios.</param>
    /// <returns>The response.</returns>
    public static FundamentalRatiosResponse From(FundamentalRatios ratios)
    {
        ArgumentNullException.ThrowIfNull(ratios);

        return new FundamentalRatiosResponse(
            ratios.InstrumentId.Value,
            ratios.Session,
            ratios.KnownAsOfUtc,
            ratios.Scope?.ToString(),
            ratios.BalanceSheet?.ToString(),
            [.. ratios.ProfitQuarters.Select(quarter => quarter.ToString())],
            ratios.ActionsApplied,
            ratios.ActionsWithheld,
            [
                Ratio("close", ratios.Close),
                Ratio("shares", ratios.Shares),
                Ratio("netProfitTtm", ratios.NetProfitTtm),
                Ratio("epsTtm", ratios.EpsTtm),
                Ratio("bookValuePerShare", ratios.BookValuePerShare),
                Ratio("priceToEarnings", ratios.PriceToEarnings),
                Ratio("priceToBook", ratios.PriceToBook),
                Ratio("returnOnEquity", ratios.ReturnOnEquity),
                Ratio("dividendYield", ratios.DividendYield),
            ]);

        static RatioResponse Ratio(string name, RatioValue value) => new(name, value.Value, value.Absent);
    }
}

/// <summary>One ratio.</summary>
/// <param name="Name">Its name.</param>
/// <param name="Value">The value, or null.</param>
/// <param name="Absent">Why there is no value, or null.</param>
public sealed record RatioResponse(string Name, decimal? Value, string? Absent);
