using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PersonalQuant.Application.Abstractions;
using PersonalQuant.Application.Exchanges;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Domain.Currencies;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Infrastructure.Persistence;

namespace PersonalQuant.IntegrationTests;

/// <summary>
/// Verifies the instrument master against a real PostgreSQL instance.
/// </summary>
/// <remarks>
/// The rules under test — value-object round-tripping, the partial unique
/// index, and the restriction on deleting referenced master data — are all
/// enforced by the database, so an in-memory provider would prove nothing.
/// </remarks>
/// <param name="containers">Shared container fixture.</param>
[Collection(DependencyContainerTests.Name)]
public sealed class InstrumentMasterPersistenceTests(DependencyContainerFixture containers)
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 14, 9, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly ListingDate = new(2026, 8, 20);

    [Fact]
    public async Task An_instrument_round_trips_through_postgres()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var exchange = await AddExchangeAsync(scope, "HOSE");

        var instrument = Instrument.Register(
            exchange.Id,
            Ticker.Create("FPT"),
            "A Listed Company",
            AssetType.Equity,
            CurrencyCode.Vnd,
            Now);
        instrument.List(ListingDate, Now.AddDays(1));

        scope.Instruments.Add(instrument);
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Read through a fresh scope so the answer cannot come from the
        // change tracker.
        await using var reader = await CreateScopeAsync();
        var loaded = await reader.Instruments.FindByIdAsync(
            instrument.Id, TestContext.Current.CancellationToken);

        Assert.NotNull(loaded);
        Assert.Equal(instrument.Id, loaded.Id);
        Assert.Equal(Ticker.Create("FPT"), loaded.Ticker);
        Assert.Equal(CurrencyCode.Vnd, loaded.Currency);
        Assert.Equal(AssetType.Equity, loaded.AssetType);
        Assert.Equal(InstrumentStatus.Listed, loaded.Status);
        Assert.Equal(ListingDate, loaded.ListedOn);
        Assert.Equal(Now, loaded.CreatedAtUtc);
    }

    [Fact]
    public async Task Two_active_instruments_cannot_share_a_ticker_on_one_exchange()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var exchange = await AddExchangeAsync(scope, "HNX");

        scope.Instruments.Add(Register(exchange.Id, "AAA"));
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        scope.Instruments.Add(Register(exchange.Id, "AAA"));

        // The database, not the application, is the last line of defence
        // against a duplicate arriving through a concurrent import.
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() =>
            scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken));

        var postgres = Assert.IsType<PostgresException>(failure.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
    }

    [Fact]
    public async Task A_ticker_may_be_reissued_after_the_previous_holder_delists()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        // The reason uniqueness is filtered rather than absolute. Vietnamese
        // tickers are released on delisting and can be reassigned.
        await using var scope = await CreateScopeAsync();
        var exchange = await AddExchangeAsync(scope, "UPCOM");

        var original = Register(exchange.Id, "BBB");
        original.List(ListingDate, Now.AddDays(1));
        scope.Instruments.Add(original);
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        original.Delist(new DateOnly(2026, 9, 1), Now.AddDays(2));
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        var successor = Register(exchange.Id, "BBB");
        scope.Instruments.Add(successor);
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.NotEqual(original.Id, successor.Id);
    }

    [Fact]
    public async Task Only_the_current_holder_of_a_ticker_is_returned()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var exchange = await AddExchangeAsync(scope, "HOSE2");

        var original = Register(exchange.Id, "CCC");
        original.List(ListingDate, Now.AddDays(1));
        original.Delist(new DateOnly(2026, 9, 1), Now.AddDays(2));
        scope.Instruments.Add(original);

        var successor = Register(exchange.Id, "CCC");
        scope.Instruments.Add(successor);
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var reader = await CreateScopeAsync();
        var active = await reader.Instruments.FindActiveByTickerAsync(
            exchange.Id, Ticker.Create("CCC"), TestContext.Current.CancellationToken);
        // The previous holder is still discoverable, through the relation the
        // API exposes rather than a read nothing else uses. A chart drawn
        // before the reassignment is not about this security.
        var related = await reader.Instruments.ListRelatedAsync(
            successor.Id, TestContext.Current.CancellationToken);

        Assert.Equal(successor.Id, active?.Id);
        Assert.Equal(original.Id, Assert.Single(related).Instrument.InstrumentId);
    }

    [Fact]
    public async Task A_delisted_ticker_reports_as_available()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var exchange = await AddExchangeAsync(scope, "HNX2");

        var instrument = Register(exchange.Id, "DDD");
        instrument.List(ListingDate, Now.AddDays(1));
        scope.Instruments.Add(instrument);
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        var takenWhileListed = await scope.Instruments.IsTickerTakenAsync(
            exchange.Id, Ticker.Create("DDD"), TestContext.Current.CancellationToken);

        instrument.Delist(new DateOnly(2026, 9, 1), Now.AddDays(2));
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        var takenAfterDelisting = await scope.Instruments.IsTickerTakenAsync(
            exchange.Id, Ticker.Create("DDD"), TestContext.Current.CancellationToken);

        Assert.True(takenWhileListed);
        Assert.False(takenAfterDelisting);
    }

    [Fact]
    public async Task The_same_ticker_may_be_active_on_two_exchanges()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        // Uniqueness is scoped per venue, not globally.
        await using var scope = await CreateScopeAsync();
        var first = await AddExchangeAsync(scope, "VENA");
        var second = await AddExchangeAsync(scope, "VENB");

        scope.Instruments.Add(Register(first.Id, "EEE"));
        scope.Instruments.Add(Register(second.Id, "EEE"));

        var written = await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, written);
    }

    [Fact]
    public async Task An_exchange_referenced_by_an_instrument_cannot_be_deleted()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        // Master data is never removed, and the guarantee has to hold below
        // the ORM. The delete is issued as SQL rather than through the change
        // tracker, which would refuse it in memory and so prove nothing about
        // the database — an import script or a psql session would bypass that.
        await using var scope = await CreateScopeAsync();
        var exchange = await AddExchangeAsync(scope, "VENC");

        scope.Instruments.Add(Register(exchange.Id, "FFF"));
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        var failure = await Assert.ThrowsAsync<PostgresException>(() =>
            scope.DbContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM quant.exchanges WHERE id = {exchange.Id.Value}",
                TestContext.Current.CancellationToken));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, failure.SqlState);
    }

    [Fact]
    public async Task Deleting_an_instrument_is_not_reachable_through_the_repository()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        // Instrument master data is historical reference data that prices and
        // positions join against. Removal is not an operation the port offers,
        // and this test fails if one is ever added without a decision.
        await using var scope = await CreateScopeAsync();

        var removalOperations = typeof(IInstrumentRepository)
            .GetMethods()
            .Where(method =>
                method.Name.Contains("Remove", StringComparison.OrdinalIgnoreCase)
                || method.Name.Contains("Delete", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Empty(removalOperations);
        Assert.NotNull(scope.Instruments);
    }

    [Fact]
    public async Task Ticker_and_exchange_changes_preserve_the_stored_identity()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var origin = await AddExchangeAsync(scope, "VEND");
        var destination = await AddExchangeAsync(scope, "VENE");

        var instrument = Register(origin.Id, "GGG");
        instrument.List(ListingDate, Now.AddDays(1));
        scope.Instruments.Add(instrument);
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        var id = instrument.Id;
        instrument.ChangeTicker(Ticker.Create("HHH"), Now.AddDays(2));
        instrument.TransferToExchange(destination.Id, Now.AddDays(2));
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var reader = await CreateScopeAsync();
        var loaded = await reader.Instruments.FindByIdAsync(id, TestContext.Current.CancellationToken);

        Assert.NotNull(loaded);
        Assert.Equal(Ticker.Create("HHH"), loaded.Ticker);
        Assert.Equal(destination.Id, loaded.ExchangeId);
    }

    [Fact]
    public async Task An_exchange_code_is_unique()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        await AddExchangeAsync(scope, "VENF");

        scope.Exchanges.Add(Exchange.Register(
            ExchangeCode.Create("VENF"), "Duplicate", "Asia/Ho_Chi_Minh", Now));

        var failure = await Assert.ThrowsAsync<DbUpdateException>(() =>
            scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken));

        var postgres = Assert.IsType<PostgresException>(failure.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
    }

    private static Instrument Register(ExchangeId exchangeId, string ticker) =>
        Instrument.Register(
            exchangeId,
            Ticker.Create(ticker),
            "A Listed Company",
            AssetType.Equity,
            CurrencyCode.Vnd,
            Now);

    private async Task<ExchangeScope> CreateScopeAsync()
    {
        var factory = PersonalQuantApiFactory.WithDependencies(
            containers.Postgres,
            containers.Redis,
            applyMigrations: true);

        // Building a client forces host start-up, which runs the migration
        // hosted service.
        using var client = factory.CreateClient();
        _ = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        return new ExchangeScope(factory);
    }

    private static async Task<Exchange> AddExchangeAsync(ExchangeScope scope, string code)
    {
        var exchange = Exchange.Register(
            ExchangeCode.Create(code),
            $"Venue {code}",
            "Asia/Ho_Chi_Minh",
            Now);

        scope.Exchanges.Add(exchange);
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        return exchange;
    }

    /// <summary>
    /// Owns a host, a DI scope and the services resolved from it, so each test
    /// reads and writes through the real composition root rather than a
    /// hand-built context.
    /// </summary>
    private sealed class ExchangeScope : IAsyncDisposable
    {
        private readonly PersonalQuantApiFactory _factory;
        private readonly AsyncServiceScope _scope;

        public ExchangeScope(PersonalQuantApiFactory factory)
        {
            _factory = factory;
            _scope = factory.Services.CreateAsyncScope();

            DbContext = _scope.ServiceProvider.GetRequiredService<PersonalQuantDbContext>();
            UnitOfWork = _scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            Instruments = _scope.ServiceProvider.GetRequiredService<IInstrumentRepository>();
            Exchanges = _scope.ServiceProvider.GetRequiredService<IExchangeRepository>();
        }

        public PersonalQuantDbContext DbContext { get; }

        public IUnitOfWork UnitOfWork { get; }

        public IInstrumentRepository Instruments { get; }

        public IExchangeRepository Exchanges { get; }

        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync();
            await _factory.DisposeAsync();
        }
    }
}
