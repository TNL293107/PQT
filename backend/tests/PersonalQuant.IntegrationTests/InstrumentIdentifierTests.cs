using Microsoft.Extensions.DependencyInjection;
using PersonalQuant.Application.Abstractions;
using PersonalQuant.Application.Exchanges;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Domain.Currencies;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.IntegrationTests;

/// <summary>
/// Verifies identifier aliases, the paged list and the related-instrument read
/// against real PostgreSQL.
/// </summary>
/// <remarks>
/// The properties under test are schema properties. Global uniqueness and
/// per-provider uniqueness are two partial unique indexes, and the paged list's
/// promise — that a caller sees every row exactly once — depends on a total
/// ordering evaluated by the database. Neither can be proved anywhere else.
/// </remarks>
/// <param name="containers">Shared container fixture.</param>
[Collection(DependencyContainerTests.Name)]
public sealed class InstrumentIdentifierTests(DependencyContainerFixture containers)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 26, 3, 0, 0, TimeSpan.Zero);
    private static readonly SourceCode Vendor = SourceCode.Create("VENDOR");
    private static readonly SourceCode Other = SourceCode.Create("OTHER");

    /// <summary>
    /// Synthetic ISINs, valid by check digit.
    /// </summary>
    /// <remarks>
    /// One per test that stores a global identifier, never shared. Every class
    /// here writes to the same database and nothing resets it between tests, so
    /// uniqueness of the data <em>is</em> the isolation — the same reason the
    /// exchange codes and tickers below are all distinct. A global identifier
    /// is unique on scheme and value alone, with no instrument in the key, so
    /// two tests reusing one ISIN collide on
    /// <c>ux_instrument_identifiers_global</c> and whichever runs second fails
    /// in its setup.
    /// </remarks>
    private const string IsinA = "AU0000XVGZA3";
    private const string IsinB = "US0378331005";
    private const string IsinC = "XS0000PQT003";

    [Fact]
    public async Task An_alias_round_trips_and_is_found_by_its_value()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var venue = await AddExchangeAsync(scope, "IDNA");
        var instrumentId = await AddInstrumentAsync(scope, venue, "IDA", "Alias Company");

        await AddAliasAsync(scope, instrumentId, IdentifierScheme.ProviderSymbol, "IDA.HM", Vendor);

        // Act
        await using var reader = await CreateScopeAsync();
        var found = await reader.Instruments.FindIdentifierAsync(
            IdentifierValue.Create(IdentifierScheme.ProviderSymbol, "IDA.HM"),
            Vendor,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(found);
        Assert.Equal(instrumentId, found.InstrumentId);
        Assert.Equal(Vendor, found.Source);
    }

    [Fact]
    public async Task A_global_identifier_may_name_only_one_instrument()
    {
        // An ISIN names the security rather than a listing of it, so a second
        // instrument claiming it means the master has a duplicate.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var venue = await AddExchangeAsync(scope, "IDNB");
        var first = await AddInstrumentAsync(scope, venue, "IDB", "First Company");
        var second = await AddInstrumentAsync(scope, venue, "IDC", "Second Company");

        await AddAliasAsync(scope, first, IdentifierScheme.Isin, IsinA, source: null);

        // Act
        await using var clash = await CreateScopeAsync();
        clash.Instruments.AddIdentifier(InstrumentIdentifier.Record(
            second, IdentifierValue.Create(IdentifierScheme.Isin, IsinA), null, Now));

        // Assert
        await Assert.ThrowsAnyAsync<Exception>(
            () => clash.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Two_providers_may_use_the_same_symbol_for_different_securities()
    {
        // A provider symbol is unique only within the provider that issued it,
        // which is why the source is part of the key.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var venue = await AddExchangeAsync(scope, "IDNC");
        var first = await AddInstrumentAsync(scope, venue, "IDD", "First Company");
        var second = await AddInstrumentAsync(scope, venue, "IDE", "Second Company");

        // Act
        await AddAliasAsync(scope, first, IdentifierScheme.ProviderSymbol, "SHARED", Vendor);
        await AddAliasAsync(scope, second, IdentifierScheme.ProviderSymbol, "SHARED", Other);

        // Assert
        await using var reader = await CreateScopeAsync();
        var value = IdentifierValue.Create(IdentifierScheme.ProviderSymbol, "SHARED");

        var byVendor = await reader.Instruments.FindIdentifierAsync(
            value, Vendor, TestContext.Current.CancellationToken);
        var byOther = await reader.Instruments.FindIdentifierAsync(
            value, Other, TestContext.Current.CancellationToken);

        Assert.Equal(first, byVendor!.InstrumentId);
        Assert.Equal(second, byOther!.InstrumentId);
    }

    [Fact]
    public async Task One_provider_may_not_reuse_a_symbol()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var venue = await AddExchangeAsync(scope, "IDND");
        var first = await AddInstrumentAsync(scope, venue, "IDF", "First Company");
        var second = await AddInstrumentAsync(scope, venue, "IDG", "Second Company");

        await AddAliasAsync(scope, first, IdentifierScheme.ProviderSymbol, "TAKEN", Vendor);

        await using var clash = await CreateScopeAsync();
        clash.Instruments.AddIdentifier(InstrumentIdentifier.Record(
            second,
            IdentifierValue.Create(IdentifierScheme.ProviderSymbol, "TAKEN"),
            Vendor,
            Now));

        await Assert.ThrowsAnyAsync<Exception>(
            () => clash.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_instrument_is_found_by_an_alias_typed_into_search()
    {
        // Exact only, and ranked last: nobody types twelve characters of ISIN
        // by accident, so nothing else will be competing with it.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var venue = await AddExchangeAsync(scope, "IDNE");
        var instrumentId = await AddInstrumentAsync(scope, venue, "IDH", "Searchable Company");

        await AddAliasAsync(scope, instrumentId, IdentifierScheme.Isin, IsinB, source: null);

        // Act
        var byIsin = await SearchAsync(scope, IsinB);
        var byPrefix = await SearchAsync(scope, IsinB[..6]);

        // Assert
        var match = Assert.Single(byIsin);
        Assert.Equal(instrumentId, match.InstrumentId);
        Assert.Equal(InstrumentMatchKind.IdentifierExact, match.MatchKind);

        // A prefix of an identifier identifies nothing.
        Assert.Empty(byPrefix);
    }

    [Fact]
    public async Task An_instruments_aliases_travel_with_its_detail()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var venue = await AddExchangeAsync(scope, "IDNF");
        var instrumentId = await AddInstrumentAsync(scope, venue, "IDI", "Detailed Company");

        await AddAliasAsync(scope, instrumentId, IdentifierScheme.Isin, IsinC, source: null);
        await AddAliasAsync(scope, instrumentId, IdentifierScheme.ProviderSymbol, "IDI.HM", Vendor);

        // Act
        await using var reader = await CreateScopeAsync();
        var detail = await reader.Catalog.FindDetailAsync(
            instrumentId, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(detail);
        Assert.Equal(2, detail.Aliases.Count);
        Assert.Contains(detail.Aliases, alias => alias.Scheme == IdentifierScheme.Isin && alias.Source is null);
        Assert.Contains(detail.Aliases, alias => alias.Source == "VENDOR");
    }

    [Fact]
    public async Task A_reissued_ticker_relates_the_old_holder_to_the_new_one()
    {
        // Vietnamese tickers are released on delisting and reassigned. This is
        // what lets a user see that the security they are looking at is not
        // the one an old chart was drawn from.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var venue = await AddExchangeAsync(scope, "IDNI");

        var gone = Instrument.Register(
            venue, Ticker.Create("IDK"), "Delisted Company", AssetType.Equity, CurrencyCode.Vnd, Now);
        gone.List(new DateOnly(2020, 1, 6), Now);
        gone.Delist(new DateOnly(2024, 6, 28), Now.AddDays(1));
        scope.Instruments.Add(gone);
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        var successor = await AddInstrumentAsync(scope, venue, "IDK", "New Holder");

        // Act
        await using var reader = await CreateScopeAsync();
        var related = await reader.Catalog.ListRelatedAsync(
            successor, TestContext.Current.CancellationToken);

        // Assert
        var relation = Assert.Single(related);
        Assert.Equal(InstrumentRelationKind.TickerHistory, relation.Relation);
        Assert.Equal(gone.Id, relation.Instrument.InstrumentId);
        Assert.Equal("IDK", relation.Detail);
    }

    [Fact]
    public async Task The_paged_list_filters_counts_and_orders_deterministically()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var venue = await AddExchangeAsync(scope, "IDNJ");

        foreach (var ticker in new[] { "LSC", "LSA", "LSD", "LSB" })
        {
            await AddInstrumentAsync(scope, venue, ticker, $"{ticker} Company");
        }

        // Act
        var first = await ListAsync(scope, venue: "IDNJ", limit: 2, offset: 0);
        var second = await ListAsync(scope, venue: "IDNJ", limit: 2, offset: 2);

        // Assert
        Assert.Equal(4, first.Total);
        Assert.Equal(["LSA", "LSB"], first.Items.Select(item => item.Ticker.Value));
        Assert.Equal(["LSC", "LSD"], second.Items.Select(item => item.Ticker.Value));
    }

    [Fact]
    public async Task The_paged_list_includes_delisted_instruments_unless_a_status_is_given()
    {
        // The opposite of search's default. This is the read historical work
        // uses, and omitting delisted rows is how survivorship bias gets in.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var venue = await AddExchangeAsync(scope, "IDNK");

        await AddInstrumentAsync(scope, venue, "LSE", "Live Company");

        var gone = Instrument.Register(
            venue, Ticker.Create("LSF"), "Gone Company", AssetType.Equity, CurrencyCode.Vnd, Now);
        gone.List(Now);
        gone.Delist(new DateOnly(2025, 3, 14), Now.AddDays(1));
        scope.Instruments.Add(gone);
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        var everything = await ListAsync(scope, venue: "IDNK");
        var listedOnly = await ListAsync(scope, venue: "IDNK", status: InstrumentStatus.Listed);

        // Assert
        Assert.Equal(2, everything.Total);
        Assert.Equal(1, listedOnly.Total);
    }

    private static async Task<IReadOnlyList<InstrumentSearchResult>> SearchAsync(
        IdentifierScope scope,
        string query)
    {
        Assert.True(
            InstrumentSearchCriteria.TryCreate(query, null, false, out var criteria, out var problem),
            problem);

        return await scope.Search.SearchAsync(criteria, TestContext.Current.CancellationToken);
    }

    private static async Task<InstrumentPage> ListAsync(
        IdentifierScope scope,
        string venue,
        InstrumentStatus? status = null,
        int? limit = null,
        int? offset = null)
    {
        Assert.True(
            InstrumentListCriteria.TryCreate(
                ExchangeCode.Create(venue),
                null,
                status,
                null,
                limit,
                offset,
                out var criteria,
                out var problem),
            problem);

        return await scope.Catalog.ListAsync(criteria, TestContext.Current.CancellationToken);
    }

    private static async Task AddAliasAsync(
        IdentifierScope scope,
        InstrumentId instrumentId,
        IdentifierScheme scheme,
        string value,
        SourceCode? source)
    {
        scope.Instruments.AddIdentifier(InstrumentIdentifier.Record(
            instrumentId, IdentifierValue.Create(scheme, value), source, Now));

        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<ExchangeId> AddExchangeAsync(IdentifierScope scope, string code)
    {
        var exchange = Exchange.Register(
            ExchangeCode.Create(code), $"{code} Test Venue", "Asia/Ho_Chi_Minh", Now);

        scope.Exchanges.Add(exchange);
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        return exchange.Id;
    }

    private static async Task<InstrumentId> AddInstrumentAsync(
        IdentifierScope scope,
        ExchangeId exchangeId,
        string ticker,
        string name)
    {
        var instrument = Instrument.Register(
            exchangeId, Ticker.Create(ticker), name, AssetType.Equity, CurrencyCode.Vnd, Now);

        instrument.List(Now);

        scope.Instruments.Add(instrument);
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        return instrument.Id;
    }

    private async Task<IdentifierScope> CreateScopeAsync()
    {
        var factory = PersonalQuantApiFactory.WithDependencies(
            containers.Postgres,
            containers.Redis,
            applyMigrations: true);

        using var client = factory.CreateClient();
        _ = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        return new IdentifierScope(factory);
    }

    /// <summary>
    /// Owns a host and a DI scope, so every test reads and writes through the
    /// real composition root.
    /// </summary>
    private sealed class IdentifierScope : IAsyncDisposable
    {
        private readonly PersonalQuantApiFactory _factory;
        private readonly AsyncServiceScope _scope;

        public IdentifierScope(PersonalQuantApiFactory factory)
        {
            _factory = factory;
            _scope = factory.Services.CreateAsyncScope();

            UnitOfWork = _scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            Exchanges = _scope.ServiceProvider.GetRequiredService<IExchangeRepository>();
            Instruments = _scope.ServiceProvider.GetRequiredService<IInstrumentRepository>();
            Catalog = _scope.ServiceProvider.GetRequiredService<IInstrumentCatalog>();
            Search = _scope.ServiceProvider.GetRequiredService<IInstrumentSearchService>();
        }

        public IUnitOfWork UnitOfWork { get; }

        public IExchangeRepository Exchanges { get; }

        public IInstrumentRepository Instruments { get; }

        public IInstrumentCatalog Catalog { get; }

        public IInstrumentSearchService Search { get; }

        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync();
            await _factory.DisposeAsync();
        }
    }
}
