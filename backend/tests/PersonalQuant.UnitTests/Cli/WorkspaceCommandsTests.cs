using PersonalQuant.Application.Terminal;
using PersonalQuant.Cli.CommandLine;
using PersonalQuant.Cli.Commands;
using PersonalQuant.UnitTests.Cli.Fakes;

namespace PersonalQuant.UnitTests.Cli;

/// <summary>Verifies saving and reading console workspaces from the shell.</summary>
public sealed class WorkspaceCommandsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Each_operand_after_the_name_is_one_console_line()
    {
        var harness = new Harness();

        var code = await harness.RunAsync("workspace", "save", "morning", "breadth VN30", "FPT GP --line");

        Assert.Equal(ExitCode.Ok, code);
        Assert.Equal(["breadth VN30", "FPT GP --line"], harness.Workspaces.Saved);
        Assert.Contains("Workspace 'morning' created.", harness.Result, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("save", "morning")]
    [InlineData("show")]
    [InlineData("load", "morning")]
    public async Task A_command_line_the_group_cannot_use_is_a_usage_error(params string[] rest)
    {
        var harness = new Harness();

        Assert.Equal(ExitCode.Usage, await harness.RunAsync(["workspace", .. rest]));
        Assert.Null(harness.Workspaces.Saved);
    }

    [Fact]
    public async Task Showing_a_missing_workspace_exits_non_zero()
    {
        var harness = new Harness();

        Assert.Equal(ExitCode.Refused, await harness.RunAsync("workspace", "show", "ghost"));
        Assert.Contains("no workspace named 'ghost'", harness.Problems, StringComparison.Ordinal);
    }

    private sealed class Harness
    {
        private readonly WorkspaceCommands _commands;
        private readonly RecordedOutput _output = new();

        public Harness() =>
            _commands = new WorkspaceCommands(new Lazy<IWorkspaceService>(() => Workspaces), _output.Output);

        public FakeWorkspaceService Workspaces { get; } = new();

        public string Result => _output.Result;

        public string Problems => _output.Problems;

        public async Task<int> RunAsync(params string[] args)
        {
            Assert.True(CommandArguments.TryParse(args, out var command, out var problem), problem);

            return await _commands.RunAsync(command, TestContext.Current.CancellationToken);
        }
    }

    private sealed class FakeWorkspaceService : IWorkspaceService
    {
        public IReadOnlyList<string>? Saved { get; private set; }

        public Task<IReadOnlyList<WorkspaceView>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkspaceView>>([]);

        public Task<WorkspaceView?> FindAsync(string name, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkspaceView?>(null);

        public Task<WorkspaceChange> SaveAsync(
            string name,
            IReadOnlyList<string> entries,
            CancellationToken cancellationToken = default)
        {
            Saved = entries;

            return Task.FromResult(new WorkspaceChange(
                WorkspaceOutcome.Created, null, new WorkspaceView(name, entries, Now, Now)));
        }

        public Task<WorkspaceChange> DeleteAsync(string name, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WorkspaceChange(WorkspaceOutcome.NotFound, $"There is no workspace named '{name}'.", null));
    }
}
