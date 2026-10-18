using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using PersonalQuant.Api.Contracts;
using PersonalQuant.Application.Abstractions;
using PersonalQuant.Application.CorporateActions;
using PersonalQuant.Application.Exchanges;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Application.Universes;
using PersonalQuant.Domain.CorporateActions;
using PersonalQuant.Domain.Currencies;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.Domain.Universes;

namespace PersonalQuant.IntegrationTests;

/// <summary>
/// Verifies the coverage registry end to end: HTTP, the application query and
/// the grouped reads against real PostgreSQL.
/// </summary>
/// <remarks>
/// The aggregates are the part only a database can prove — grouping by a
/// converted source column, the newest run per resolution, the session count
/// over an array parameter — and the wire shape is the part only the pipeline
/// can: states as names, and an unmeasured count as null rather than zero.
/// </remarks>
/// <param name="containers">Shared container fixture.</param>
[Collection(DependencyContainerTests.Name)]
public sealed class CoverageEndpointTests(DependencyContainerFixture containers)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 1, 0, 0, TimeSpan.Zero);
    private static readonly SourceCode Source = SourceCode.Create(DailySource.Code);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // The week of 3 August 2026.
    private static readonly DateOnly Monday = new(2026, 8, 3);

    [Fact]
    public async Task An_unknown_instrument_is_not_found()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var host = await CreateHostAsync();

        var response = await host.Client.GetAsync(
            new Uri($"/instruments/{Guid.CreateVersion7()}/coverage", UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(
            Json, TestContext.Current.CancellationToken);

        Assert.Equal(404, problem?.Status);
    }

    [Fact]
    public async Task An_instrument_with_no_data_is_answered_with_every_dataset_absent()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var host = await CreateHostAsync();
        var venue = await host.AddExchangeAsync("CVNA");
        var instrument = await host.AddInstrumentAsync(venue, "CVN");

        var coverage = await GetCoverageAsync(host, instrument);

        Assert.Equal("CVN", coverage.Ticker);
        Assert.Equal("CVNA", coverage.Exchange);

        // The source serves daily bars and nothing finer: daily is collected
        // and absent, intraday is not collected at all.
        var daily = coverage.Prices[0];
        Assert.Equal("OneDay", daily.Interval);
        Assert.Equal("1d", daily.IntervalCode);
        Assert.Equal("NotHeld", daily.State);
        Assert.Equal(0, daily.Bars);
        Assert.Null(daily.MissingSessions);
        Assert.Null(daily.LastAttempt);
        Assert.All(coverage.Prices.Skip(1), price => Assert.Equal("NotCollected", price.State));

        Assert.Equal("NotHeld", coverage.CorporateActions.State);
        Assert.Equal("NotHeld", coverage.Fundamentals.State);
        Assert.Equal(0, coverage.Fundamentals.Reports);
        Assert.Equal("no report recorded", coverage.Fundamentals.Note);
    }

    [Fact]
    public async Task A_held_series_reports_its_extent_findings_attempt_and_missing_sessions()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var host = await CreateHostAsync();
        var venue = await host.AddExchangeAsync("CVHA", calendarFrom: new DateOnly(2026, 1, 1));
        var instrument = await host.AddInstrumentAsync(venue, "CVH");

        // Wednesday is a closure holding a bar anyway; Thursday traded and
        // holds nothing. Exactly one session is missing.
        host.Exchanges.AddHoliday(TradingHoliday.Record(venue, Monday.AddDays(2), "A closure", Now));
        host.Bars.AddRange(
        [
            Bar(instrument, Monday, "CVHB"),
            Bar(instrument, Monday.AddDays(1), DailySource.Code),
            Bar(instrument, Monday.AddDays(2), DailySource.Code),
            Bar(instrument, Monday.AddDays(4), DailySource.Code),
        ]);
        host.Issues.Add(DataQualityIssue.Raise(
            instrument,
            BarInterval.OneDay,
            Instant(Monday),
            DataQualityIssueKind.PriceLimitBreach,
            "A breach.",
            DataRules.ValidationVersion,
            Now));

        var older = IngestionRun.Start(Source, instrument, BarInterval.OneDay, Instant(Monday), Instant(Monday.AddDays(5)), Now.AddDays(-2));
        older.Fail("An outage.", 3, Now.AddDays(-2));
        var latest = IngestionRun.Start(Source, instrument, BarInterval.OneDay, Instant(Monday), Instant(Monday.AddDays(5)), Now.AddHours(-1));
        latest.Succeed(IngestionCounts.None, 1, null, Now);
        host.Journal.AddRun(older);
        host.Journal.AddRun(latest);

        await host.SaveAsync();

        var daily = (await GetCoverageAsync(host, instrument)).Prices[0];

        Assert.Equal("Held", daily.State);
        Assert.Equal(Monday, daily.FirstSession);
        Assert.Equal(Monday.AddDays(4), daily.LastSession);
        Assert.Equal(4, daily.Bars);
        Assert.Equal([DailySource.Code, "CVHB"], daily.Sources);
        Assert.Equal(1, daily.OpenFindings);
        Assert.Equal(4, daily.SessionsExpected);
        Assert.Equal(1, daily.MissingSessions);
        Assert.NotNull(daily.LastAttempt);
        Assert.Equal("Succeeded", daily.LastAttempt.Outcome);
        Assert.Equal(Now.AddHours(-1), daily.LastAttempt.StartedAtUtc);
    }

    [Fact]
    public async Task Missing_sessions_cross_the_wire_as_null_when_the_calendar_does_not_reach()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var host = await CreateHostAsync();
        var venue = await host.AddExchangeAsync("CVCA");
        var instrument = await host.AddInstrumentAsync(venue, "CVC");
        host.Bars.AddRange([Bar(instrument, Monday, DailySource.Code)]);
        await host.SaveAsync();

        var response = await host.Client.GetAsync(
            new Uri($"/instruments/{instrument.Value}/coverage", UriKind.Relative),
            TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var daily = document.RootElement.GetProperty("prices")[0];

        Assert.Equal("Held", daily.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, daily.GetProperty("missingSessions").ValueKind);
        Assert.Equal(JsonValueKind.Null, daily.GetProperty("sessionsExpected").ValueKind);
    }

    [Fact]
    public async Task Corporate_actions_are_summarised_with_the_undated_ones_counted()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var host = await CreateHostAsync();
        var venue = await host.AddExchangeAsync("CVAA");
        var instrument = await host.AddInstrumentAsync(venue, "CVA");

        var dated = CorporateAction.Record(
            instrument, CorporateActionType.CashDividend, new DateOnly(2025, 6, 2), null, 1_000m, Source, Now);
        dated.Schedule(null, null, new DateOnly(2025, 5, 1), Now);

        host.Actions.Add(dated);
        host.Actions.Add(CorporateAction.Record(
            instrument, CorporateActionType.CashDividend, new DateOnly(2026, 6, 1), null, 1_500m, Source, Now));
        await host.SaveAsync();

        var actions = (await GetCoverageAsync(host, instrument)).CorporateActions;

        Assert.Equal("Held", actions.State);
        Assert.Equal(2, actions.Count);
        Assert.Equal(1, actions.WithoutAnnouncementDate);
        Assert.Equal(new DateOnly(2025, 6, 2), actions.FirstExDate);
        Assert.Equal(new DateOnly(2026, 6, 1), actions.LastExDate);
    }

    [Fact]
    public async Task Universe_membership_keeps_not_held_and_not_measured_apart()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var host = await CreateHostAsync();
        var venue = await host.AddExchangeAsync("CVUA");
        var instrument = await host.AddInstrumentAsync(venue, "CVU");

        var member = host.DefineUniverse("CVUM", new DateOnly(2024, 1, 1));
        host.DefineUniverse("CVUN", new DateOnly(2024, 1, 1));
        host.DefineUniverse("CVUX", coverageFrom: null);
        await host.SaveAsync();

        var spell = UniverseMembership.Admit(
            member.Id, instrument, new DateOnly(2024, 1, 2), new DateOnly(2023, 12, 20), Source, Now);
        spell.Remove(new DateOnly(2025, 1, 6), new DateOnly(2024, 12, 20));
        host.Universes.Add(spell);
        await host.SaveAsync();

        var universes = (await GetCoverageAsync(host, instrument)).Universes;

        var held = Assert.Single(universes, universe => universe.Code == "CVUM");
        Assert.Equal("Held", held.State);
        var recorded = Assert.Single(held.Spells);
        Assert.Equal(new DateOnly(2024, 1, 2), recorded.From);
        Assert.Equal(new DateOnly(2025, 1, 6), recorded.To);
        Assert.Equal(new DateOnly(2023, 12, 20), recorded.AnnouncedOn);
        Assert.Equal(new DateOnly(2024, 12, 20), recorded.RemovalAnnouncedOn);

        var absent = Assert.Single(universes, universe => universe.Code == "CVUN");
        Assert.Equal("NotHeld", absent.State);
        Assert.Equal(new DateOnly(2024, 1, 1), absent.DeclaredFrom);
        Assert.Null(absent.DeclaredUntil);

        var unknown = Assert.Single(universes, universe => universe.Code == "CVUX");
        Assert.Equal("NotMeasured", unknown.State);
        Assert.Null(unknown.DeclaredFrom);
    }

    private static async Task<InstrumentCoverageResponse> GetCoverageAsync(
        CoverageHost host,
        InstrumentId instrument)
    {
        var response = await host.Client.GetAsync(
            new Uri($"/instruments/{instrument.Value}/coverage", UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<InstrumentCoverageResponse>(
            Json, TestContext.Current.CancellationToken);

        Assert.NotNull(payload);
        return payload;
    }

    private static OhlcvBar Bar(InstrumentId instrument, DateOnly session, string source) =>
        OhlcvBar.Record(
            instrument,
            BarInterval.OneDay,
            Instant(session),
            Price.Create(100m),
            Price.Create(100m),
            Price.Create(100m),
            Price.Create(100m),
            1_000,
            null,
            SourceCode.Create(source),
            Now);

    private static DateTimeOffset Instant(DateOnly session) =>
        new(session.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    private async Task<CoverageHost> CreateHostAsync()
    {
        var factory = PersonalQuantApiFactory.WithMarketDataProvider(
            containers.Postgres,
            containers.Redis,
            new DailySource());

        var host = new CoverageHost(factory);

        // Forces host start-up, which runs the migration hosted service.
        _ = await host.Client.GetAsync(
            new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        return host;
    }

    /// <summary>
    /// A registered source serving daily bars only, so the registry has
    /// something collected and something not.
    /// </summary>
    private sealed class DailySource : IMarketDataProvider
    {
        public const string Code = "CVDS";

        SourceCode IMarketDataProvider.Code { get; } = SourceCode.Create(Code);

        public ProviderCapability Capability { get; } = new()
        {
            Code = SourceCode.Create(Code),
            DisplayName = "Coverage test daily source",
            Intervals = new HashSet<BarInterval> { BarInterval.OneDay },
            ReportedFields = new ProviderReportedFields(),
            Limitations = new ProviderLimitations(),
        };

        public Task<MarketDataFetchResult> FetchBarsAsync(
            MarketDataRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Coverage never fetches.");
    }

    /// <summary>
    /// A host, a DI scope and the repositories a test seeds through.
    /// </summary>
    private sealed class CoverageHost : IAsyncDisposable
    {
        private readonly PersonalQuantApiFactory _factory;
        private readonly AsyncServiceScope _scope;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IInstrumentRepository _instruments;

        public CoverageHost(PersonalQuantApiFactory factory)
        {
            _factory = factory;
            Client = factory.CreateClient();
            _scope = factory.Services.CreateAsyncScope();

            var services = _scope.ServiceProvider;
            _unitOfWork = services.GetRequiredService<IUnitOfWork>();
            _instruments = services.GetRequiredService<IInstrumentRepository>();
            Exchanges = services.GetRequiredService<IExchangeRepository>();
            Bars = services.GetRequiredService<IBarRepository>();
            Issues = services.GetRequiredService<IDataQualityRepository>();
            Journal = services.GetRequiredService<IIngestionJournal>();
            Actions = services.GetRequiredService<ICorporateActionRepository>();
            Universes = services.GetRequiredService<IUniverseRepository>();
        }

        public HttpClient Client { get; }

        public IExchangeRepository Exchanges { get; }

        public IBarRepository Bars { get; }

        public IDataQualityRepository Issues { get; }

        public IIngestionJournal Journal { get; }

        public ICorporateActionRepository Actions { get; }

        public IUniverseRepository Universes { get; }

        public async Task<ExchangeId> AddExchangeAsync(string code, DateOnly? calendarFrom = null)
        {
            var exchange = Exchange.Register(
                ExchangeCode.Create(code), $"Venue {code}", "Asia/Ho_Chi_Minh", Now);

            if (calendarFrom is { } from)
            {
                exchange.DeclareCalendarCoverage(CalendarCoverage.Create(from, new DateOnly(2027, 1, 1)), Now);
            }

            Exchanges.Add(exchange);
            await SaveAsync();

            return exchange.Id;
        }

        public async Task<InstrumentId> AddInstrumentAsync(ExchangeId venue, string ticker)
        {
            var instrument = Instrument.Register(
                venue, Ticker.Create(ticker), $"{ticker} Test Company", AssetType.Equity, CurrencyCode.Vnd, Now);

            instrument.List(Now);
            _instruments.Add(instrument);
            await SaveAsync();

            return instrument.Id;
        }

        public Universe DefineUniverse(string code, DateOnly? coverageFrom)
        {
            var universe = Universe.Define(
                UniverseId.New(), UniverseCode.Create(code), $"{code} Test Universe", UniverseKind.Index, Source, Now);

            if (coverageFrom is { } from)
            {
                universe.DeclareCoverage(MembershipCoverage.Create(from, null), Source, Now);
            }

            Universes.Add(universe);

            return universe;
        }

        public Task<int> SaveAsync() => _unitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _scope.DisposeAsync();
            await _factory.DisposeAsync();
        }
    }
}
