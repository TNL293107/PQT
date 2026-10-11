using System.Globalization;
using PersonalQuant.Application.Terminal;
using PersonalQuant.Cli.CommandLine;

namespace PersonalQuant.Cli.Commands;

/// <summary>
/// <c>pqt workspace</c> — the console's saved workspaces, from the shell.
/// </summary>
/// <remarks>
/// A workspace is a list of console lines, and the CLI does not run them: it
/// is how an operator prepares or inspects one, and the console is where it
/// replays.
/// </remarks>
/// <param name="workspaces">The workspace service.</param>
/// <param name="output">Where the answer is written.</param>
internal sealed class WorkspaceCommands(Lazy<IWorkspaceService> workspaces, Output output)
{
    /// <summary>Runs one <c>workspace</c> verb.</summary>
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
            ("save", [var name, _, ..]) => SaveAsync(name, [.. command.Operands.Skip(1)], cancellationToken),
            ("delete", [var name]) => DeleteAsync(name, cancellationToken),
            _ => Task.FromResult(Usage(command.Verb)),
        };
    }

    private async Task<int> ListAsync(CancellationToken cancellationToken)
    {
        var held = await workspaces.Value.ListAsync(cancellationToken).ConfigureAwait(false);

        if (held.Count == 0)
        {
            output.Line("No workspaces. Save one with 'pqt workspace save <NAME> \"<line>\" ...', "
                + "or with 'ws save <NAME>' in the console.");
            return ExitCode.Ok;
        }

        output.Table(
            ["name", "lines", "updated"],
            [
                .. held.Select(workspace => (IReadOnlyList<string>)
                [
                    workspace.Name,
                    workspace.Entries.Count.ToString(CultureInfo.InvariantCulture),
                    workspace.UpdatedAtUtc.UtcDateTime.ToString("yyyy-MM-dd HH:mm'Z'", CultureInfo.InvariantCulture),
                ]),
            ]);

        return ExitCode.Ok;
    }

    private async Task<int> ShowAsync(string name, CancellationToken cancellationToken)
    {
        var view = await workspaces.Value.FindAsync(name, cancellationToken).ConfigureAwait(false);

        if (view is null)
        {
            output.Problem($"There is no workspace named '{name}'.");
            return ExitCode.Refused;
        }

        Print(view);

        return ExitCode.Ok;
    }

    private async Task<int> SaveAsync(string name, IReadOnlyList<string> lines, CancellationToken cancellationToken)
    {
        var change = await workspaces.Value.SaveAsync(name, lines, cancellationToken).ConfigureAwait(false);

        if (change is not { Succeeded: true, Workspace: { } view })
        {
            output.Problem(change.Message ?? "The workspace was not saved.");
            return ExitCode.Refused;
        }

        output.Line($"Workspace '{view.Name}' {(change.Outcome == WorkspaceOutcome.Created ? "created" : "replaced")}.");
        Print(view);

        return ExitCode.Ok;
    }

    private async Task<int> DeleteAsync(string name, CancellationToken cancellationToken)
    {
        var change = await workspaces.Value.DeleteAsync(name, cancellationToken).ConfigureAwait(false);

        if (!change.Succeeded)
        {
            output.Problem(change.Message ?? $"Workspace '{name}' was not deleted.");
            return ExitCode.Refused;
        }

        output.Line($"Workspace '{name}' deleted.");

        return ExitCode.Ok;
    }

    private void Print(WorkspaceView view)
    {
        for (var index = 0; index < view.Entries.Count; index++)
        {
            output.Line($"  {(index + 1).ToString(CultureInfo.InvariantCulture),2}  {view.Entries[index]}");
        }
    }

    private int Usage(string verb)
    {
        output.Problem(verb switch
        {
            "show" or "delete" => $"pqt workspace {verb} <NAME>",
            "save" => "pqt workspace save <NAME> \"<console line>\" [\"<console line>\"...]",
            "list" => "pqt workspace list",
            _ => $"'workspace {verb}' is not a command. Try list, show, save or delete.",
        });

        return ExitCode.Usage;
    }
}
