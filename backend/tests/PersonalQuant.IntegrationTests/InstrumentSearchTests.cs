using Microsoft.Extensions.DependencyInjection;
using PersonalQuant.Application.Abstractions;
using PersonalQuant.Application.Exchanges;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Domain.Currencies;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.IntegrationTests;

/// <summary>
/// Verifies instrument search against real PostgreSQL.
/// </summary>
/// <remarks>
/// Ranking, filtering and the result bound are all expressed as SQL, so this
/// is the only place they can be proved. An in-memory provider would evaluate
/// the same LINQ with .NET string semantics and agree with itself while the
/// database did something else.
/// </remarks>
/// <param name="containers">Shared container fixture.</param>
[Collection(DependencyContainerTests.Name)]
public sealed class InstrumentSearchTests(DependencyContainerFixture containers)
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 14, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task An_exact_ticker_outranks_every_name_match()
    {
        // Scenario A and B of the phase: typing FPT, or a word that several
        // companies share, must put the security the trader meant first.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var venue = await AddExchangeAsync(scope, "RANKA");

        // The token is unique to this test. Search spans the whole instrument
        // master rather than one venue, and the test classes share a database,
        // so a fixture built from a real ticker would be joined by rows
        // another test created.
        await AddInstrumentsAsync(
            scope,
            venue,
            ("ZQA", "ZQA Corporation"),
            ("ZQB", "ZQA Telecom Joint Stock Company"),
            ("ZQC", "A Company Mentioning ZQA Midway"));

        // Act
        var results = await SearchAsync(scope, "ZQA");

        // Assert
        Assert.Equal("ZQA", results[0].Ticker.Value);
        Assert.Equal(InstrumentMatchKind.ExactTicker, results[0].MatchKind);
        Assert.Equal(InstrumentMatchKind.NamePrefix, results[1].MatchKind);
        Assert.Equal(InstrumentMatchKind.NameContains, results[2].MatchKind);
    }

    [Fact]
    public async Task A_ticker_prefix_outranks_a_name_match()
    {
        // Scenario C: three characters typed into the command bar are far more
        // likely to be the start of a ticker than a fragment of a name.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var venue = await AddExchangeAsync(scope, "RANKB");

        await AddInstrumentsAsync(
            scope,
            venue,
            ("VNMX", "An Unrelated Company"),
            ("ZZZ", "VNM Holdings"));

        // Act
        var results = await SearchAsync(scope, "VNM");

        // Assert
        Assert.Equal(InstrumentMatchKind.TickerPrefix, results[0].MatchKind);
        Assert.Equal("VNMX", results[0].Ticker.Value);
        Assert.Equal(InstrumentMatchKind.NamePrefix, results[1].MatchKind);
    }

    [Fact]
    public async Task An_exact_name_outranks_a_name_prefix()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var venue = await AddExchangeAsync(scope, "RANKC");

        await AddInstrumentsAsync(
            scope,
            venue,
            ("QQQ", "Masan Group Corporation"),
            ("RRR", "Masan Group"));

        // Act
        var results = await SearchAsync(scope, "Masan Group");

        // Assert
        Assert.Equal(InstrumentMatchKind.ExactName, results[0].MatchKind);
        Assert.Equal("RRR", results[0].Ticker.Value);
        Assert.Equal(InstrumentMatchKind.NamePrefix, results[1].MatchKind);
    }

    [Fact]
    public async Task Search_is_case_insensitive()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var venue = await AddExchangeAsync(scope, "CASEA");

        await AddInstrumentsAsync(scope, venue, ("HPG", "Hoa Phat Group Joint Stock Company"));

        // Act
        var lower = await SearchAsync(scope, "hpg");
        var mixed = await SearchAsync(scope, "hOa pHat");

        // Assert
        Assert.Equal("HPG", Assert.Single(lower).Ticker.Value);
        Assert.Equal("HPG", Assert.Single(mixed).Ticker.Value);
    }

    [Fact]
    public async Task A_name_is_found_without_its_Vietnamese_accents()
    {
        // The reason the stored name is folded rather than matched with a
        // case-insensitive collation. Nobody types accents into a terminal.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var venue = await AddExchangeAsync(scope, "ACCEN");

        await AddInstrumentsAsync(scope, venue, ("VCB", "Ngân hàng Ngoại thương Việt Nam"));

        // Act
        var unaccented = await SearchAsync(scope, "ngan hang");
        var accented = await SearchAsync(scope, "Ngân hàng");

        // Assert
        Assert.Equal("VCB", Assert.Single(unaccented).Ticker.Value);
        Assert.Equal("VCB", Assert.Single(accented).Ticker.Value);
    }

    [Fact]
    public async Task Delisted_instruments_are_excluded_by_default()
    {
        // A delisted ticker may already belong to a different issuer, so
        // offering it as a selection would set the terminal's context to a
        // security that cannot be quoted.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var venue = await AddExchangeAsync(scope, "DELIS");

        var gone = Register(venue, "DED", "A Delisted Company");
        gone.List(new DateOnly(2026, 1, 5), Now.AddDays(1));
        gone.Delist(new DateOnly(2026, 6, 1), Now.AddDays(2));
        scope.Instruments.Add(gone);
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        var excluded = await SearchAsync(scope, "DED");
        var included = await SearchAsync(scope, "DED", includeInactive: true);

        // Assert
        Assert.Empty(excluded);
        Assert.Single(included);
    }

    [Fact]
    public async Task A_query_that_matches_nothing_returns_an_empty_list()
    {
        // Not an error, and not a null. The caller distinguishes "nothing
        // matched" from "the request was wrong" by status, not by shape.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var venue = await AddExchangeAsync(scope, "EMPTY");
        await AddInstrumentsAsync(scope, venue, ("FPT", "FPT Corporation"));

        // Act
        var results = await SearchAsync(scope, "XYZABC");

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task The_limit_bounds_the_result_count()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var venue = await AddExchangeAsync(scope, "LIMIT");

        await AddInstrumentsAsync(
            scope,
            venue,
            ("LMA", "Limited Company A"),
            ("LMB", "Limited Company B"),
            ("LMC", "Limited Company C"),
            ("LMD", "Limited Company D"));

        // Act
        var results = await SearchAsync(scope, "LM", limit: 2);

        // Assert
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task Ordering_is_stable_across_identical_queries()
    {
        // A search box whose results reshuffle between identical queries is
        // worse than one that is merely wrong: the row under the cursor moves
        // between the keystroke and the Enter.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var venue = await AddExchangeAsync(scope, "STABL");

        await AddInstrumentsAsync(
            scope,
            venue,
            ("STA", "Stable Holdings One"),
            ("STB", "Stable Holdings Two"),
            ("STC", "Stable Holdings Three"));

        // Act
        var first = await SearchAsync(scope, "STABLE HOLDINGS");
        var second = await SearchAsync(scope, "STABLE HOLDINGS");

        // Assert
        Assert.Equal(
            first.Select(result => result.InstrumentId),
            second.Select(result => result.InstrumentId));
    }

    [Fact]
    public async Task Wildcards_in_a_query_are_matched_literally()
    {
        // An unescaped % would turn any search into a scan returning the whole
        // instrument master, and an unescaped _ would match a character the
        // user never typed. Escaped, both are ordinary text: a query of "%"
        // finds the one name that actually contains a percent sign, and
        // nothing else.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var venue = await AddExchangeAsync(scope, "WILDC");

        await AddInstrumentsAsync(
            scope,
            venue,
            ("WCA", "Wildcard Alpha Holdings"),
            ("WCB", "Wildcard Beta 100% Owned"),
            ("WCC", "Wildcard Gamma_Delta Holdings"));

        // Act
        var percent = await SearchAsync(scope, "%");
        var underscore = await SearchAsync(scope, "GAMMA_DELTA");
        var underscoreAsWildcard = await SearchAsync(scope, "GAMMAXDELTA");

        // Assert
        Assert.Equal("WCB", Assert.Single(percent).Ticker.Value);
        Assert.Equal("WCC", Assert.Single(underscore).Ticker.Value);
        Assert.Empty(underscoreAsWildcard);
    }

    [Fact]
    public async Task A_search_result_carries_the_exchange_code_rather_than_its_key()
    {
        // The user has to be able to tell two listings apart, and a surrogate
        // key tells them nothing.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var venue = await AddExchangeAsync(scope, "CODEA");
        await AddInstrumentsAsync(scope, venue, ("CDA", "A Listed Company"));

        // Act
        var result = Assert.Single(await SearchAsync(scope, "CDA"));

        // Assert
        Assert.Equal("CODEA", result.ExchangeCode.Value);
        Assert.Equal(CurrencyCode.Vnd, result.Currency);
        Assert.Equal(AssetType.Equity, result.AssetType);
        Assert.Equal(InstrumentStatus.Listed, result.Status);
    }

    [Fact]
    public async Task Renaming_an_instrument_changes_what_finds_it()
    {
        // Proves the folded search column is maintained through the database
        // and not only in memory.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var venue = await AddExchangeAsync(scope, "RENAM");

        var instrument = Register(venue, "RNM", "Original Trading Name");
        instrument.List(Now.AddDays(1));
        scope.Instruments.Add(instrument);
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        instrument.Rename("Chứng khoán Sài Gòn", Now.AddDays(2));
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        await using var reader = await CreateScopeAsync();
        var byOldName = await SearchAsync(reader, "Original Trading");
        var byNewName = await SearchAsync(reader, "chung khoan");

        // Assert
        Assert.Empty(byOldName);
        Assert.Equal("RNM", Assert.Single(byNewName).Ticker.Value);
    }

    private static async Task<IReadOnlyList<InstrumentSearchResult>> SearchAsync(
        InstrumentScope scope,
        string query,
        int? limit = null,
        bool includeInactive = false)
    {
        Assert.True(
            InstrumentSearchCriteria.TryCreate(query, limit, includeInactive, out var criteria, out var problem),
            problem);

        return await scope.Search.SearchAsync(criteria, TestContext.Current.CancellationToken);
    }

    private static Instrument Register(ExchangeId exchangeId, string ticker, string name) =>
        Instrument.Register(
            exchangeId,
            Ticker.Create(ticker),
            name,
            AssetType.Equity,
            CurrencyCode.Vnd,
            Now);

    private static async Task AddInstrumentsAsync(
        InstrumentScope scope,
        ExchangeId exchangeId,
        params (string Ticker, string Name)[] instruments)
    {
        foreach (var (ticker, name) in instruments)
        {
            var instrument = Register(exchangeId, ticker, name);
            instrument.List(Now.AddDays(1));
            scope.Instruments.Add(instrument);
        }

        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<ExchangeId> AddExchangeAsync(InstrumentScope scope, string code)
    {
        var exchange = Exchange.Register(
            ExchangeCode.Create(code),
            $"Venue {code}",
            "Asia/Ho_Chi_Minh",
            Now);

        scope.Exchanges.Add(exchange);
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        return exchange.Id;
    }

    private async Task<InstrumentScope> CreateScopeAsync()
    {
        var factory = PersonalQuantApiFactory.WithDependencies(
            containers.Postgres,
            containers.Redis,
            applyMigrations: true);

        using var client = factory.CreateClient();
        _ = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        return new InstrumentScope(factory);
    }

    /// <summary>
    /// Owns a host and a DI scope, so every test reads and writes through the
    /// real composition root.
    /// </summary>
    private sealed class InstrumentScope : IAsyncDisposable
    {
        private readonly PersonalQuantApiFactory _factory;
        private readonly AsyncServiceScope _scope;

        public InstrumentScope(PersonalQuantApiFactory factory)
        {
            _factory = factory;
            _scope = factory.Services.CreateAsyncScope();

            UnitOfWork = _scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            Instruments = _scope.ServiceProvider.GetRequiredService<IInstrumentRepository>();
            Exchanges = _scope.ServiceProvider.GetRequiredService<IExchangeRepository>();
            Search = _scope.ServiceProvider.GetRequiredService<IInstrumentSearchService>();
            Resolver = _scope.ServiceProvider.GetRequiredService<IInstrumentResolver>();
        }

        public IUnitOfWork UnitOfWork { get; }

        public IInstrumentRepository Instruments { get; }

        public IExchangeRepository Exchanges { get; }

        public IInstrumentSearchService Search { get; }

        public IInstrumentResolver Resolver { get; }

        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync();
            await _factory.DisposeAsync();
        }
    }
}
