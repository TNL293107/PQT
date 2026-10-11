using PersonalQuant.Application.Terminal;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.Terminal;
using PersonalQuant.UnitTests.MarketData.Fakes;

namespace PersonalQuant.UnitTests.Terminal;

public sealed class WatchlistServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemoryWatchlists _store = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    [Fact]
    public async Task Creating_a_list_saves_it_and_answers_with_it()
    {
        var change = await Service().CreateAsync("banks", TestContext.Current.CancellationToken);

        Assert.Equal(WatchlistOutcome.Changed, change.Outcome);
        Assert.Equal("banks", change.Watchlist?.Name);
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Theory]
    [InlineData("Banks")]
    [InlineData("")]
    public async Task An_invalid_name_is_refused_without_a_write(string name)
    {
        var change = await Service().CreateAsync(name, TestContext.Current.CancellationToken);

        Assert.Equal(WatchlistOutcome.Invalid, change.Outcome);
        Assert.Contains("not a watchlist name", change.Message, StringComparison.Ordinal);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task A_taken_name_is_a_conflict_on_create_and_on_rename()
    {
        var service = Service();
        await service.CreateAsync("banks", TestContext.Current.CancellationToken);
        await service.CreateAsync("steel", TestContext.Current.CancellationToken);

        var created = await service.CreateAsync("banks", TestContext.Current.CancellationToken);
        var renamed = await service.RenameAsync("steel", "banks", TestContext.Current.CancellationToken);

        Assert.Equal(WatchlistOutcome.NameTaken, created.Outcome);
        Assert.Equal(WatchlistOutcome.NameTaken, renamed.Outcome);
    }

    [Fact]
    public async Task The_deployment_bound_on_lists_is_enforced()
    {
        var service = Service();

        for (var index = 0; index < WatchlistService.MaxWatchlists; index++)
        {
            await service.CreateAsync($"list-{index}", TestContext.Current.CancellationToken);
        }

        var change = await service.CreateAsync("one-more", TestContext.Current.CancellationToken);

        Assert.Equal(WatchlistOutcome.Invalid, change.Outcome);
    }

    [Fact]
    public async Task Adding_an_unknown_instrument_is_refused()
    {
        var service = Service();
        await service.CreateAsync("banks", TestContext.Current.CancellationToken);

        var change = await service.AddAsync("banks", InstrumentId.New(), TestContext.Current.CancellationToken);

        Assert.Equal(WatchlistOutcome.InstrumentNotFound, change.Outcome);
    }

    [Fact]
    public async Task Adding_twice_and_removing_what_is_absent_are_unchanged_and_not_saved()
    {
        var service = Service();
        var instrument = _store.KnownInstrument();
        await service.CreateAsync("banks", TestContext.Current.CancellationToken);
        await service.AddAsync("banks", instrument, TestContext.Current.CancellationToken);
        var saves = _unitOfWork.SaveCount;

        var again = await service.AddAsync("banks", instrument, TestContext.Current.CancellationToken);
        var absent = await service.RemoveAsync("banks", InstrumentId.New(), TestContext.Current.CancellationToken);

        Assert.Equal(WatchlistOutcome.Unchanged, again.Outcome);
        Assert.Equal(WatchlistOutcome.Unchanged, absent.Outcome);
        Assert.True(again.Succeeded);
        Assert.Equal(saves, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Every_change_to_a_missing_list_says_so()
    {
        var service = Service();
        var token = TestContext.Current.CancellationToken;

        Assert.Equal(WatchlistOutcome.NotFound, (await service.RenameAsync("ghost", "spirit", token)).Outcome);
        Assert.Equal(WatchlistOutcome.NotFound, (await service.DeleteAsync("ghost", token)).Outcome);
        Assert.Equal(WatchlistOutcome.NotFound, (await service.AddAsync("ghost", InstrumentId.New(), token)).Outcome);
        Assert.Equal(WatchlistOutcome.NotFound, (await service.RemoveAsync("ghost", InstrumentId.New(), token)).Outcome);
        Assert.Null(await service.FindAsync("Not A Name", token));
    }

    [Fact]
    public async Task A_name_taken_by_a_concurrent_create_is_a_conflict_not_a_failure()
    {
        // The existence check passed; the database's unique index refused the
        // commit because another request got there first.
        var service = new WatchlistService(_store, new RacingUnitOfWork(), new FakeClock(Now));

        var change = await service.CreateAsync("banks", TestContext.Current.CancellationToken);

        Assert.Equal(WatchlistOutcome.NameTaken, change.Outcome);
    }

    private sealed class RacingUnitOfWork : PersonalQuant.Application.Abstractions.IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new PersonalQuant.Application.Abstractions.UniqueConstraintException("lost the race");
    }

    private WatchlistService Service() => new(_store, _unitOfWork, new FakeClock(Now));

    /// <summary>Holds lists in memory; instruments exist when the test declared them.</summary>
    private sealed class InMemoryWatchlists : IWatchlistRepository
    {
        private readonly List<Watchlist> _lists = [];
        private readonly HashSet<InstrumentId> _instruments = [];

        public InstrumentId KnownInstrument()
        {
            var id = InstrumentId.New();
            _instruments.Add(id);

            return id;
        }

        public Task<IReadOnlyList<WatchlistSummary>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WatchlistSummary>>(
                [.. _lists.Select(list => new WatchlistSummary(list.Name, list.Entries.Count, list.CreatedAtUtc, list.UpdatedAtUtc))]);

        public Task<Watchlist?> FindForUpdateAsync(string name, CancellationToken cancellationToken = default) =>
            Task.FromResult(_lists.FirstOrDefault(list => list.Name == name));

        public Task<WatchlistView?> FindViewAsync(string name, CancellationToken cancellationToken = default) =>
            Task.FromResult(_lists.FirstOrDefault(list => list.Name == name) is { } list
                ? new WatchlistView(
                    list.Name,
                    list.CreatedAtUtc,
                    list.UpdatedAtUtc,
                    [.. list.Entries.Select(entry => new WatchlistItem(
                        entry.InstrumentId, "TKR", "Name", "HOSE", InstrumentStatus.Listed, entry.Position, entry.AddedAtUtc))])
                : null);

        public Task<bool> ExistsAsync(string name, CancellationToken cancellationToken = default) =>
            Task.FromResult(_lists.Exists(list => list.Name == name));

        public Task<bool> InstrumentExistsAsync(InstrumentId instrumentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_instruments.Contains(instrumentId));

        public void Add(Watchlist watchlist) => _lists.Add(watchlist);

        public void Remove(Watchlist watchlist) => _lists.Remove(watchlist);
    }
}
