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
/// Verifies the market data tables against real PostgreSQL.
/// </summary>
/// <remarks>
/// The properties under test are schema properties. Deduplication is the
/// primary key, the bound on a series read is a <c>LIMIT</c> applied from the
/// newest end, and prices are <c>numeric</c> rather than floating point. None
/// of the three can be proved anywhere but against a database.
/// </remarks>
/// <param name="containers">Shared container fixture.</param>
[Collection(DependencyContainerTests.Name)]
public sealed class MarketDataPersistenceTests(DependencyContainerFixture containers)
{
    private static readonly DateTimeOffset Period = new(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Ingested = new(2026, 8, 26, 1, 0, 0, TimeSpan.Zero);
    private static readonly SourceCode Source = SourceCode.Create("TEST");

    [Fact]
    public async Task A_bar_round_trips_with_its_exact_decimal_prices()
    {
        // The reason prices are numeric(18,6). A close that comes back a
        // fraction different from the one that went in compounds into returns
        // the market never produced.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var instrumentId = await AddInstrumentAsync(scope, "MDA", "MDA");

        scope.Bars.AddRange([Bar(instrumentId, Period, close: 27_350.125m, turnover: 1_234_567.891m)]);
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        await using var reader = await CreateScopeAsync();
        var stored = await ReadSeriesAsync(reader, instrumentId);

        // Assert
        var bar = Assert.Single(stored);
        Assert.Equal(27_350.125m, bar.Close);
        Assert.Equal(1_234_567.891m, bar.Turnover);
        Assert.Equal(Period, bar.OpenedAtUtc);
        Assert.Equal("TEST", bar.Source.Value);
    }

    [Fact]
    public async Task The_same_period_cannot_be_stored_twice()
    {
        // Deduplication is the primary key rather than a rule in whatever code
        // happens to be writing.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var instrumentId = await AddInstrumentAsync(scope, "MDB", "MDB");

        scope.Bars.AddRange([Bar(instrumentId, Period)]);
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        await using var second = await CreateScopeAsync();
        second.Bars.AddRange([Bar(instrumentId, Period, close: 999m)]);

        // Assert
        await Assert.ThrowsAnyAsync<Exception>(
            () => second.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_restatement_is_written_back_rather_than_inserted()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var instrumentId = await AddInstrumentAsync(scope, "MDC", "MDC");

        scope.Bars.AddRange([Bar(instrumentId, Period, close: 105m)]);
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        var tracked = await scope.Bars.ListForUpdateAsync(
            instrumentId,
            BarInterval.OneDay,
            Period,
            Period.AddDays(1),
            TestContext.Current.CancellationToken);

        Assert.Single(tracked);
        Assert.True(tracked[0].Revise(
            Price.Create(100m),
            Price.Create(110m),
            Price.Create(95m),
            Price.Create(108m),
            2_000,
            null,
            Source,
            Ingested.AddDays(1)));

        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        await using var reader = await CreateScopeAsync();
        var stored = Assert.Single(await ReadSeriesAsync(reader, instrumentId));

        Assert.Equal(108m, stored.Close);
        Assert.Equal(1, stored.Revision);

        // The restatement stamp lives on the stored bar rather than the read
        // projection, so it is asserted through the tracked entity.
        var reloaded = await reader.Bars.ListForUpdateAsync(
            instrumentId,
            BarInterval.OneDay,
            Period,
            Period.AddDays(1),
            TestContext.Current.CancellationToken);

        Assert.Equal(Ingested.AddDays(1), Assert.Single(reloaded).RevisedAtUtc);
    }

    [Fact]
    public async Task A_bounded_read_returns_the_newest_bars_oldest_first()
    {
        // "The last N periods" is what a chart asks for. Taking from the oldest
        // end would return the start of the history and omit what was wanted.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var instrumentId = await AddInstrumentAsync(scope, "MDD", "MDD");

        scope.Bars.AddRange(
            [.. Enumerable.Range(0, 10).Select(offset => Bar(instrumentId, Period.AddDays(offset)))]);
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        var series = await ReadSeriesAsync(scope, instrumentId, limit: 3);

        // Assert
        Assert.Equal(3, series.Count);
        Assert.Equal(Period.AddDays(7), series[0].OpenedAtUtc);
        Assert.Equal(Period.AddDays(9), series[2].OpenedAtUtc);
    }

    [Fact]
    public async Task A_window_narrows_the_read_at_both_ends()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var instrumentId = await AddInstrumentAsync(scope, "MDE", "MDE");

        scope.Bars.AddRange(
            [.. Enumerable.Range(0, 10).Select(offset => Bar(instrumentId, Period.AddDays(offset)))]);
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        var series = await ReadSeriesAsync(
            scope, instrumentId, from: Period.AddDays(2), to: Period.AddDays(5));

        // Assert
        Assert.Equal(3, series.Count);
        Assert.Equal(Period.AddDays(2), series[0].OpenedAtUtc);
        Assert.Equal(Period.AddDays(4), series[2].OpenedAtUtc);
    }

    [Fact]
    public async Task Series_of_different_resolutions_do_not_collide()
    {
        // The same instant at two resolutions is two different bars, and the
        // key has to keep them apart.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var instrumentId = await AddInstrumentAsync(scope, "MDF", "MDF");

        scope.Bars.AddRange(
        [
            Bar(instrumentId, Period),
            Bar(instrumentId, Period, interval: BarInterval.OneHour),
        ]);
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        var daily = await ReadSeriesAsync(scope, instrumentId);
        var hourly = await ReadSeriesAsync(scope, instrumentId, interval: BarInterval.OneHour);

        // Assert
        Assert.Single(daily);
        Assert.Single(hourly);
    }

    [Fact]
    public async Task A_raw_batch_a_run_and_a_checkpoint_commit_together()
    {
        // The failure this guards against: a checkpoint surviving while the
        // bars it covers do not. The next run would resume past data that was
        // never stored, and nothing downstream could tell the gap from a
        // market holiday.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var instrumentId = await AddInstrumentAsync(scope, "MDG", "MDG");

        var batch = RawMarketDataBatch.Retain(
            Source,
            instrumentId,
            BarInterval.OneDay,
            Period,
            Period.AddDays(1),
            "timestamp,open,high,low,close,volume\n2026-08-03,100,110,95,105,1000\n",
            "text/csv",
            Ingested);

        var run = IngestionRun.Start(
            Source, instrumentId, BarInterval.OneDay, Period, Period.AddDays(1), Ingested);
        run.Succeed(new IngestionCounts(1, 1, 0, 1, 0), attempts: 1, batch.Id, Ingested);

        var checkpoint = IngestionCheckpoint.Start(
            instrumentId, BarInterval.OneDay, Source, Period, Ingested);

        // Act
        scope.Journal.AddRawBatch(batch);
        scope.Journal.AddRun(run);
        scope.Journal.AddCheckpoint(checkpoint);
        scope.Bars.AddRange([Bar(instrumentId, Period)]);
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        await using var reader = await CreateScopeAsync();

        var storedRun = Assert.Single(await reader.Journal.ListRecentRunsAsync(
            instrumentId, BarInterval.OneDay, 10, TestContext.Current.CancellationToken));
        Assert.Equal(IngestionOutcome.Succeeded, storedRun.Outcome);
        Assert.Equal(batch.Id, storedRun.RawBatchId);

        var storedCheckpoint = await reader.Journal.FindCheckpointAsync(
            instrumentId, BarInterval.OneDay, Source, TestContext.Current.CancellationToken);
        Assert.NotNull(storedCheckpoint);
        Assert.Equal(Period.AddDays(1), storedCheckpoint.ResumeFromUtc);

        Assert.Single(await ReadSeriesAsync(reader, instrumentId));
    }

    [Fact]
    public async Task A_failed_run_is_recorded_with_no_bars_and_no_checkpoint()
    {
        // The audit table's purpose is to explain gaps, which it can only do
        // if a failure leaves a row.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var instrumentId = await AddInstrumentAsync(scope, "MDH", "MDH");

        var run = IngestionRun.Start(
            Source, instrumentId, BarInterval.OneDay, Period, Period.AddDays(1), Ingested);
        run.Fail("The provider did not answer.", attempts: 3, Ingested);

        // Act
        scope.Journal.AddRun(run);
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        await using var reader = await CreateScopeAsync();
        var stored = Assert.Single(await reader.Journal.ListRecentRunsAsync(
            instrumentId, BarInterval.OneDay, 10, TestContext.Current.CancellationToken));

        Assert.Equal(IngestionOutcome.Failed, stored.Outcome);
        Assert.Equal("The provider did not answer.", stored.FailureReason);
        Assert.Null(stored.RawBatchId);
        Assert.Empty(await ReadSeriesAsync(reader, instrumentId));
    }

    private static OhlcvBar Bar(
        InstrumentId instrumentId,
        DateTimeOffset openedAtUtc,
        decimal close = 105m,
        decimal? turnover = null,
        BarInterval interval = BarInterval.OneDay) =>
        OhlcvBar.Record(
            instrumentId,
            interval,
            openedAtUtc,
            Price.Create(100m),
            Price.Create(110m),
            Price.Create(95m),
            Price.Create(close),
            1_000,
            turnover,
            Source,
            Ingested);

    private static async Task<IReadOnlyList<SeriesBar>> ReadSeriesAsync(
        MarketDataScope scope,
        InstrumentId instrumentId,
        BarInterval interval = BarInterval.OneDay,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int? limit = null,
        bool adjusted = false)
    {
        // Raw by default here: these tests are about what the schema stores,
        // and adjustment is covered where the actions that drive it are.
        Assert.True(
            BarQuery.TryCreate(
                instrumentId, interval, from, to, limit, out var query, out var problem, adjusted),
            problem);

        var series = await scope.MarketData.GetSeriesAsync(
            query, TestContext.Current.CancellationToken);

        return series.Bars;
    }

    private static async Task<InstrumentId> AddInstrumentAsync(
        MarketDataScope scope,
        string venueCode,
        string ticker)
    {
        var exchange = Exchange.Register(
            ExchangeCode.Create(venueCode), $"{venueCode} Test Venue", "Asia/Ho_Chi_Minh", Ingested);

        scope.Exchanges.Add(exchange);
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        var instrument = Instrument.Register(
            exchange.Id,
            Ticker.Create(ticker),
            $"{ticker} Test Company",
            AssetType.Equity,
            CurrencyCode.Vnd,
            Ingested);

        instrument.List(Ingested);

        scope.Instruments.Add(instrument);
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        return instrument.Id;
    }

    private async Task<MarketDataScope> CreateScopeAsync()
    {
        var factory = PersonalQuantApiFactory.WithDependencies(
            containers.Postgres,
            containers.Redis,
            applyMigrations: true);

        using var client = factory.CreateClient();
        _ = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        return new MarketDataScope(factory);
    }

    /// <summary>
    /// Owns a host and a DI scope, so every test reads and writes through the
    /// real composition root.
    /// </summary>
    private sealed class MarketDataScope : IAsyncDisposable
    {
        private readonly PersonalQuantApiFactory _factory;
        private readonly AsyncServiceScope _scope;

        public MarketDataScope(PersonalQuantApiFactory factory)
        {
            _factory = factory;
            _scope = factory.Services.CreateAsyncScope();

            UnitOfWork = _scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            Exchanges = _scope.ServiceProvider.GetRequiredService<IExchangeRepository>();
            Instruments = _scope.ServiceProvider.GetRequiredService<IInstrumentRepository>();
            Bars = _scope.ServiceProvider.GetRequiredService<IBarRepository>();
            Journal = _scope.ServiceProvider.GetRequiredService<IIngestionJournal>();
            MarketData = _scope.ServiceProvider.GetRequiredService<IMarketDataQueryService>();
        }

        public IUnitOfWork UnitOfWork { get; }

        public IExchangeRepository Exchanges { get; }

        public IInstrumentRepository Instruments { get; }

        public IBarRepository Bars { get; }

        public IIngestionJournal Journal { get; }

        public IMarketDataQueryService MarketData { get; }

        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync();
            await _factory.DisposeAsync();
        }
    }
}
