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
/// <para>
/// Search spans the whole instrument master, and every class in the
/// collection shares one database. Other tests create instruments with random
/// tickers (<c>UniverseEndpointTests</c> uses six hex characters, so
/// <c>RCDAA24</c> contains <c>CDA</c>), so a result list may hold rows this
/// test never made. Every assertion here is therefore made on
/// <see cref="Own"/>: the results, in their ranked order, restricted to the
/// instruments the test itself created. Ranking among them is unchanged by
/// rows from elsewhere, so this keeps what each test proves.
/// </para>
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
        var created = await AddInstrumentsAsync(
            scope,
            venue,
            ("ZQA", "ZQA Corporation"),
            ("ZQB", "ZQA Telecom Joint Stock Company"),
            ("ZQC", "A Company Mentioning ZQA Midway"));

        // Act
        var results = Own(await SearchAsync(scope, "ZQA"), created);

        // Assert
        Assert.Equal(3, results.Count);
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

        var created = await AddInstrumentsAsync(
            scope,
            venue,
            ("VNMX", "An Unrelated Company"),
            ("ZZZ", "VNM Holdings"));

        // Act
        var results = Own(await SearchAsync(scope, "VNM"), created);

        // Assert
        Assert.Equal(2, results.Count);
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

        var created = await AddInstrumentsAsync(
            scope,
            venue,
            ("QQQ", "Masan Group Corporation"),
            ("RRR", "Masan Group"));

        // Act
        var results = Own(await SearchAsync(scope, "Masan Group"), created);

        // Assert
        Assert.Equal(2, results.Count);
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

        var created = await AddInstrumentsAsync(scope, venue, ("HPG", "Hoa Phat Group Joint Stock Company"));

        // Act
        var lower = Own(await SearchAsync(scope, "hpg"), created);
        var mixed = Own(await SearchAsync(scope, "hOa pHat"), created);

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

        var created = await AddInstrumentsAsync(scope, venue, ("VCB", "Ngân hàng Ngoại thương Việt Nam"));

        // Act
        var unaccented = Own(await SearchAsync(scope, "ngan hang"), created);
        var accented = Own(await SearchAsync(scope, "Ngân hàng"), created);

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
        // "DED" is three hex characters, which a random tag elsewhere can hold.
        var excluded = Own(await SearchAsync(scope, "DED"), gone.Id);
        var included = Own(await SearchAsync(scope, "DED", includeInactive: true), gone.Id);

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

        // Act. Deliberately not restricted to this test's rows: the claim is
        // about the whole answer. X, Y and Z are not hex, so no random tag in
        // the shared database can contain the query.
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

        var created = await AddInstrumentsAsync(
            scope,
            venue,
            ("WCA", "Wildcard Alpha Holdings"),
            ("WCB", "Wildcard Beta 100% Owned"),
            ("WCC", "Wildcard Gamma_Delta Holdings"));

        // Act. Restricted to this test's rows, an unescaped % would still
        // return WCA and WCC, so the claim survives the restriction.
        var percent = Own(await SearchAsync(scope, "%"), created);
        var underscore = Own(await SearchAsync(scope, "GAMMA_DELTA"), created);
        var underscoreAsWildcard = Own(await SearchAsync(scope, "GAMMAXDELTA"), created);

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
        var created = await AddInstrumentsAsync(scope, venue, ("CDA", "A Listed Company"));

        // Act. Filtered by instrument id, not by venue, so the exchange code
        // asserted below is still read from the result rather than assumed.
        var result = Assert.Single(Own(await SearchAsync(scope, "CDA"), created));

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
        var byOldName = Own(await SearchAsync(reader, "Original Trading"), instrument.Id);
        var byNewName = Own(await SearchAsync(reader, "chung khoan"), instrument.Id);

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

    /// <summary>
    /// The results, in ranked order, that are instruments this test created.
    /// </summary>
    private static List<InstrumentSearchResult> Own(
        IEnumerable<InstrumentSearchResult> results,
        params IEnumerable<InstrumentId> created)
    {
        var ids = created.ToHashSet();
        return [.. results.Where(result => ids.Contains(result.InstrumentId))];
    }

    private static Instrument Register(ExchangeId exchangeId, string ticker, string name) =>
        Instrument.Register(
            exchangeId,
            Ticker.Create(ticker),
            name,
            AssetType.Equity,
            CurrencyCode.Vnd,
            Now);

    private static async Task<IReadOnlyList<InstrumentId>> AddInstrumentsAsync(
        InstrumentScope scope,
        ExchangeId exchangeId,
        params (string Ticker, string Name)[] instruments)
    {
        var created = new List<InstrumentId>();

        foreach (var (ticker, name) in instruments)
        {
            var instrument = Register(exchangeId, ticker, name);
            instrument.List(Now.AddDays(1));
            scope.Instruments.Add(instrument);
            created.Add(instrument.Id);
        }

        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);
        return created;
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
