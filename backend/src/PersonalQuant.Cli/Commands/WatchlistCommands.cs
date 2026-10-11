using System.Globalization;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Application.Terminal;
using PersonalQuant.Cli.CommandLine;

namespace PersonalQuant.Cli.Commands;

/// <summary>
/// <c>pqt watchlist</c> — the operator's watchlists, from the shell (ADR-026).
/// </summary>
/// <remarks>
/// <para>
/// The same application service the API's write endpoints call, so a list made
/// here is the list the console shows. The CLI needs no operator key: like
/// every operator command it writes with the deployment's database
/// credentials, which is a stronger claim to be the operator than a key is
/// (ADR-027).
/// </para>
/// <para>
/// Instruments are named by ticker here and stored by canonical identifier,
/// so a later ticker change leaves the list pointing at the same security.
/// </para>
/// </remarks>
/// <param name="watchlists">The watchlist service.</param>
/// <param name="instruments">Resolves a ticker to the security it names.</param>
/// <param name="output">Where the answer is written.</param>
internal sealed class WatchlistCommands(
    Lazy<IWatchlistService> watchlists,
    Lazy<IInstrumentResolver> instruments,
    Output output)
{
    /// <summary>Runs one <c>watchlist</c> verb.</summary>
    /// <param name="command">The parsed command line.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The process exit code.</returns>
    public Task<int> RunAsync(CommandArguments command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!command.Validate([], out var problem))
        {
            output.Problem(problem);
            return Task.FromResult(ExitCode.Usage);
        }

        return (command.Verb, command.Operands) switch
        {
            ("list", []) => ListAsync(cancellationToken),
            ("show", [var name]) => ShowAsync(name, cancellationToken),
            ("create", [var name]) => ReportAsync(watchlists.Value.CreateAsync(name, cancellationToken), "created"),
            ("rename", [var name, var newName]) =>
                ReportAsync(watchlists.Value.RenameAsync(name, newName, cancellationToken), "renamed"),
            ("delete", [var name]) => DeleteAsync(name, cancellationToken),
            ("add", [var name, _, ..]) =>
                EachAsync(name, [.. command.Operands.Skip(1)], add: true, cancellationToken),
            ("remove", [var name, _, ..]) =>
                EachAsync(name, [.. command.Operands.Skip(1)], add: false, cancellationToken),
            _ => Task.FromResult(Usage(command.Verb)),
        };
    }

    private async Task<int> ListAsync(CancellationToken cancellationToken)
    {
        var lists = await watchlists.Value.ListAsync(cancellationToken).ConfigureAwait(false);

        if (lists.Count == 0)
        {
            output.Line("No watchlists. Create one with 'pqt watchlist create <NAME>'.");
            return ExitCode.Ok;
        }

        output.Table(
            ["name", "instruments", "updated"],
            [
                .. lists.Select(list => (IReadOnlyList<string>)
                [
                    list.Name,
                    list.Count.ToString(CultureInfo.InvariantCulture),
                    Instant(list.UpdatedAtUtc),
                ]),
            ]);

        return ExitCode.Ok;
    }

    private async Task<int> ShowAsync(string name, CancellationToken cancellationToken)
    {
        var view = await watchlists.Value.FindAsync(name, cancellationToken).ConfigureAwait(false);

        if (view is null)
        {
            output.Problem($"There is no watchlist named '{name}'.");
            return ExitCode.Refused;
        }

        Print(view);

        return ExitCode.Ok;
    }

    private async Task<int> DeleteAsync(string name, CancellationToken cancellationToken)
    {
        var change = await watchlists.Value.DeleteAsync(name, cancellationToken).ConfigureAwait(false);

        if (!change.Succeeded)
        {
            output.Problem(change.Message ?? $"Watchlist '{name}' was not deleted.");
            return ExitCode.Refused;
        }

        output.Line($"Watchlist '{name}' deleted.");

        return ExitCode.Ok;
    }

    private async Task<int> ReportAsync(Task<WatchlistChange> pending, string verb)
    {
        var change = await pending.ConfigureAwait(false);

        if (change is not { Succeeded: true, Watchlist: { } view })
        {
            output.Problem(change.Message ?? "The watchlist was not changed.");
            return ExitCode.Refused;
        }

        output.Line($"Watchlist '{view.Name}' {verb}.");

        return ExitCode.Ok;
    }

    /// <summary>Adds or removes several tickers, each resolved and applied on its own.</summary>
    /// <remarks>
    /// Every ticker is resolved before anything is written, so a typo in the
    /// third ticker does not leave the first two applied and the operator
    /// wondering which half of the command took effect.
    /// </remarks>
    private async Task<int> EachAsync(
        string name,
        IReadOnlyList<string> tickers,
        bool add,
        CancellationToken cancellationToken)
    {
        var resolved = new List<InstrumentSearchResult>(tickers.Count);

        foreach (var ticker in tickers)
        {
            var resolution = await instruments.Value
                .ResolveAsync(ticker, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (resolution.Instrument is not { } instrument)
            {
                output.Problem(resolution.Candidates.Count > 1
                    ? $"'{resolution.Query}' trades on more than one venue; nothing was changed."
                    : $"No instrument currently trades under '{resolution.Query}'; nothing was changed.");
                return ExitCode.Refused;
            }

            resolved.Add(instrument);
        }

        WatchlistView? last = null;

        foreach (var instrument in resolved)
        {
            var change = add
                ? await watchlists.Value.AddAsync(name, instrument.InstrumentId, cancellationToken).ConfigureAwait(false)
                : await watchlists.Value.RemoveAsync(name, instrument.InstrumentId, cancellationToken).ConfigureAwait(false);

            if (!change.Succeeded)
            {
                output.Problem(change.Message ?? $"{instrument.Ticker} was not changed.");
                return ExitCode.Refused;
            }

            output.Line(change.Outcome == WatchlistOutcome.Unchanged
                ? $"{instrument.Ticker}: {(add ? "already on" : "not on")} '{name}'."
                : $"{instrument.Ticker}: {(add ? "added to" : "removed from")} '{name}'.");

            last = change.Watchlist;
        }

        if (last is not null)
        {
            output.Blank();
            Print(last);
        }

        return ExitCode.Ok;
    }

    private void Print(WatchlistView view)
    {
        output.Line($"{view.Name} — {Output.Plural(view.Items.Count, "instrument")}, updated {Instant(view.UpdatedAtUtc)}");

        if (view.Items.Count == 0)
        {
            return;
        }

        output.Blank();
        output.Table(
            ["ticker", "venue", "status", "name"],
            [
                .. view.Items.Select(item => (IReadOnlyList<string>)
                [
                    item.Ticker,
                    item.ExchangeCode,
                    item.Status.ToString(),
                    item.Name,
                ]),
            ]);
    }

    private int Usage(string verb)
    {
        output.Problem(verb switch
        {
            "show" or "create" or "delete" => $"pqt watchlist {verb} <NAME>",
            "rename" => "pqt watchlist rename <NAME> <NEW-NAME>",
            "add" or "remove" => $"pqt watchlist {verb} <NAME> <TICKER> [<TICKER>...]",
            "list" => "pqt watchlist list",
            _ => $"'watchlist {verb}' is not a command. Try list, show, create, rename, delete, add or remove.",
        });

        return ExitCode.Usage;
    }

    private static string Instant(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd HH:mm'Z'", CultureInfo.InvariantCulture);
}
