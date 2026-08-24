using Microsoft.Extensions.DependencyInjection;
using PersonalQuant.Application.Abstractions;
using PersonalQuant.Application.Exchanges;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Domain.Currencies;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.IntegrationTests;

/// <summary>
/// Verifies the quality tables and the scoring reads against real PostgreSQL.
/// </summary>
/// <remarks>
/// The properties under test are schema properties and aggregate queries: one
/// finding per session and kind is a unique index, the ingestion summary is a
/// grouped query, and the calendar horizon is an ordered read. None can be
/// proved anywhere but against a database.
/// </remarks>
/// <param name="containers">Shared container fixture.</param>
[Collection(DependencyContainerTests.Name)]
public sealed class DataQualityPersistenceTests(DependencyContainerFixture containers)
{
    private static readonly DateTimeOffset Monday = new(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 1, 0, 0, TimeSpan.Zero);
    private static readonly SourceCode Source = SourceCode.Create("TEST");

    [Fact]
    public async Task A_finding_round_trips_with_its_lifecycle()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var instrumentId = await AddInstrumentAsync(scope, "DQA", "DQA");

        var issue = DataQualityIssue.Raise(
            instrumentId,
            BarInterval.OneDay,
            Monday,
            DataQualityIssueKind.PriceLimitBreach,
            "The close moved -50%, beyond the band.",
            DataRules.ValidationVersion,
            Now);

        scope.Issues.Add(issue);
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        await using var resolver = await CreateScopeAsync();
        var tracked = await resolver.Issues.FindAsync(issue.Id, TestContext.Current.CancellationToken);
        tracked!.Explain("A two-for-one split on 2026-08-03.", Now.AddDays(1));
        await resolver.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        await using var reader = await CreateScopeAsync();
        var open = await reader.Issues.ListOpenAsync(
            instrumentId, BarInterval.OneDay, 10, TestContext.Current.CancellationToken);

        Assert.Empty(open);

        var all = await reader.Issues.ListAsync(
            instrumentId,
            BarInterval.OneDay,
            Monday,
            Monday.AddDays(7),
            TestContext.Current.CancellationToken);

