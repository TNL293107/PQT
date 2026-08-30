using Microsoft.EntityFrameworkCore;
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
using PersonalQuant.Infrastructure.Persistence;

namespace PersonalQuant.IntegrationTests;

/// <summary>
/// Verifies universe membership against real PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// Three of the properties under test exist only in the schema. The half-open
/// interval is a <c>WHERE</c> clause; the refusal to let one security hold two
/// spells over the same dates is an exclusion constraint over a
/// <c>daterange</c>; the refusal of an interval covering no session is a check
/// constraint. None can be proved anywhere but against a database, and an
/// importer that enforced them in application code would enforce them only for
/// as long as it was the only writer.
/// </para>
/// <para>
/// The fourth is the one that matters most and is not a constraint at all: a
/// date nobody has sourced must read as unknown rather than as an index with no
/// constituents.
/// </para>
/// </remarks>
/// <param name="containers">Shared container fixture.</param>
[Collection(DependencyContainerTests.Name)]
public sealed class UniversePersistenceTests(DependencyContainerFixture containers)
{
    private static readonly SourceCode Source = SourceCode.Create("TEST");
    private static readonly DateTimeOffset RecordedAt = new(2026, 8, 30, 3, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Joined = new(2024, 1, 2);
    private static readonly DateOnly Left = new(2024, 7, 1);
    private static readonly DateOnly Rejoined = new(2025, 1, 6);

    [Fact]
    public async Task A_security_is_a_member_from_the_day_it_joined_until_the_day_it_left()
    {
        // The half-open interval, proved through the query rather than the
        // entity. A review that swaps one name for another happens on a single
        // date, and that date belongs to the joiner alone.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var code = UniverseCode.Create("UMA");

        // Coverage starts before the security joined, so the day before its
        // admission is a covered date with nobody in it — an empty answer that
        // is a fact. Without that the day before would be unknown, and this
        // test would prove the coverage claim rather than the interval.
        var universe = await DefineAsync(scope, code, Joined.AddDays(-30), until: null);
        var security = await AddInstrumentAsync(scope, "UMA", "UMAA");

        var membership = UniverseMembership.Admit(
            universe.Id, security, Joined, announcedOn: null, Source, RecordedAt);
        membership.Remove(Left);

        scope.Universes.Add(membership);
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        await using var reader = await CreateScopeAsync();
        var dayBefore = await ReadAsync(reader, code, Joined.AddDays(-1));
        var firstDay = await ReadAsync(reader, code, Joined);
        var lastDay = await ReadAsync(reader, code, Left.AddDays(-1));
        var removalDay = await ReadAsync(reader, code, Left);

        // Assert
        Assert.Empty(dayBefore.Members);
        Assert.Equal(security, Assert.Single(firstDay.Members));
        Assert.Equal(security, Assert.Single(lastDay.Members));
        Assert.Empty(removalDay.Members);
    }

    [Fact]
    public async Task A_re_entry_leaves_a_gap_the_read_can_see()
    {
        // Two spells, not one interval with a hole punched in it. The months
        // between them are the part a survivorship-free backtest must be able
        // to see: the security existed, kept its prices, and was not selectable
        // from this universe.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var code = UniverseCode.Create("UMB");
        var universe = await DefineAsync(scope, code, Joined, until: null);
        var security = await AddInstrumentAsync(scope, "UMB", "UMBA");

        var first = UniverseMembership.Admit(
            universe.Id, security, Joined, announcedOn: null, Source, RecordedAt);
        first.Remove(Left);

        var second = UniverseMembership.Admit(
            universe.Id, security, Rejoined, announcedOn: null, Source, RecordedAt);

        scope.Universes.Add(first);
        scope.Universes.Add(second);
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        await using var reader = await CreateScopeAsync();
        var duringFirst = await ReadAsync(reader, code, new DateOnly(2024, 3, 1));
        var inTheGap = await ReadAsync(reader, code, new DateOnly(2024, 10, 1));
        var duringSecond = await ReadAsync(reader, code, new DateOnly(2025, 3, 1));

        // Assert
        Assert.Single(duringFirst.Members);
        Assert.Empty(inTheGap.Members);
        Assert.Single(duringSecond.Members);
    }

    [Fact]
    public async Task Two_spells_of_one_security_cannot_cover_the_same_dates()
    {
        // The exclusion constraint. Both spells are open-ended, so both claim
        // the security is a member today — an index of thirty holding one name
        // twice. The primary key cannot see it: the two rows differ by start
        // date.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var code = UniverseCode.Create("UMC");
        var universe = await DefineAsync(scope, code, Joined, until: null);
        var security = await AddInstrumentAsync(scope, "UMC", "UMCA");

        scope.Universes.Add(UniverseMembership.Admit(
            universe.Id, security, Joined, announcedOn: null, Source, RecordedAt));
        scope.Universes.Add(UniverseMembership.Admit(
            universe.Id, security, Rejoined, announcedOn: null, Source, RecordedAt));

        // Act & Assert
        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken));

