using Microsoft.Extensions.DependencyInjection;
using PersonalQuant.Application.Abstractions;
using PersonalQuant.Application.Classification;
using PersonalQuant.Application.Exchanges;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Domain.Classification;
using PersonalQuant.Domain.Currencies;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.IntegrationTests;

/// <summary>
/// Verifies the classification taxonomy and the instrument detail read
/// against real PostgreSQL.
/// </summary>
/// <remarks>
/// The detail query left-joins two levels that are both optional. That is
/// exactly the shape a provider gets wrong — an inner join answers "no such
/// instrument" for every index — and it can only be proved against a database
/// that actually performs the join.
/// </remarks>
/// <param name="containers">Shared container fixture.</param>
[Collection(DependencyContainerTests.Name)]
public sealed class InstrumentClassificationTests(DependencyContainerFixture containers)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 26, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_classified_instrument_reports_both_taxonomy_levels()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var venue = await AddExchangeAsync(scope, "CLSA");
        var industryId = await AddIndustryAsync(scope, "CLSA-SEC", "CLSA Sector", "CLSA-IND", "CLSA Industry");

        var instrument = Instrument.Register(
            venue, Ticker.Create("CLA"), "Classified Company", AssetType.Equity, CurrencyCode.Vnd, Now);
        instrument.List(Now);
        instrument.AssignIndustry(industryId, Now);

        scope.Instruments.Add(instrument);
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        var detail = await scope.Catalog.FindDetailAsync(
            instrument.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(detail);
        Assert.NotNull(detail.Classification);
        Assert.Equal("CLSA-SEC", detail.Classification.SectorCode.Value);
        Assert.Equal("CLSA Sector", detail.Classification.SectorName);
        Assert.Equal("CLSA-IND", detail.Classification.IndustryCode.Value);
        Assert.Equal("CLSA Industry", detail.Classification.IndustryName);
        Assert.Equal("CLSA", detail.ExchangeCode.Value);
    }

    [Fact]
    public async Task An_unclassified_instrument_is_still_returned()
    {
        // The case an inner join silently breaks: an index belongs to no
        // industry, and asking for it must not answer "no such instrument".
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var venue = await AddExchangeAsync(scope, "CLSB");

        var instrument = Instrument.Register(
            venue, Ticker.Create("CLBIDX"), "CLSB Composite Index", AssetType.Index, CurrencyCode.Vnd, Now);
        instrument.List(Now);

        scope.Instruments.Add(instrument);
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        var detail = await scope.Catalog.FindDetailAsync(
            instrument.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(detail);
        Assert.Null(detail.Classification);
        Assert.Equal("CLBIDX", detail.Ticker.Value);
    }

    [Fact]
    public async Task An_unknown_identifier_reads_as_nothing()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();

        // Act
        var detail = await scope.Catalog.FindDetailAsync(
            InstrumentId.New(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(detail);
    }

    [Fact]
    public async Task Clearing_an_industry_is_persisted()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var venue = await AddExchangeAsync(scope, "CLSC");
        var industryId = await AddIndustryAsync(scope, "CLSC-SEC", "CLSC Sector", "CLSC-IND", "CLSC Industry");

        var instrument = Instrument.Register(
            venue, Ticker.Create("CLC"), "Reclassified Company", AssetType.Equity, CurrencyCode.Vnd, Now);
        instrument.List(Now);
        instrument.AssignIndustry(industryId, Now);
        scope.Instruments.Add(instrument);
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        var tracked = await scope.Instruments.FindByIdAsync(
            instrument.Id, TestContext.Current.CancellationToken);
        tracked!.ClearIndustry(Now.AddDays(1));
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        var detail = await scope.Catalog.FindDetailAsync(
            instrument.Id, TestContext.Current.CancellationToken);

        Assert.NotNull(detail);
        Assert.Null(detail.Classification);
    }

    private static async Task<ExchangeId> AddExchangeAsync(ClassificationScope scope, string code)
    {
        var exchange = Exchange.Register(
            ExchangeCode.Create(code), $"{code} Test Venue", "Asia/Ho_Chi_Minh", Now);

        scope.Exchanges.Add(exchange);
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        return exchange.Id;
    }

    private static async Task<IndustryId> AddIndustryAsync(
        ClassificationScope scope,
        string sectorCode,
        string sectorName,
        string industryCode,
        string industryName)
    {
        var sector = Sector.Register(ClassificationCode.Create(sectorCode), sectorName, Now);
        scope.Classification.AddSector(sector);
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        var industry = Industry.Register(
            sector.Id, ClassificationCode.Create(industryCode), industryName, Now);
        scope.Classification.AddIndustry(industry);
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        return industry.Id;
    }

    private async Task<ClassificationScope> CreateScopeAsync()
    {
        var factory = PersonalQuantApiFactory.WithDependencies(
            containers.Postgres,
            containers.Redis,
            applyMigrations: true);

        using var client = factory.CreateClient();
        _ = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        return new ClassificationScope(factory);
    }

    /// <summary>
    /// Owns a host and a DI scope, so every test reads and writes through the
    /// real composition root.
    /// </summary>
    private sealed class ClassificationScope : IAsyncDisposable
    {
        private readonly PersonalQuantApiFactory _factory;
        private readonly AsyncServiceScope _scope;

        public ClassificationScope(PersonalQuantApiFactory factory)
        {
            _factory = factory;
            _scope = factory.Services.CreateAsyncScope();

            UnitOfWork = _scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            Exchanges = _scope.ServiceProvider.GetRequiredService<IExchangeRepository>();
            Instruments = _scope.ServiceProvider.GetRequiredService<IInstrumentRepository>();
            Classification = _scope.ServiceProvider.GetRequiredService<IClassificationRepository>();
            Catalog = _scope.ServiceProvider.GetRequiredService<IInstrumentCatalog>();
        }

        public IUnitOfWork UnitOfWork { get; }

        public IExchangeRepository Exchanges { get; }

        public IInstrumentRepository Instruments { get; }

        public IClassificationRepository Classification { get; }

        public IInstrumentCatalog Catalog { get; }

        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync();
            await _factory.DisposeAsync();
        }
    }
}
