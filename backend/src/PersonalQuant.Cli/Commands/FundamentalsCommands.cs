using System.Globalization;
using PersonalQuant.Application.Fundamentals;
using PersonalQuant.Cli.CommandLine;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Cli.Commands;

/// <summary>
/// <c>pqt fundamentals import</c> — records disclosed financial reports from
/// one named provider (ADR-029).
/// </summary>
/// <remarks>
/// The provider is named, never defaulted, as for market data: an import that
/// quietly read another source would date its values by that source's record.
/// </remarks>
/// <param name="imports">The import service.</param>
/// <param name="output">Where the answer is written.</param>
internal sealed class FundamentalsCommands(Lazy<IFundamentalsImportService> imports, Output output)
{
    /// <summary>Runs one <c>fundamentals</c> verb.</summary>
    /// <param name="command">The parsed command line.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The process exit code.</returns>
    public Task<int> RunAsync(CommandArguments command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!command.Validate(["provider", "ticker"], out var problem))
        {
            output.Problem(problem);
            return Task.FromResult(ExitCode.Usage);
        }

        return (command.Verb, command.Operands) switch
        {
            ("import", []) => ImportAsync(command, cancellationToken),
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

    private int Usage()
    {
        output.Problem("pqt fundamentals import --provider <CODE> [--ticker FPT,VCB]");
        return ExitCode.Usage;
    }

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);
}