        var stored = Assert.Single(all);
        Assert.Equal(DataQualityIssueStatus.Explained, stored.Status);
        Assert.Contains("split", stored.Resolution, StringComparison.Ordinal);
    }

    [Fact]
    public async Task One_session_and_kind_may_hold_only_one_finding()
    {
        // Without this a nightly run raises the same finding every night, and
        // Monday's dismissal is buried by Friday.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var instrumentId = await AddInstrumentAsync(scope, "DQB", "DQB");

        scope.Issues.Add(Issue(instrumentId, DataQualityIssueKind.MissingSession));
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        await using var clash = await CreateScopeAsync();
        clash.Issues.Add(Issue(instrumentId, DataQualityIssueKind.MissingSession));

        // Assert
        await Assert.ThrowsAnyAsync<Exception>(
            () => clash.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Two_kinds_may_concern_the_same_session()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var instrumentId = await AddInstrumentAsync(scope, "DQC", "DQC");

        scope.Issues.Add(Issue(instrumentId, DataQualityIssueKind.PriceLimitBreach));
        scope.Issues.Add(Issue(instrumentId, DataQualityIssueKind.UnexpectedSession));

        // Act
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        var counts = await scope.Issues.CountOpenByKindAsync(
            instrumentId,
            BarInterval.OneDay,
            Monday,
            Monday.AddDays(7),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, counts.Count);
        Assert.Equal(1, counts[DataQualityIssueKind.PriceLimitBreach]);
    }

    [Fact]
    public async Task A_venue_reports_how_far_its_calendar_reaches()
    {
        // A window with no closures in range and one the calendar never
        // covered are indistinguishable without this.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var venue = await AddExchangeAsync(scope, "DQCAL", dailyPriceLimitPercent: 7m);

        var before = await scope.Exchanges.FindCalendarHorizonAsync(
            venue, TestContext.Current.CancellationToken);

        Assert.Null(before);

        scope.Exchanges.AddHoliday(
            TradingHoliday.Record(venue, new DateOnly(2026, 9, 2), "National Day", Now));
        scope.Exchanges.AddHoliday(
            TradingHoliday.Record(venue, new DateOnly(2026, 12, 31), "Calendar horizon", Now));
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        await using var reader = await CreateScopeAsync();
        var horizon = await reader.Exchanges.FindCalendarHorizonAsync(
            venue, TestContext.Current.CancellationToken);

        var window = await reader.Calendar.LoadAsync(
            venue,
            new DateOnly(2026, 8, 31),
            new DateOnly(2026, 9, 4),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new DateOnly(2026, 12, 31), horizon);
        Assert.True(window.IsComplete);
        Assert.False(window.IsTradingDay(new DateOnly(2026, 9, 2)));
        Assert.Equal(4, window.TradingDays().Count());
    }

    [Fact]
    public async Task A_venue_records_its_daily_price_limit()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var venue = await AddExchangeAsync(scope, "DQLIM", dailyPriceLimitPercent: 10m);

        await using var reader = await CreateScopeAsync();
        var exchange = await reader.Exchanges.FindByIdAsync(
            venue, TestContext.Current.CancellationToken);

        Assert.NotNull(exchange);
        Assert.Equal(10m, exchange.DailyPriceLimit!.Value.Percent);
    }

    [Fact]
    public async Task A_venue_with_no_recorded_limit_reads_back_as_having_none()
    {
        // Not the same as having no limit, and the null is what keeps the
        // check from being run against a guess.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var venue = await AddExchangeAsync(scope, "DQNUL", dailyPriceLimitPercent: null);

        await using var reader = await CreateScopeAsync();
        var exchange = await reader.Exchanges.FindByIdAsync(
            venue, TestContext.Current.CancellationToken);

        Assert.Null(exchange!.DailyPriceLimit);
    }

    [Fact]
    public async Task The_ingestion_summary_is_aggregated_over_the_window()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var instrumentId = await AddInstrumentAsync(scope, "DQD", "DQD");

        var succeeded = IngestionRun.Start(
            Source, instrumentId, BarInterval.OneDay, Monday, Monday.AddDays(1), Monday);
        succeeded.Succeed(new IngestionCounts(10, 9, 1, 9, 0), attempts: 1, null, Monday);

        var failed = IngestionRun.Start(
            Source, instrumentId, BarInterval.OneDay, Monday, Monday.AddDays(1), Monday.AddDays(1));
        failed.Fail("The provider did not answer.", attempts: 3, Monday.AddDays(1));

        var skipped = IngestionRun.Start(
            Source, instrumentId, BarInterval.OneDay, Monday, Monday.AddDays(1), Monday.AddDays(2));
        skipped.Skip("Nothing new.", Monday.AddDays(2));

        scope.Journal.AddRun(succeeded);
        scope.Journal.AddRun(failed);
        scope.Journal.AddRun(skipped);
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        await using var reader = await CreateScopeAsync();
        var summary = await reader.Journal.SummariseRunsAsync(
            instrumentId,
            BarInterval.OneDay,
            Monday,
            Monday.AddDays(7),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, summary.Runs);
        Assert.Equal(1, summary.Succeeded);
        Assert.Equal(1, summary.Failed);
        Assert.Equal(1, summary.Skipped);
        Assert.Equal(10, summary.BarsFetched);
        Assert.Equal(9, summary.BarsAccepted);
        Assert.Equal(1, summary.BarsRejected);
    }

    [Fact]
    public async Task A_window_with_no_runs_summarises_to_nothing()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var instrumentId = await AddInstrumentAsync(scope, "DQE", "DQE");

        var summary = await scope.Journal.SummariseRunsAsync(
            instrumentId,
            BarInterval.OneDay,
            Monday,
            Monday.AddDays(7),
            TestContext.Current.CancellationToken);

        Assert.Equal(0, summary.Runs);
    }

    [Fact]
    public async Task A_score_reports_that_completeness_is_unmeasured_without_a_calendar()
    {
        // The field a dashboard must read first: a deployment that has not
        // imported a calendar gets a completeness figure that means nothing.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var instrumentId = await AddInstrumentAsync(scope, "DQF", "DQF");

        var report = await scope.Quality.ScoreAsync(
            instrumentId,
            BarInterval.OneDay,
            new DateOnly(2026, 8, 3),
            new DateOnly(2026, 8, 7),
            TestContext.Current.CancellationToken);

        Assert.NotNull(report);
        Assert.False(report.CalendarIsComplete);
        Assert.Equal(0, report.SessionsExpected);
    }

    [Fact]
    public async Task A_score_over_a_complete_week_measures_completeness()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var venue = await AddExchangeAsync(scope, "DQSCO", dailyPriceLimitPercent: 7m);
        var instrumentId = await AddInstrumentAsync(scope, venue, "DQG");

        scope.Exchanges.AddHoliday(
            TradingHoliday.Record(venue, new DateOnly(2026, 12, 31), "Calendar horizon", Now));

        // Four of the week's five sessions.
        scope.Bars.AddRange(
        [
            Bar(instrumentId, Monday),
            Bar(instrumentId, Monday.AddDays(1)),
            Bar(instrumentId, Monday.AddDays(2)),
            Bar(instrumentId, Monday.AddDays(3)),
        ]);

        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        await using var reader = await CreateScopeAsync();
        var report = await reader.Quality.ScoreAsync(
            instrumentId,
            BarInterval.OneDay,
            new DateOnly(2026, 8, 3),
            new DateOnly(2026, 8, 7),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(report);
        Assert.True(report.CalendarIsComplete);
        Assert.Equal(5, report.SessionsExpected);
        Assert.Equal(4, report.BarsStored);
        Assert.Equal(0.8m, report.Score.Completeness);

        // Nothing has run the rules over them yet.
        Assert.Equal(4, report.UnvalidatedBars);
    }

    [Fact]
    public async Task An_unknown_instrument_has_no_score()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();

        var report = await scope.Quality.ScoreAsync(
            InstrumentId.New(),
            BarInterval.OneDay,
            new DateOnly(2026, 8, 3),
            new DateOnly(2026, 8, 7),
            TestContext.Current.CancellationToken);

        Assert.Null(report);
    }

    private static DataQualityIssue Issue(InstrumentId instrumentId, DataQualityIssueKind kind) =>
        DataQualityIssue.Raise(
            instrumentId,
            BarInterval.OneDay,
            Monday,
            kind,
            $"A {kind} finding.",
            DataRules.ValidationVersion,
            Now);

    private static OhlcvBar Bar(InstrumentId instrumentId, DateTimeOffset openedAtUtc) =>
        OhlcvBar.Record(
            instrumentId,
            BarInterval.OneDay,
            openedAtUtc,
            Price.Create(100m),
            Price.Create(100m),
            Price.Create(100m),
            Price.Create(100m),
            1_000,
            null,
            Source,
            Now);

    private static async Task<ExchangeId> AddExchangeAsync(
        QualityScope scope,
        string code,
        decimal? dailyPriceLimitPercent)
    {
        var exchange = Exchange.Register(
            ExchangeCode.Create(code),
            $"{code} Test Venue",
            "Asia/Ho_Chi_Minh",
            Now,
            mic: null,
            dailyPriceLimitPercent is { } percent ? PriceLimit.FromPercent(percent) : null);

        scope.Exchanges.Add(exchange);
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        return exchange.Id;
    }

    private static async Task<InstrumentId> AddInstrumentAsync(
        QualityScope scope,
        string venueCode,
        string ticker)
    {
        var venue = await AddExchangeAsync(scope, venueCode, dailyPriceLimitPercent: 7m);

        return await AddInstrumentAsync(scope, venue, ticker);
    }

    private static async Task<InstrumentId> AddInstrumentAsync(
        QualityScope scope,
        ExchangeId venue,
        string ticker)
    {
        var instrument = Instrument.Register(
            venue,
            Ticker.Create(ticker),
            $"{ticker} Test Company",
            AssetType.Equity,
            CurrencyCode.Vnd,
            Now);

        instrument.List(Now);

        scope.Instruments.Add(instrument);
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        return instrument.Id;
    }

    private async Task<QualityScope> CreateScopeAsync()
    {
        var factory = PersonalQuantApiFactory.WithDependencies(
            containers.Postgres,
            containers.Redis,
            applyMigrations: true);

        using var client = factory.CreateClient();
        _ = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        return new QualityScope(factory);
    }

    /// <summary>
    /// Owns a host and a DI scope, so every test reads and writes through the
    /// real composition root.
    /// </summary>
    private sealed class QualityScope : IAsyncDisposable
    {
        private readonly PersonalQuantApiFactory _factory;
        private readonly AsyncServiceScope _scope;

        public QualityScope(PersonalQuantApiFactory factory)
        {
            _factory = factory;
            _scope = factory.Services.CreateAsyncScope();

            UnitOfWork = _scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            Exchanges = _scope.ServiceProvider.GetRequiredService<IExchangeRepository>();
            Instruments = _scope.ServiceProvider.GetRequiredService<IInstrumentRepository>();
            Bars = _scope.ServiceProvider.GetRequiredService<IBarRepository>();
            Issues = _scope.ServiceProvider.GetRequiredService<IDataQualityRepository>();
            Journal = _scope.ServiceProvider.GetRequiredService<IIngestionJournal>();
            Calendar = _scope.ServiceProvider.GetRequiredService<ITradingCalendar>();
            Quality = _scope.ServiceProvider.GetRequiredService<IDataQualityService>();
        }

        public IUnitOfWork UnitOfWork { get; }

        public IExchangeRepository Exchanges { get; }

        public IInstrumentRepository Instruments { get; }

        public IBarRepository Bars { get; }

        public IDataQualityRepository Issues { get; }

        public IIngestionJournal Journal { get; }

        public ITradingCalendar Calendar { get; }

        public IDataQualityService Quality { get; }

        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync();
            await _factory.DisposeAsync();
        }
    }
}
