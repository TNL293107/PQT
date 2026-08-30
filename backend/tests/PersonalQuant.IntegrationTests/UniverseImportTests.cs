using Microsoft.Extensions.DependencyInjection;
using PersonalQuant.Application.Abstractions;
using PersonalQuant.Application.Exchanges;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Application.Universes;
using PersonalQuant.Domain.Currencies;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.Domain.Universes;

namespace PersonalQuant.IntegrationTests;

/// <summary>
/// Runs the universe import from real CSV files against real PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// End to end on purpose: the file source, the symbol resolution, the
/// reconciliation against recorded spells, the schema constraints and the
/// coverage review all have to agree, and each of them is a place where a
/// membership history can be quietly rewritten.
/// </para>
/// <para>
/// The files are written per test into a temporary directory, so a fixture
/// change cannot silently alter what these prove.
/// </para>
/// </remarks>
/// <param name="containers">Shared container fixture.</param>
[Collection(DependencyContainerTests.Name)]
public sealed class UniverseImportTests(DependencyContainerFixture containers) : IDisposable
{
    private static readonly DateTimeOffset RecordedAt = new(2026, 8, 30, 3, 0, 0, TimeSpan.Zero);
    private static readonly SourceCode FileSource = SourceCode.Create("FILE");

    private readonly string _directory = Directory.CreateTempSubdirectory("pqt-universe-").FullName;

    [Fact]
    public async Task An_import_records_the_history_the_file_states()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        WriteUniverses("code,name,kind,coverage_from,coverage_until", "UIA,Import A,Index,2026-01-02,");
        WriteMemberships(
            "universe_code,symbol,effective_from,effective_to,announced_on",
            "UIA,UIA.HM,2026-01-02,2026-04-01,2025-12-15",
            "UIA,UIB.HM,2026-01-02,,2025-12-15");

        await using var scope = await CreateScopeAsync();
        await AddInstrumentAsync(scope, "UIA", "UIAA", "UIA.HM");
        await AddInstrumentAsync(scope, "UIB", "UIBB", "UIB.HM");