        Assert.Contains(
            "ex_universe_memberships_no_overlap",
            error.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Spells_that_meet_at_a_date_are_accepted()
    {
        // The boundary the exclusion constraint must not over-reach: a security
        // whose spell is closed on the same date another opens is one
        // continuous membership recorded in two rows, which happens whenever a
        // source restates a spell rather than extending it.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var code = UniverseCode.Create("UMD");
        var universe = await DefineAsync(scope, code, Joined, until: null);
        var security = await AddInstrumentAsync(scope, "UMD", "UMDA");

        var first = UniverseMembership.Admit(
            universe.Id, security, Joined, announcedOn: null, Source, RecordedAt);
        first.Remove(Left);

        var second = UniverseMembership.Admit(
            universe.Id, security, Left, announcedOn: null, Source, RecordedAt);

        scope.Universes.Add(first);
        scope.Universes.Add(second);

        // Act
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        await using var reader = await CreateScopeAsync();
        Assert.Single((await ReadAsync(reader, code, Left)).Members);
    }

    [Fact]
    public async Task A_spell_that_covers_no_session_is_refused_by_the_database()
    {
        // The domain refuses this too, and the check constraint is what makes
        // the refusal true of the table rather than of one code path. Written
        // through raw SQL precisely because the domain cannot produce it.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var code = UniverseCode.Create("UME");
        var universe = await DefineAsync(scope, code, Joined, until: null);
        var security = await AddInstrumentAsync(scope, "UME", "UMEA");

        // Act & Assert
        var error = await Assert.ThrowsAnyAsync<Exception>(() =>
            scope.Database.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 INSERT INTO quant.universe_memberships (
                     universe_id, instrument_id, effective_from, effective_to,
                     announced_on, source, recorded_at_utc)
                 VALUES (
                     {universe.Id.Value}, {security.Value}, {Joined}, {Joined},
                     NULL, {Source.Value}, {RecordedAt});
                 """,
                TestContext.Current.CancellationToken));

        Assert.Contains(
            "ck_universe_memberships_interval",
            error.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_date_nobody_sourced_reads_as_unknown_rather_than_empty()
    {
        // The survivorship guarantee, end to end. The universe is real, its
        // recent membership is recorded, and 2018 is not. The rows for 2018 are
        // absent either way; only the coverage claim can say which absence this
        // is.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var code = UniverseCode.Create("UMF");
        var universe = await DefineAsync(scope, code, Joined, until: null);
        var security = await AddInstrumentAsync(scope, "UMF", "UMFA");

        scope.Universes.Add(UniverseMembership.Admit(
            universe.Id, security, Joined, announcedOn: null, Source, RecordedAt));
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        await using var reader = await CreateScopeAsync();
        var sourced = await ReadAsync(reader, code, Joined);
        var unsourced = await ReadAsync(reader, code, new DateOnly(2018, 6, 1));

        // Assert
        Assert.True(sourced.IsKnown);
        Assert.Single(sourced.Members);

        Assert.False(unsourced.IsKnown);
        Assert.Equal(UniverseUnknownReason.OutsideCoverage, unsourced.UnknownReason);
        Assert.Throws<InvalidOperationException>(() => unsourced.Members);
    }

    [Fact]
    public async Task A_universe_with_no_coverage_claim_knows_no_date()
    {
        // What a universe looks like before anyone sources it. Nothing about
        // this state may resemble an answer.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var code = UniverseCode.Create("UMG");
        await DefineAsync(scope, code, coverageFrom: null, until: null);

        // Act
        await using var reader = await CreateScopeAsync();
        var result = await ReadAsync(reader, code, Joined);

        // Assert
        Assert.False(result.IsKnown);
        Assert.Equal(UniverseUnknownReason.NoCoverageDeclared, result.UnknownReason);
    }

    [Fact]
    public async Task A_coverage_claim_survives_a_round_trip()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var code = UniverseCode.Create("UMH");
        await DefineAsync(scope, code, Joined, until: Rejoined);

        // Act
        await using var reader = await CreateScopeAsync();
        var stored = await reader.Universes.FindByCodeAsync(
            code, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(stored);
        Assert.Equal(Joined, stored.Coverage?.From);
        Assert.Equal(Rejoined, stored.Coverage?.Until);
        Assert.Equal(UniverseKind.Index, stored.Kind);
    }

    private static async Task<UniverseConstituents> ReadAsync(
        UniverseScope scope,
        UniverseCode code,
        DateOnly asOf) =>
        await scope.Catalog.ConstituentsAsOfAsync(
            code, asOf, TestContext.Current.CancellationToken);

    private static async Task<Universe> DefineAsync(
        UniverseScope scope,
        UniverseCode code,
        DateOnly? coverageFrom,
        DateOnly? until)
    {
        var universe = Universe.Define(
            UniverseId.New(),
            code,
            $"{code.Value} Test Universe",
            UniverseKind.Index,
            Source,
            RecordedAt);

        if (coverageFrom is { } from)
        {
            universe.DeclareCoverage(MembershipCoverage.Create(from, until), Source, RecordedAt);
        }

        scope.Universes.Add(universe);
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        return universe;
    }

    private static async Task<InstrumentId> AddInstrumentAsync(
        UniverseScope scope,
        string venueCode,
        string ticker)
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

        return instrument.Id;
    }

    private async Task<UniverseScope> CreateScopeAsync()
    {
        var factory = PersonalQuantApiFactory.WithDependencies(
            containers.Postgres,
            containers.Redis,
            applyMigrations: true);

        using var client = factory.CreateClient();
        _ = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        return new UniverseScope(factory);
    }

    private sealed class UniverseScope : IAsyncDisposable
    {
        private readonly PersonalQuantApiFactory _factory;
        private readonly AsyncServiceScope _scope;

        public UniverseScope(PersonalQuantApiFactory factory)
        {
            _factory = factory;
            _scope = factory.Services.CreateAsyncScope();

            UnitOfWork = _scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            Exchanges = _scope.ServiceProvider.GetRequiredService<IExchangeRepository>();
            Instruments = _scope.ServiceProvider.GetRequiredService<IInstrumentRepository>();
            Universes = _scope.ServiceProvider.GetRequiredService<IUniverseRepository>();
            Catalog = _scope.ServiceProvider.GetRequiredService<IUniverseCatalog>();
            Database = _scope.ServiceProvider.GetRequiredService<PersonalQuantDbContext>();
        }

        public IUnitOfWork UnitOfWork { get; }

        public IExchangeRepository Exchanges { get; }

        public IInstrumentRepository Instruments { get; }

        public IUniverseRepository Universes { get; }

        public IUniverseCatalog Catalog { get; }

        public PersonalQuantDbContext Database { get; }

        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync();
            await _factory.DisposeAsync();
        }
    }
}
