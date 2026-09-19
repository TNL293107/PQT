using PersonalQuant.Application.Instruments;
using PersonalQuant.Cli.CommandLine;
using PersonalQuant.Domain.Exchanges;

namespace PersonalQuant.Cli.Commands;

/// <summary>
/// Corrects the instrument master where the import cannot.
/// </summary>
/// <remarks>
/// <c>transfer</c> exists because of BSR: it moved from UPCOM to HOSE and joined
/// VN30 there, the master still held it on UPCOM, and a VN30 backfill was
/// refused because the source serves HOSE and not UPCOM. The import will not
/// infer a move from a ticker; this is where somebody states one.
/// </remarks>
/// <param name="transfers">Moves an instrument between venues.</param>
/// <param name="instruments">Resolves a ticker to the security it names.</param>
/// <param name="output">Where results and refusals go.</param>
internal sealed class InstrumentCommands(
    Lazy<IInstrumentTransferService> transfers,
    Lazy<IInstrumentResolver> instruments,
    Output output)
{
    /// <summary>
    /// Dispatches an <c>instrument</c> verb.
    /// </summary>
    /// <param name="command">The parsed command line.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The process exit code.</returns>
    public Task<int> RunAsync(CommandArguments command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return command.Verb switch
        {
            "transfer" => TransferAsync(command, cancellationToken),
            _ => Task.FromResult(Unknown(command.Verb)),
        };
    }

    private async Task<int> TransferAsync(
        CommandArguments command,
        CancellationToken cancellationToken)
    {
        if (!command.Validate(["instrument", "to"], out var problem))
        {
            output.Problem(problem);
            return ExitCode.Usage;
        }

        if (!command.TryRequired("instrument", out var ticker, out var missing)
            || !command.TryRequired("to", out var venue, out missing))
        {
            output.Problem(missing);
            return ExitCode.Usage;
        }

        if (!ExchangeCode.TryCreate(venue, out var to))
        {
            output.Problem($"'{venue}' is not a venue code.");
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

        var transfer = await transfers.Value
            .TransferAsync(instrument.InstrumentId, to, cancellationToken)
            .ConfigureAwait(false);

        var name = instrument.Ticker.ToString();

        switch (transfer.Outcome)
        {
            case InstrumentTransferOutcome.Transferred:
                output.Line($"{name} moved from {transfer.From} to {transfer.To}. Its identity, "
                    + "bars and actions are unchanged; everything read through its venue is now "
                    + $"{transfer.To}'s, including for bars that printed before the move.");
                break;
            case InstrumentTransferOutcome.AlreadyThere:
                output.Line($"{name} already lists on {transfer.To}. Nothing changed.");
                break;
            case InstrumentTransferOutcome.UnknownExchange:
                output.Problem($"'{transfer.To}' is not a venue this system holds.");
                break;
            case InstrumentTransferOutcome.TickerTaken:
                output.Problem($"Another active instrument already trades as {name} on "
                    + $"{transfer.To}. Two records claim one listing; decide which is wrong "
                    + "before moving either.");
                break;
            case InstrumentTransferOutcome.Delisted:
                output.Problem($"{name} is delisted and can no longer move venue.");
                break;
            default:
                output.Problem($"No instrument has the identifier {instrument.InstrumentId}.");
                break;
        }

        return transfer.Succeeded ? ExitCode.Ok : ExitCode.Refused;
    }

    private int Unknown(string verb)
    {
        output.Problem($"'instrument {verb}' is not a command. Try transfer.");
        return ExitCode.Usage;
    }
}
