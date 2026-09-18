using System.Globalization;
using PersonalQuant.Application.CorporateActions;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Cli.CommandLine;

namespace PersonalQuant.Cli.Commands;

/// <summary>
/// Re-examines an instrument's adjustment factors on request.
/// </summary>
/// <remarks>
/// <para>
/// The verb the U3 reproduction found missing. The engine runs when an import
/// creates or amends an action, and only then — so everything that changes on
/// the price side after a factor is stored went unseen: a restated reference
/// close, the ex-date's bar arriving late, a price-limit breach the inspector
/// raised after the action was recorded. The only lever was to touch an action.
/// </para>
/// <para>
/// <c>recompute</c> always runs <see cref="RecomputeScope.Everything"/>. An
/// operator who asks for a recompute has already decided the cheap scope was
/// not enough; offering it here would be a flag whose only use is to do less
/// than was asked.
/// </para>
/// </remarks>
/// <param name="adjustments">The adjustment engine, constructed once the arguments hold.</param>
/// <param name="instruments">Resolves a ticker to the security it names.</param>
/// <param name="output">Where results and refusals go.</param>
internal sealed class AdjustCommands(
    Lazy<IPriceAdjustmentService> adjustments,
    Lazy<IInstrumentResolver> instruments,
    Output output)
{
    /// <summary>
    /// Dispatches an <c>adjust</c> verb.
    /// </summary>
    /// <param name="command">The parsed command line.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The process exit code.</returns>
    public Task<int> RunAsync(CommandArguments command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return command.Verb switch
        {
            "recompute" => RecomputeAsync(command, cancellationToken),
            _ => Task.FromResult(Unknown(command.Verb)),
        };
    }

    private async Task<int> RecomputeAsync(
        CommandArguments command,
        CancellationToken cancellationToken)
    {
        if (!command.Validate(["instrument"], out var problem))
        {
            output.Problem(problem);
            return ExitCode.Usage;
        }

        if (!command.TryRequired("instrument", out var ticker, out var missing))
        {
            output.Problem(missing);
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

        var run = await adjustments.Value
            .RecomputeAsync(instrument.InstrumentId, RecomputeScope.Everything, cancellationToken)
            .ConfigureAwait(false);

        if (run.ActionsConsidered == 0)
        {
            output.Line($"{instrument.Ticker}: no corporate actions are recorded, so nothing adjusts.");
            return ExitCode.Ok;
        }

        output.Line(
            $"{instrument.Ticker}: {Output.Plural(run.ActionsConsidered, "action")} — "
                + $"{run.Computed} computed, {run.Unchanged} unchanged, {run.Removed} removed, "
                + $"{run.Rejections.Count} rejected.");

        output.Line(
            $"{Output.Plural(run.IssuesExplained, "finding")} explained, "
                + $"{Output.Plural(run.IssuesRaised, "finding")} raised.");

        if (run.IssuesRaised > 0)
        {
            output.Line(
                $"An action claims a move the prices never made. Read it with "
                    + $"'pqt quality list --instrument {instrument.Ticker}'.");
        }

        if (run.Rejections.Count == 0)
        {
            return ExitCode.Ok;
        }

        output.Blank();
        output.Table(
            ["EX-DATE", "TYPE", "WHY NO FACTOR"],
            [.. run.Rejections.Select(rejection => (IReadOnlyList<string>)
            [
                rejection.ExDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                rejection.Type.ToString(),
                rejection.Detail,
            ])]);

        // Refused, not done: the series is still unadjusted for an event the
        // system knows about, and a script chaining this into an export must
        // not carry on as though it were.
        return ExitCode.Refused;
    }

    private int Unknown(string verb)
    {
        output.Problem($"'adjust {verb}' is not a command. Try recompute.");
        return ExitCode.Usage;
    }
}
