using PersonalQuant.Application.Terminal;
using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.Terminal;
using PersonalQuant.UnitTests.MarketData.Fakes;

namespace PersonalQuant.UnitTests.Terminal;

public sealed class WorkspaceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Lines_are_kept_in_order_trimmed_and_without_blanks()
    {
        var workspace = Workspace.Create("morning", ["  breadth VN30 ", "", "FPT GP --line"], Now);

        Assert.Equal(["breadth VN30", "FPT GP --line"], workspace.Entries);
    }

    [Fact]
    public void A_workspace_with_nothing_to_replay_is_refused()
    {
        Assert.Throws<DomainValidationException>(() => Workspace.Create("empty", ["  ", ""], Now));
    }

    [Fact]
    public void More_lines_than_the_bound_are_refused()
    {
        var lines = Enumerable.Range(0, Workspace.MaxEntries + 1).Select(index => $"L{index} DES").ToArray();

        Assert.Throws<DomainValidationException>(() => Workspace.Create("big", lines, Now));
    }

    [Theory]
    [InlineData("FPT GP\u001b[2J")]
    [InlineData("FPT\nGP")]
    public void A_line_with_control_characters_is_refused_and_not_echoed_raw(string line)
    {
        var refusal = Assert.Throws<DomainValidationException>(() => Workspace.Create("bad", [line], Now));

        Assert.DoesNotContain(refusal.Message, character => char.IsControl(character));
    }

    [Fact]
    public async Task Saving_under_an_existing_name_replaces_its_lines()
    {
        var store = new InMemoryWorkspaces();
        var unitOfWork = new FakeUnitOfWork();
        var service = new WorkspaceService(store, unitOfWork, new FakeClock(Now));
        var token = TestContext.Current.CancellationToken;

        var first = await service.SaveAsync("morning", ["breadth VN30"], token);
        var second = await service.SaveAsync("morning", ["FPT GP", "VCB GP"], token);

        Assert.Equal(WorkspaceOutcome.Created, first.Outcome);
        Assert.Equal(WorkspaceOutcome.Replaced, second.Outcome);
        Assert.Equal(["FPT GP", "VCB GP"], (await service.FindAsync("morning", token))!.Entries);
        Assert.Single(await service.ListAsync(token));
    }

    [Fact]
    public async Task An_invalid_save_and_a_missing_delete_say_why()
    {
        var service = new WorkspaceService(new InMemoryWorkspaces(), new FakeUnitOfWork(), new FakeClock(Now));
        var token = TestContext.Current.CancellationToken;

        var badName = await service.SaveAsync("Morning", ["FPT GP"], token);
        var missing = await service.DeleteAsync("ghost", token);

        Assert.Equal(WorkspaceOutcome.Invalid, badName.Outcome);
        Assert.Contains("not a workspace name", badName.Message, StringComparison.Ordinal);
        Assert.Equal(WorkspaceOutcome.NotFound, missing.Outcome);
    }

    private sealed class InMemoryWorkspaces : IWorkspaceRepository
    {
        private readonly List<Workspace> _held = [];

        public Task<IReadOnlyList<Workspace>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Workspace>>([.. _held]);

        public Task<Workspace?> FindAsync(string name, CancellationToken cancellationToken = default) =>
            Task.FromResult(_held.FirstOrDefault(workspace => workspace.Name == name));

        public Task<int> CountAsync(CancellationToken cancellationToken = default) => Task.FromResult(_held.Count);

        public void Add(Workspace workspace) => _held.Add(workspace);

        public void Remove(Workspace workspace) => _held.Remove(workspace);
    }
}
