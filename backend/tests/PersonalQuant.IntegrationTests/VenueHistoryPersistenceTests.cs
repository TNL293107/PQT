using Microsoft.Extensions.DependencyInjection;
using PersonalQuant.Application.Abstractions;
using PersonalQuant.Application.Exchanges;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Domain.Currencies;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.IntegrationTests;

/// <summary>
/// Verifies venue history against real PostgreSQL: that a move reads back as
/// two spells meeting at a session, and that the schema refuses one instrument
/// on two venues at once.
/// </summary>
/// <param name="containers">Shared container fixture.</param>
[Collection(DependencyContainerTests.Name)]
public sealed class VenueHistoryPersistenceTests(DependencyContainerFixture containers)
{
    private static readonly DateTimeOffset RecordedAt = new(2026, 10, 13, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Moved = new(2025, 1, 17);

    [Fact]
    public async Task A_move_reads_back_as_two_spells_that_meet_at_the_first_new_session()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var (instrument, before, after) = await ArrangeAsync(scope, "VHA");

        scope.History.Add(InstrumentVenueSpell.Record(instrument, before, null, Moved, RecordedAt));
        scope.History.Add(InstrumentVenueSpell.Record(instrument, after, Moved, null, RecordedAt));
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var reader = await CreateScopeAsync();
        var spells = await reader.History.ListAsync([instrument], TestContext.Current.CancellationToken);
        var timeline = new VenueTimeline(after, spells);

        Assert.Equal(2, spells.Count);
        Assert.Equal(before, timeline.On(Moved.AddDays(-1)));
        Assert.Equal(after, timeline.On(Moved));
    }

    [Fact]
    public async Task One_instrument_cannot_be_on_two_venues_on_the_same_session()
    {
        // Both spells claim the instrument is on its venue today. The primary
        // key cannot see it; the exclusion constraint does.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var (instrument, before, after) = await ArrangeAsync(scope, "VHB");

        scope.History.Add(InstrumentVenueSpell.Record(instrument, before, null, null, RecordedAt));
        scope.History.Add(InstrumentVenueSpell.Record(instrument, after, Moved, null, RecordedAt));

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken));

        Assert.Contains("ex_instrument_venue_spells_no_overlap", error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Two venues and an instrument on the second, the one it moved to.</summary>
    private static async Task<(InstrumentId Instrument, ExchangeId Before, ExchangeId After)> ArrangeAsync(
        HistoryScope scope,
        string tag)
    {
        var suffix = Guid.NewGuid().ToString("N")[..5].ToUpperInvariant();
        var before = Exchange.Register(ExchangeCode.Create($"U{suffix}"), "Old venue", "Asia/Ho_Chi_Minh", RecordedAt);
        var after = Exchange.Register(ExchangeCode.Create($"H{suffix}"), "New venue", "Asia/Ho_Chi_Minh", RecordedAt);
        scope.Exchanges.Add(before);
        scope.Exchanges.Add(after);
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        var instrument = Instrument.Register(
            after.Id, Ticker.Create(tag), $"{tag} Test Company", AssetType.Equity, CurrencyCode.Vnd, RecordedAt);
        instrument.List(RecordedAt);
        scope.Instruments.Add(instrument);
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        return (instrument.Id, before.Id, after.Id);
    }

    private async Task<HistoryScope> CreateScopeAsync()
    {
        var factory = PersonalQuantApiFactory.WithDependencies(
            containers.Postgres,
            containers.Redis,
            applyMigrations: true);

        using var client = factory.CreateClient();
        _ = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        return new HistoryScope(factory);
    }

    private sealed class HistoryScope : IAsyncDisposable
    {
        private readonly PersonalQuantApiFactory _factory;
        private readonly AsyncServiceScope _scope;

        public HistoryScope(PersonalQuantApiFactory factory)
        {
            _factory = factory;
            _scope = factory.Services.CreateAsyncScope();

            UnitOfWork = _scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            Exchanges = _scope.ServiceProvider.GetRequiredService<IExchangeRepository>();
            Instruments = _scope.ServiceProvider.GetRequiredService<IInstrumentRepository>();
            History = _scope.ServiceProvider.GetRequiredService<IVenueHistoryRepository>();
        }

        public IUnitOfWork UnitOfWork { get; }

        public IExchangeRepository Exchanges { get; }

        public IInstrumentRepository Instruments { get; }

        public IVenueHistoryRepository History { get; }

        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync();
            await _factory.DisposeAsync();
        }
    }
}
