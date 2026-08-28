using Microsoft.Extensions.DependencyInjection;
using PersonalQuant.Application.Abstractions;
using PersonalQuant.Application.Classification;
using PersonalQuant.Application.Exchanges;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Infrastructure.Persistence.Seeding;

namespace PersonalQuant.IntegrationTests;

/// <summary>
/// Verifies that seeding fills an empty instrument master and then leaves it
/// alone.
/// </summary>
/// <remarks>
/// The seeder runs at every start-up in development and in Compose, so it will
/// meet an already-populated database far more often than an empty one.
/// Creating a duplicate on the second run would be a data-integrity failure,
/// not a cosmetic one.
/// </remarks>
/// <param name="containers">Shared container fixture.</param>
[Collection(DependencyContainerTests.Name)]
public sealed class ReferenceDataSeedingTests(DependencyContainerFixture containers)
{
    [Fact]
    public async Task Seeding_an_empty_database_creates_the_venues_and_securities()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync("seed_creates");

        // Act
        var outcome = await scope.Seeder.SeedAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(VietnamReferenceData.Exchanges.Count, outcome.ExchangesCreated);
        Assert.Equal(VietnamReferenceData.Sectors.Count, outcome.SectorsCreated);
        Assert.Equal(VietnamReferenceData.Industries.Count, outcome.IndustriesCreated);
        Assert.Equal(VietnamReferenceData.Instruments.Count, outcome.InstrumentsCreated);
    }

    [Fact]
    public async Task Seeding_twice_creates_nothing_the_second_time()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync("seed_idempotent");
        await scope.Seeder.SeedAsync(TestContext.Current.CancellationToken);

        // Act
        await using var second = await CreateScopeAsync("seed_idempotent");
        var outcome = await second.Seeder.SeedAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, outcome.ExchangesCreated);
        Assert.Equal(0, outcome.SectorsCreated);
        Assert.Equal(0, outcome.IndustriesCreated);
        Assert.Equal(0, outcome.InstrumentsCreated);
    }

    [Fact]
    public async Task A_seeded_security_is_findable_by_ticker_and_by_name()
    {
        // The point of seeding: a fresh database has something for the
        // terminal's search to return.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync("seed_searchable");
        await scope.Seeder.SeedAsync(TestContext.Current.CancellationToken);

        // Act
        var byTicker = await SearchAsync(scope, "FPT");
        var byName = await SearchAsync(scope, "vinhomes");

        // Assert
        Assert.Equal(InstrumentMatchKind.ExactTicker, byTicker[0].MatchKind);
        Assert.Equal("FPT", byTicker[0].Ticker.Value);
        Assert.Equal("HOSE", byTicker[0].ExchangeCode.Value);
        Assert.Equal("VHM", byName[0].Ticker.Value);
    }

    [Fact]
    public async Task Seeded_securities_are_recorded_as_trading_without_a_listing_date()
    {
        // Their real listing dates are public but unsourced here, and an
        // unsourced date in the system of record is exactly the kind of quiet
        // error the instrument master exists to prevent.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync("seed_listing_state");
        await scope.Seeder.SeedAsync(TestContext.Current.CancellationToken);

        var hose = await scope.Exchanges.FindByCodeAsync(
            ExchangeCode.Create("HOSE"), TestContext.Current.CancellationToken);

        // Act
        var fpt = await scope.Instruments.FindActiveByTickerAsync(
            hose!.Id, Ticker.Create("FPT"), TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(fpt);
        Assert.Equal(InstrumentStatus.Listed, fpt.Status);
        Assert.Null(fpt.ListedOn);
        Assert.True(fpt.IsActive);
    }

    [Fact]
    public async Task Seeding_leaves_a_record_that_has_since_been_corrected_alone()
    {
        // The seeder fills an empty database; it does not assert authority
        // over a populated one. A name corrected by hand must survive the next
        // start-up.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync("seed_preserves_edits");
        await scope.Seeder.SeedAsync(TestContext.Current.CancellationToken);

        var hose = await scope.Exchanges.FindByCodeAsync(
            ExchangeCode.Create("HOSE"), TestContext.Current.CancellationToken);
        var vnm = await scope.Instruments.FindActiveByTickerAsync(
            hose!.Id, Ticker.Create("VNM"), TestContext.Current.CancellationToken);

        // Derived from the row the seeder just wrote, not from a fixed date.
        // The seeder stamps creation with the real clock, so a hardcoded
        // instant is only valid until the calendar passes it — this test
        // failed for exactly that reason once the date it was written on went
        // by. An edit one second after creation is correct whenever it runs.
        vnm!.Rename("Vinamilk", vnm.CreatedAtUtc.AddSeconds(1));
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        await using var second = await CreateScopeAsync("seed_preserves_edits");
        await second.Seeder.SeedAsync(TestContext.Current.CancellationToken);

        // Assert
        await using var reader = await CreateScopeAsync("seed_preserves_edits");
        var reloaded = await reader.Instruments.FindByIdAsync(
            vnm.Id, TestContext.Current.CancellationToken);

        Assert.Equal("Vinamilk", reloaded?.Name);
    }

    private static async Task<IReadOnlyList<InstrumentSearchResult>> SearchAsync(
        SeedScope scope,
        string query)
    {
        Assert.True(
            InstrumentSearchCriteria.TryCreate(
                query, limit: null, includeInactive: false, out var criteria, out var problem),
            problem);

        return await scope.Search.SearchAsync(criteria, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Opens a scope onto a database of this test's own.
    /// </summary>
    /// <remarks>
    /// Two levels of isolation, both necessary. Seeding creates HOSE, HNX and
    /// UPCOM by name and the exchange code is unique, so these tests cannot
    /// share the collection's database with classes that name their own
    /// venues. And each test here asserts on what a seeding run created, which
    /// only means anything against a database no earlier test has seeded — so
    /// they do not share one with each other either.
    /// </remarks>
    /// <param name="database">
    /// A name unique to the calling test. Repeated calls with the same name
    /// reuse the database, which is how a test observes a second seeding run.
    /// </param>
    private async Task<SeedScope> CreateScopeAsync(string database)
    {
        var factory = PersonalQuantApiFactory.WithDependencies(
            await containers.CreateDatabaseAsync(database),
            containers.Redis,
            applyMigrations: true);

        using var client = factory.CreateClient();
        _ = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        return new SeedScope(factory);
    }

    /// <summary>Owns a host, a DI scope, and a seeder built from it.</summary>
    private sealed class SeedScope : IAsyncDisposable
    {
        private readonly PersonalQuantApiFactory _factory;
        private readonly AsyncServiceScope _scope;

        public SeedScope(PersonalQuantApiFactory factory)
        {
            _factory = factory;
            _scope = factory.Services.CreateAsyncScope();

            UnitOfWork = _scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            Instruments = _scope.ServiceProvider.GetRequiredService<IInstrumentRepository>();
            Exchanges = _scope.ServiceProvider.GetRequiredService<IExchangeRepository>();
            Search = _scope.ServiceProvider.GetRequiredService<IInstrumentSearchService>();

            Classification = _scope.ServiceProvider.GetRequiredService<IClassificationRepository>();

            Seeder = new ReferenceDataSeeder(
                Exchanges,
                Classification,
                Instruments,
                UnitOfWork,
                _scope.ServiceProvider.GetRequiredService<IClock>());
        }

        public IUnitOfWork UnitOfWork { get; }

        public IClassificationRepository Classification { get; }

        public IInstrumentRepository Instruments { get; }

        public IExchangeRepository Exchanges { get; }

        public IInstrumentSearchService Search { get; }

        public ReferenceDataSeeder Seeder { get; }

        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync();
            await _factory.DisposeAsync();
        }
    }
}
