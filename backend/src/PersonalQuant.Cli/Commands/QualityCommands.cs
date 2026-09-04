using System.Globalization;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Cli.CommandLine;
using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Cli.Commands;

/// <summary>
/// Lists what the quality rules found, and closes a finding once something
/// accounts for it.
/// </summary>
/// <remarks>
/// <para>
/// <c>resolve</c> is the half that was missing. A finding stays open until
/// something explains it, the consistency score decays while it does, and until
/// now the only caller able to close one was Phase 4 matching a price-limit
/// breach to a corporate action. Everything a person had to investigate — a
/// calendar that named a session the exchange had moved, for instance — stayed
/// open with no surface to close it on.
/// </para>
/// <para>
/// Explained and dismissed are kept apart deliberately, and the CLI will not
/// let a caller avoid choosing. Explained says the discontinuity was real and
/// something accounts for it; dismissed says there was nothing there. Read back
/// in five years those are opposite claims about the data.
/// </para>
/// </remarks>
/// <param name="quality">The findings service, constructed once the arguments hold.</param>
/// <param name="instruments">Resolves a ticker to the security it names.</param>
/// <param name="output">Where results and refusals go.</param>
internal sealed class QualityCommands(
    Lazy<IDataQualityService> quality,
    Lazy<IInstrumentResolver> instruments,
    Output output)
{
    /// <summary>How many findings one listing shows unless asked otherwise.</summary>
    private const int DefaultLimit = 50;

    /// <summary>
    /// Dispatches a <c>quality</c> verb.
    /// </summary>
    /// <param name="command">The parsed command line.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The process exit code.</returns>
    public Task<int> RunAsync(CommandArguments command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return command.Verb switch
        {
            "list" => ListAsync(command, cancellationToken),
            "resolve" => ResolveAsync(command, cancellationToken),
            _ => Task.FromResult(Unknown(command.Verb)),
        };
    }

    private async Task<int> ListAsync(CommandArguments command, CancellationToken cancellationToken)
    {
        if (!command.Validate(["instrument", "interval", "limit", "status"], out var problem))
        {
            output.Problem(problem);
            return ExitCode.Usage;
        }

        if (!command.TryRequired("instrument", out var ticker, out var missing))
        {
            output.Problem(missing);
            return ExitCode.Usage;
        }

        if (!BarIntervalParser.TryParse(command.Value("interval"), out var interval))
        {
            output.Problem(
                $"The interval is not one this system records. Accepted: {BarIntervalParser.DescribeAccepted()}.");
            return ExitCode.Usage;
        }

        if (!command.TryCount("limit", DefaultLimit, out var limit, out var badCount))
        {
            output.Problem(badCount);
            return ExitCode.Usage;
        }

        // Open is the only listing the service offers, and pretending to accept
        // --status closed would return the open ones under a heading that lies.
        if (command.Value("status") is { } status
            && !string.Equals(status, "open", StringComparison.OrdinalIgnoreCase))
        {
            output.Problem(
                $"'--status {status}' is not available. Only open findings can be listed; a "
                    + "closed one is read from the run it was resolved against.");

            return ExitCode.Usage;
        }

        var resolution = await instruments.Value
            .ResolveAsync(ticker, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (resolution.Instrument is not { } instrument)
        {
            output.Problem($"No instrument currently trades under '{resolution.Query}'.");
            return ExitCode.Refused;
        }

        var findings = await quality.Value
            .ListOpenIssuesAsync(instrument.InstrumentId, interval, limit, cancellationToken)
            .ConfigureAwait(false);

        if (findings.Count == 0)
        {
            output.Line(
                $"{instrument.Ticker} {BarIntervalParser.Describe(interval)}: no open findings.");

            return ExitCode.Ok;
        }

        output.Table(
            ["ID", "SESSION", "KIND", "DETECTED", "DETAIL"],
            [.. findings.Select(finding => (IReadOnlyList<string>)
            [
                finding.Id.Value.ToString(),
                finding.SessionAtUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                finding.Kind.ToString(),
                finding.DetectedAtUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                finding.Detail,
            ])]);

        output.Blank();
        output.Line(
            $"{Output.Plural(findings.Count, "open finding")}. Close one with "
                + "'pqt quality resolve <ID> --explained --reason \"...\"'.");

        return ExitCode.Ok;
    }

    private async Task<int> ResolveAsync(
        CommandArguments command,
        CancellationToken cancellationToken)
    {
        if (!command.Validate(["explained", "dismissed", "reason"], out var problem))
        {
            output.Problem(problem);
            return ExitCode.Usage;
        }

        if (command.Operands.Count != 1)
        {
            output.Problem("'quality resolve' needs exactly one finding identifier.");
            return ExitCode.Usage;
        }

        if (!Guid.TryParse(command.Operands[0], out var id))
        {
            output.Problem($"'{command.Operands[0]}' is not a finding identifier.");
            return ExitCode.Usage;
        }

        var explained = command.HasFlag("explained");
        var dismissed = command.HasFlag("dismissed");

        if (explained == dismissed)
        {
            output.Problem(
                "'quality resolve' needs exactly one of --explained and --dismissed. Explained "
                    + "says something accounts for the finding; dismissed says there was nothing "
                    + "there. They are opposite claims and the record keeps them apart.");

            return ExitCode.Usage;
        }

        if (!command.TryRequired("reason", out var reason, out var missing))
        {
            output.Problem(missing);
            return ExitCode.Usage;
        }

        var outcome = explained
            ? DataQualityResolution.Explained
            : DataQualityResolution.Dismissed;

        DataQualityIssue? finding;

        try
        {
            finding = await quality.Value
                .ResolveIssueAsync(new DataQualityIssueId(id), outcome, reason, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (DomainStateException exception)
        {
            // Closing a closed finding would erase the audit trail the finding
            // exists to leave. The aggregate refuses; the refusal is reported
            // rather than translated into something softer.
            output.Problem(exception.Message);
            return ExitCode.Refused;
        }

        if (finding is null)
        {
            output.Problem($"No finding exists with the identifier {id}.");
            return ExitCode.Refused;
        }

        output.Line(
            $"{finding.Kind} on "
                + $"{finding.SessionAtUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} "
                + $"is now {finding.Status.ToString().ToLowerInvariant()}: {finding.Resolution}");

        return ExitCode.Ok;
    }

    private int Unknown(string verb)
    {
        output.Problem($"'quality {verb}' is not a command. Try list or resolve.");
        return ExitCode.Usage;
    }
}