        // Act
        var report = await scope.Import.ImportAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, report.UniversesDefined);
        Assert.Equal(1, report.CoverageDeclared);
        Assert.Equal(2, report.SpellsCreated);
        Assert.Equal(0, report.Rejected);

        await using var reader = await CreateScopeAsync();
        var code = UniverseCode.Create("UIA");

        var inJanuary = await reader.Catalog.ConstituentsAsOfAsync(
            code, new DateOnly(2026, 1, 2), TestContext.Current.CancellationToken);
        var inMay = await reader.Catalog.ConstituentsAsOfAsync(
            code, new DateOnly(2026, 5, 1), TestContext.Current.CancellationToken);

        Assert.Equal(2, inJanuary.Members.Count);
        Assert.Single(inMay.Members);
    }

    [Fact]
    public async Task A_re_entry_in_the_file_becomes_two_spells_with_a_gap()
    {
        // The shape the fixture demonstrates and the one a real review history
        // is full of. The months between the spells must stay visible.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        WriteUniverses("code,name,kind,coverage_from,coverage_until", "UIB,Import B,Index,2026-01-02,");
        WriteMemberships(
            "universe_code,symbol,effective_from,effective_to,announced_on",
            "UIB,UIC.HM,2026-01-02,2026-04-01,",
            "UIB,UIC.HM,2026-07-01,,");

        await using var scope = await CreateScopeAsync();
        await AddInstrumentAsync(scope, "UIC", "UICC", "UIC.HM");

        // Act
        var report = await scope.Import.ImportAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, report.SpellsCreated);
        Assert.Equal(0, report.Rejected);

        await using var reader = await CreateScopeAsync();
        var code = UniverseCode.Create("UIB");

        var during = await reader.Catalog.ConstituentsAsOfAsync(
            code, new DateOnly(2026, 3, 1), TestContext.Current.CancellationToken);
        var gap = await reader.Catalog.ConstituentsAsOfAsync(
            code, new DateOnly(2026, 5, 1), TestContext.Current.CancellationToken);
        var after = await reader.Catalog.ConstituentsAsOfAsync(
            code, new DateOnly(2026, 8, 1), TestContext.Current.CancellationToken);

        Assert.Single(during.Members);
        Assert.Empty(gap.Members);
        Assert.Single(after.Members);
    }

    [Fact]
    public async Task Running_the_same_import_twice_changes_nothing()
    {
        // Idempotence is what makes it safe to run at every start-up. A second
        // run that created a spell would double an index's constituent count.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        WriteUniverses("code,name,kind,coverage_from,coverage_until", "UIC,Import C,Index,2026-01-02,");
        WriteMemberships(
            "universe_code,symbol,effective_from,effective_to,announced_on",
            "UIC,UID.HM,2026-01-02,,");

        await using var scope = await CreateScopeAsync();
        await AddInstrumentAsync(scope, "UID", "UIDD", "UID.HM");
        await scope.Import.ImportAsync(TestContext.Current.CancellationToken);

        // Act
        await using var second = await CreateScopeAsync();
        var report = await second.Import.ImportAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, report.UniversesDefined);
        Assert.Equal(0, report.SpellsCreated);
        Assert.Equal(1, report.Unchanged);
        Assert.Equal(0, report.Rejected);
    }

    [Fact]
    public async Task A_departure_reported_later_closes_the_open_spell()
    {
        // How a removal actually arrives: the security was a member with no end
        // date, and the next review notice says when it left.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        WriteUniverses("code,name,kind,coverage_from,coverage_until", "UID,Import D,Index,2026-01-02,");
        WriteMemberships(
            "universe_code,symbol,effective_from,effective_to,announced_on",
            "UID,UIE.HM,2026-01-02,,");

        await using var scope = await CreateScopeAsync();
        await AddInstrumentAsync(scope, "UIE", "UIEE", "UIE.HM");
        await scope.Import.ImportAsync(TestContext.Current.CancellationToken);

        WriteMemberships(
            "universe_code,symbol,effective_from,effective_to,announced_on",
            "UID,UIE.HM,2026-01-02,2026-07-01,2026-06-15");

        // Act
        await using var second = await CreateScopeAsync();
        var report = await second.Import.ImportAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, report.SpellsClosed);

        await using var reader = await CreateScopeAsync();
        var code = UniverseCode.Create("UID");

        var before = await reader.Catalog.ConstituentsAsOfAsync(
            code, new DateOnly(2026, 6, 30), TestContext.Current.CancellationToken);
        var after = await reader.Catalog.ConstituentsAsOfAsync(
            code, new DateOnly(2026, 7, 1), TestContext.Current.CancellationToken);

        Assert.Single(before.Members);
        Assert.Empty(after.Members);
    }

    [Fact]
    public async Task A_source_that_changes_its_mind_about_a_closed_spell_is_refused()
    {
        // A spell that has ended is a fact something may already have run a
        // backtest against. Rewriting it silently would change which securities
        // that backtest could have chosen from, after the fact.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        WriteUniverses("code,name,kind,coverage_from,coverage_until", "UIE,Import E,Index,2026-01-02,");
        WriteMemberships(
            "universe_code,symbol,effective_from,effective_to,announced_on",
            "UIE,UIF.HM,2026-01-02,2026-04-01,");

        await using var scope = await CreateScopeAsync();
        await AddInstrumentAsync(scope, "UIF", "UIFF", "UIF.HM");
        await scope.Import.ImportAsync(TestContext.Current.CancellationToken);

        WriteMemberships(
            "universe_code,symbol,effective_from,effective_to,announced_on",
            "UIE,UIF.HM,2026-01-02,2026-05-01,");

        // Act
        await using var second = await CreateScopeAsync();
        var report = await second.Import.ImportAsync(TestContext.Current.CancellationToken);

        // Assert
        var rejection = Assert.Single(report.Rejections);
        Assert.Equal(
            UniverseMembershipRejectionReason.ContradictsRecordedSpell,
            rejection.Reason);

        await using var reader = await CreateScopeAsync();
        var stillOut = await reader.Catalog.ConstituentsAsOfAsync(
            UniverseCode.Create("UIE"),
            new DateOnly(2026, 4, 15),
            TestContext.Current.CancellationToken);

        Assert.Empty(stillOut.Members);
    }

    [Fact]
    public async Task An_overlapping_spell_is_refused_by_name_rather_than_by_constraint()
    {
        // The exclusion constraint would refuse this too, and would take the
        // whole run's transaction with it. One malformed row must not stop a
        // decade of reviews from being recorded.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        WriteUniverses("code,name,kind,coverage_from,coverage_until", "UIF,Import F,Index,2026-01-02,");
        WriteMemberships(
            "universe_code,symbol,effective_from,effective_to,announced_on",
            "UIF,UIG.HM,2026-01-02,,",
            "UIF,UIG.HM,2026-04-01,,",
            "UIF,UIH.HM,2026-01-02,,");

        await using var scope = await CreateScopeAsync();
        await AddInstrumentAsync(scope, "UIG", "UIGG", "UIG.HM");
        await AddInstrumentAsync(scope, "UIH", "UIHH", "UIH.HM");

        // Act
        var report = await scope.Import.ImportAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, report.SpellsCreated);
        var rejection = Assert.Single(report.Rejections);
        Assert.Equal(UniverseMembershipRejectionReason.OverlapsRecordedSpell, rejection.Reason);
        Assert.Contains("UIG.HM", rejection.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_universe_with_no_history_is_recorded_as_a_finding_by_the_import()
    {
        // The requirement, running where it actually has to run. A universe the
        // file names and never populates leaves a record, in the same
        // transaction as the rows that were populated.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        WriteUniverses(
            "code,name,kind,coverage_from,coverage_until",
            "UIG,Import G,Index,2026-01-02,",
            "UIH,Import H With No History,Index,,");
        WriteMemberships(
            "universe_code,symbol,effective_from,effective_to,announced_on",
            "UIG,UII.HM,2026-01-02,,");

        await using var scope = await CreateScopeAsync();
        await AddInstrumentAsync(scope, "UII", "UIII", "UII.HM");

        // Act
        await scope.Import.ImportAsync(TestContext.Current.CancellationToken);

        // Assert
        await using var reader = await CreateScopeAsync();
        var empty = await reader.Universes.FindByCodeAsync(
            UniverseCode.Create("UIH"), TestContext.Current.CancellationToken);

        Assert.NotNull(empty);

        var findings = await reader.Universes.ListOpenFindingsAsync(
            empty.Id, TestContext.Current.CancellationToken);

        Assert.Equal(
            UniverseCoverageFindingKind.NoMembershipRecorded,
            Assert.Single(findings).Kind);

        var unknown = await reader.Catalog.ConstituentsAsOfAsync(
            UniverseCode.Create("UIH"),
            new DateOnly(2026, 5, 1),
            TestContext.Current.CancellationToken);

        Assert.False(unknown.IsKnown);
    }

    [Fact]
    public async Task A_symbol_that_resolves_to_nothing_is_refused_rather_than_guessed()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        WriteUniverses("code,name,kind,coverage_from,coverage_until", "UII,Import I,Index,2026-01-02,");
        WriteMemberships(
            "universe_code,symbol,effective_from,effective_to,announced_on",
            "UII,NOSUCH.HM,2026-01-02,,");

        await using var scope = await CreateScopeAsync();

        // Act
        var report = await scope.Import.ImportAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, report.SpellsCreated);
        Assert.Equal(
            UniverseMembershipRejectionReason.UnknownInstrument,
            Assert.Single(report.Rejections).Reason);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private void WriteUniverses(params string[] lines) =>
        File.WriteAllLines(Path.Combine(_directory, "universes.csv"), lines);

    private void WriteMemberships(params string[] lines) =>
        File.WriteAllLines(Path.Combine(_directory, "universe-memberships.csv"), lines);

    private static async Task AddInstrumentAsync(
        UniverseImportScope scope,
        string venueCode,
        string ticker,
        string providerSymbol)
    {
        var exchange = Exchange.Register(
            ExchangeCode.Create(venueCode), $"{venueCode} Test Venue", "Asia/Ho_Chi_Minh", RecordedAt);

        scope.Exchanges.Add(exchange);
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        var instrument = Instrument.Register(
            exchange.Id,
            Ticker.Create(ticker),
            $"{ticker} Test Company",
            AssetType.Equity,
            CurrencyCode.Vnd,
            RecordedAt);

        instrument.List(RecordedAt);

        scope.Instruments.Add(instrument);
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        // The alias the instrument import would have written. The universe
        // import resolves through it and has no fallback to a bare ticker.
        scope.Instruments.AddIdentifier(InstrumentIdentifier.Record(
            instrument.Id,
            IdentifierValue.Create(IdentifierScheme.ProviderSymbol, providerSymbol),
            FileSource,
            RecordedAt));

        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<UniverseImportScope> CreateScopeAsync()
    {
        var factory = PersonalQuantApiFactory.WithUniverseDirectory(
            containers.Postgres,
            containers.Redis,
            _directory);

        using var client = factory.CreateClient();
        _ = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        return new UniverseImportScope(factory);
    }

    private sealed class UniverseImportScope : IAsyncDisposable
    {
        private readonly PersonalQuantApiFactory _factory;
        private readonly AsyncServiceScope _scope;

        public UniverseImportScope(PersonalQuantApiFactory factory)
        {
            _factory = factory;
            _scope = factory.Services.CreateAsyncScope();

            UnitOfWork = _scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            Exchanges = _scope.ServiceProvider.GetRequiredService<IExchangeRepository>();
            Instruments = _scope.ServiceProvider.GetRequiredService<IInstrumentRepository>();
            Universes = _scope.ServiceProvider.GetRequiredService<IUniverseRepository>();
            Catalog = _scope.ServiceProvider.GetRequiredService<IUniverseCatalog>();
            Import = _scope.ServiceProvider.GetRequiredService<IUniverseImportService>();
        }

        public IUnitOfWork UnitOfWork { get; }

        public IExchangeRepository Exchanges { get; }

        public IInstrumentRepository Instruments { get; }

        public IUniverseRepository Universes { get; }

        public IUniverseCatalog Catalog { get; }

        public IUniverseImportService Import { get; }

        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync();
            await _factory.DisposeAsync();
        }
    }
}
