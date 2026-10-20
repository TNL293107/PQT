using System.Globalization;
using PersonalQuant.Application.Fundamentals;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Cli.CommandLine;
using PersonalQuant.Domain.Fundamentals;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Cli.Commands;

/// <summary>
/// <c>pqt fundamentals</c> — records disclosed financial reports from one named
/// provider, and reads them as they were known at an instant (ADR-029).
/// </summary>
/// <remarks>
/// The provider is named, never defaulted, as for market data: an import that
/// quietly read another source would date its values by that source's record.
/// </remarks>
/// <param name="imports">The import service.</param>
/// <param name="reader">The point-in-time read.</param>
/// <param name="ratioService">The point-in-time ratios.</param>
/// <param name="instruments">Resolves a ticker.</param>
/// <param name="output">Where the answer is written.</param>
internal sealed class FundamentalsCommands(
    Lazy<IFundamentalsImportService> imports,
    Lazy<IFundamentalsReader> reader,
    Lazy<IFundamentalRatioService> ratioService,
    Lazy<IInstrumentResolver> instruments,
    Output output)
{
    /// <summary>Runs one <c>fundamentals</c> verb.</summary>
    /// <param name="command">The parsed command line.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The process exit code.</returns>
    public Task<int> RunAsync(CommandArguments command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!command.Validate(["provider", "ticker", "known-as-of", "scope", "statement", "code", "on"], out var problem))
        {
            output.Problem(problem);
            return Task.FromResult(ExitCode.Usage);
        }

        return (command.Verb, command.Operands) switch
        {
            ("import", []) => ImportAsync(command, cancellationToken),
            ("as-of", [var ticker]) => AsOfAsync(ticker, command, cancellationToken),
            ("ratios", [var ticker]) => RatiosAsync(ticker, command, cancellationToken),
            _ => Task.FromResult(Usage()),
        };
    }

    private async Task<int> ImportAsync(CommandArguments command, CancellationToken cancellationToken)
    {
        if (command.Value("provider") is not { } code || !SourceCode.TryCreate(code, out var provider))
        {
            output.Problem("'fundamentals import' needs --provider <CODE>; nothing is read by default.");
            return ExitCode.Usage;
        }

        var service = imports.Value;

        if (!service.Providers.Contains(provider))
        {
            output.Problem(
                $"No fundamentals provider is registered as '{provider.Value}'. Registered: "
                    + (service.Providers.Count == 0 ? "none" : string.Join(", ", service.Providers.Select(source => source.Value)))
                    + ".");
            return ExitCode.Refused;
        }

        var tickers = (command.Value("ticker") ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var report = await service.ImportAsync(provider, tickers, cancellationToken).ConfigureAwait(false);

        output.Field("provider", report.Provider.Value, 22);
        output.Field("filings read", Count(report.FilingsRead), 22);
        output.Field("reports recorded", Count(report.ReportsRecorded), 22);
        output.Field("without values", Count(report.ReportsWithoutValues), 22);
        output.Field("already held", Count(report.ReportsAlreadyHeld), 22);
        output.Field("lines recorded", Count(report.LinesRecorded), 22);
        output.Field("lines restated", Count(report.LinesRestated), 22);
        output.Field("late", Count(report.Late.Count), 22);
        output.Field("refused", Count(report.Rejections.Count), 22);
        output.Field(
            "values withheld",
            $"{Count(report.Withheld.Count(withheld => withheld.Reason == FilingRejectionReason.SharedForm))} sharing the consolidated form, "
                + $"{Count(report.Withheld.Count(withheld => withheld.Reason == FilingRejectionReason.RepeatedForm))} repeating an earlier period's",
            22);

        output.Field("lines dropped", Count(report.Dropped.Count), 22);

        // A shared form is the rule for every parent company, so only the
        // repeated ones, which are each an issuer's error, are listed.
        var repeated = report.Withheld.Where(withheld => withheld.Reason == FilingRejectionReason.RepeatedForm).ToList();

        if (repeated.Count > 0)
        {
            output.Blank();
            output.Table(
                ["ticker", "filing", "withheld"],
                [.. repeated.Select(withheld => (IReadOnlyList<string>)[withheld.Ticker, withheld.Reference, withheld.Detail])]);
        }

        if (report.Dropped.Count > 0)
        {
            output.Blank();
            output.Table(
                ["ticker", "filing", "dropped"],
                [.. report.Dropped.Select(dropped => (IReadOnlyList<string>)[dropped.Ticker, dropped.Reference, dropped.Detail])]);
        }

        if (report.Late.Count > 0)
        {
            output.Blank();
            output.Table(
                ["ticker", "filing", "days", "deadline"],
                [
                    .. report.Late.Select(late => (IReadOnlyList<string>)
                    [
                        late.Ticker,
                        late.Reference,
                        Count(late.DaysAfterPeriod),
                        Count(late.DeadlineDays),
                    ]),
                ]);
        }

        if (report.Rejections.Count > 0)
        {
            output.Blank();
            output.Table(
                ["ticker", "filing", "reason", "detail"],
                [
                    .. report.Rejections.Select(rejection => (IReadOnlyList<string>)
                    [
                        rejection.Ticker,
                        rejection.Reference,
                        rejection.Reason.ToString(),
                        rejection.Detail,
                    ]),
                ]);
        }

        return ExitCode.Ok;
    }

    /// <summary>
    /// Prints one statement of an issuer as it was known at the start of a
    /// day: 00:00 UTC, before that day's session opened, the cut
    /// <c>dataset export --known-as-of</c> uses.
    /// </summary>
    private async Task<int> AsOfAsync(string ticker, CommandArguments command, CancellationToken cancellationToken)
    {
        if (!command.TryDate("known-as-of", out var day, out var badDate))
        {
            output.Problem(badDate);
            return ExitCode.Usage;
        }

        if (day is not { } knownAsOf)
        {
            output.Problem("'fundamentals as-of' needs --known-as-of yyyy-MM-dd.");
            return ExitCode.Usage;
        }

        FinancialStatementScope? scope = (command.Value("scope") ?? "consolidated").ToLowerInvariant() switch
        {
            "consolidated" => FinancialStatementScope.Consolidated,
            "separate" => FinancialStatementScope.Separate,
            _ => null,
        };

        FinancialStatement? statement = (command.Value("statement") ?? "income").ToLowerInvariant() switch
        {
            "balance" => FinancialStatement.BalanceSheet,
            "income" => FinancialStatement.IncomeStatement,
            "cash-direct" => FinancialStatement.CashFlowDirect,
            "cash-indirect" => FinancialStatement.CashFlowIndirect,
            _ => null,
        };

        if (scope is null || statement is null)
        {
            output.Problem("--scope is consolidated or separate; --statement is balance, income, cash-direct or cash-indirect.");
            return ExitCode.Usage;
        }

        string? code;

        try
        {
            code = command.Value("code") is { } written ? StatementLine.NormaliseLineCode(written) : null;
        }
        catch (Domain.Common.DomainValidationException error)
        {
            output.Problem(error.Message);
            return ExitCode.Usage;
        }

        var resolution = await instruments.Value.ResolveAsync(ticker, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (resolution.Outcome != InstrumentResolutionOutcome.Resolved || resolution.Instrument is not { } instrument)
        {
            output.Problem($"'{ticker}' does not name one active instrument.");
            return ExitCode.Refused;
        }

        var instant = new DateTimeOffset(knownAsOf.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var read = await reader.Value.ReadAsync(instrument.InstrumentId, scope.Value, instant, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var values = read.Values
            .Where(value => value.Statement == statement && (code is null || value.LineCode == code))
            .ToList();

        output.Field("known as of", instant.ToString("yyyy-MM-dd HH:mm'Z'", CultureInfo.InvariantCulture), 22);
        output.Field("reports known", Count(read.ReportsKnown), 22);
        output.Field("reports withheld", Count(read.ReportsWithheld), 22);
        output.Field("without values", Count(read.DisclosedWithoutValues.Count), 22);
        output.Field("values (as filed)", $"{Count(read.AsFiled)} ({Count(read.Vendor)} vendor)", 22);

        if (values.Count == 0)
        {
            output.Line("No values of that statement were known then.");
            return ExitCode.Ok;
        }

        output.Blank();
        output.Table(
            ["code", "period", "span", "value", "stated by", "known from", "restated"],
            [
                .. values.Select(value => (IReadOnlyList<string>)
                [
                    value.LineCode,
                    value.Period.ToString(),
                    value.Span.ToString(),
                    value.Value.ToString("#,##0.####", CultureInfo.InvariantCulture),
                    value.ReportKind.ToString(),
                    value.KnownFromUtc.ToString("yyyy-MM-dd HH:mm'Z'", CultureInfo.InvariantCulture),
                    value.Restated ? "yes" : string.Empty,
                ]),
            ]);

        return ExitCode.Ok;
    }

    /// <summary>Prints an issuer's point-in-time ratios for a session (ADR-030).</summary>
    private async Task<int> RatiosAsync(string ticker, CommandArguments command, CancellationToken cancellationToken)
    {
        if (!command.TryDate("on", out var day, out var badDate))
        {
            output.Problem(badDate);
            return ExitCode.Usage;
        }

        if (day is not { } session)
        {
            output.Problem("'fundamentals ratios' needs --on yyyy-MM-dd, the session whose close prices them.");
            return ExitCode.Usage;
        }

        var resolution = await instruments.Value.ResolveAsync(ticker, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (resolution.Outcome != InstrumentResolutionOutcome.Resolved || resolution.Instrument is not { } instrument)
        {
            output.Problem($"'{ticker}' does not name one active instrument.");
            return ExitCode.Refused;
        }

        var ratios = await ratioService.Value.ReadAsync(instrument.InstrumentId, session, cancellationToken).ConfigureAwait(false);

        output.Field("session", session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), 22);
        output.Field("statements", ratios.Scope?.ToString() ?? "none known", 22);
        output.Field("balance sheet", ratios.BalanceSheet?.ToString() ?? "none known", 22);
        output.Field("profit quarters", ratios.ProfitQuarters.Count == 0 ? "none" : string.Join(" ", ratios.ProfitQuarters), 22);
        output.Field("actions", $"{Count(ratios.ActionsApplied)} applied, {Count(ratios.ActionsWithheld)} withheld", 22);
        output.Blank();
        output.Table(
            ["ratio", "value", "absent because"],
            [
                Row("close", ratios.Close, "#,##0.##"),
                Row("shares", ratios.Shares, "#,##0"),
                Row("net profit (TTM)", ratios.NetProfitTtm, "#,##0"),
                Row("EPS (TTM)", ratios.EpsTtm, "#,##0.##"),
                Row("book value / share", ratios.BookValuePerShare, "#,##0.##"),
                Row("P/E", ratios.PriceToEarnings, "0.00"),
                Row("P/B", ratios.PriceToBook, "0.00"),
                Row("ROE (TTM)", ratios.ReturnOnEquity, "0.00%"),
                Row("dividend yield (TTM)", ratios.DividendYield, "0.00%"),
            ]);

        return ExitCode.Ok;

        static IReadOnlyList<string> Row(string name, RatioValue ratio, string format) =>
            [name, ratio.Value?.ToString(format, CultureInfo.InvariantCulture) ?? "—", ratio.Absent ?? string.Empty];
    }

    private int Usage()
    {
        output.Problem(
            "pqt fundamentals import --provider <CODE> [--ticker FPT,VCB]\n"
                + "pqt fundamentals as-of <TICKER> --known-as-of yyyy-MM-dd [--scope consolidated|separate]\n"
                + "                     [--statement balance|income|cash-direct|cash-indirect] [--code 10]\n"
                + "pqt fundamentals ratios <TICKER> --on yyyy-MM-dd");
        return ExitCode.Usage;
    }

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);
}
