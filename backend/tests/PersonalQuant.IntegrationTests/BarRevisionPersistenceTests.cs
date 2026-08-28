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
/// Verifies point-in-time reads of the market data series against real
/// PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// The properties under test are schema and query properties. The half-open
/// observation window is a <c>WHERE</c> clause, the guarantee that exactly one
/// statement matches any instant is a uniqueness property of that clause, and
/// the refusal to fall back to the current value when an as-of predates the
/// first observation is the behaviour of a join that is simply not there.
/// None of the three can be proved anywhere but against a database.
/// </para>
/// <para>
/// The corporate-action layer is deliberately not exercised: these read raw
/// series. Prices are point-in-time here; the adjustment applied over them is
/// not yet filtered by announcement date, which is U4's work and is recorded in
/// ADR-018.
/// </para>
/// </remarks>
/// <param name="containers">Shared container fixture.</param>
[Collection(DependencyContainerTests.Name)]
public sealed class BarRevisionPersistenceTests(DependencyContainerFixture containers)
{
    private static readonly DateTimeOffset Period = new(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The instant the original observation was made.</summary>
    private static readonly DateTimeOffset T1 = new(2026, 8, 26, 1, 0, 0, TimeSpan.Zero);

    /// <summary>The instant the correction was observed.</summary>
    private static readonly DateTimeOffset T3 = new(2026, 8, 27, 1, 0, 0, TimeSpan.Zero);

    private static readonly SourceCode Source = SourceCode.Create("TEST");

    [Fact]
    public async Task A_corrected_bar_reads_back_as_it_stood_at_each_instant()
    {
        // The acceptance scenario for point-in-time reads.
        //
        //   T0  provider reports  Close = 100
        //   T1  PQT observes it
        //   T2  provider corrects Close = 101
        //   T3  PQT observes the correction
        //
        // A backtest run as of T1 must see 100. It cannot be allowed to see a
        // correction that was published after the decision it is simulating.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var instrumentId = await AddInstrumentAsync(scope, "PIT", "PITA");

        await ObserveAsync(scope, instrumentId, close: 100m, at: T1);
        await ReviseAsync(scope, instrumentId, close: 101m, at: T3);

        // Act
        await using var reader = await CreateScopeAsync();
        var atT1 = await ReadAsOfAsync(reader, instrumentId, T1);
        var atT3 = await ReadAsOfAsync(reader, instrumentId, T3);
        var current = await ReadCurrentAsync(reader, instrumentId);

        // Assert
        Assert.Equal(100m, Assert.Single(atT1).Close);
        Assert.Equal(101m, Assert.Single(atT3).Close);
        Assert.Equal(101m, Assert.Single(current).Close);
    }

    [Fact]
    public async Task An_as_of_before_the_first_observation_returns_nothing()
    {
        // Never the current value. A period the system had not yet seen is
        // absent, and filling it in from today's row is precisely the leak
        // point-in-time reads exist to close.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var instrumentId = await AddInstrumentAsync(scope, "PIB", "PITB");

        await ObserveAsync(scope, instrumentId, close: 100m, at: T1);

        // Act
        await using var reader = await CreateScopeAsync();
        var before = await ReadAsOfAsync(reader, instrumentId, T1.AddTicks(-1));
        var atOpen = await ReadAsOfAsync(reader, instrumentId, T1);

        // Assert
        Assert.Empty(before);
        Assert.Single(atOpen);
    }

    [Fact]
    public async Task The_instant_before_a_correction_still_reads_the_old_value()
    {
        // The exclusive upper bound, proved at the tick. The closing edge of a
        // window belongs to its successor.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var instrumentId = await AddInstrumentAsync(scope, "PIC", "PITC");

        await ObserveAsync(scope, instrumentId, close: 100m, at: T1);
        await ReviseAsync(scope, instrumentId, close: 101m, at: T3);

        // Act
        await using var reader = await CreateScopeAsync();
        var justBefore = await ReadAsOfAsync(reader, instrumentId, T3.AddTicks(-1));
        var exactly = await ReadAsOfAsync(reader, instrumentId, T3);

        // Assert
        Assert.Equal(100m, Assert.Single(justBefore).Close);
        Assert.Equal(101m, Assert.Single(exactly).Close);
    }

    [Fact]
    public async Task Every_instant_across_several_corrections_has_exactly_one_answer()
    {
        // Three statements of one period. Whatever instant is asked for, the
        // query must return one row and never two — the guarantee that makes an
        // as-of series well defined at all.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var instrumentId = await AddInstrumentAsync(scope, "PID", "PITD");

        var second = T1.AddDays(1);
        var third = T1.AddDays(2);

        await ObserveAsync(scope, instrumentId, close: 100m, at: T1);
        await ReviseAsync(scope, instrumentId, close: 101m, at: second);
        await ReviseAsync(scope, instrumentId, close: 102m, at: third);

        await using var reader = await CreateScopeAsync();

        var expected = new (DateTimeOffset At, decimal? Close)[]
        {
            (T1.AddTicks(-1), null),
            (T1, 100m),
            (second.AddTicks(-1), 100m),
            (second, 101m),
            (third.AddTicks(-1), 101m),
            (third, 102m),
            (third.AddYears(1), 102m),
        };

        foreach (var (at, close) in expected)
        {
            // Act
            var series = await ReadAsOfAsync(reader, instrumentId, at);

            // Assert
            if (close is null)
            {
                Assert.Empty(series);
                continue;
            }

            Assert.Equal(close, Assert.Single(series).Close);
        }
    }

    [Fact]
    public async Task The_current_series_ignores_the_history_entirely()
    {
        // The regression guarantee in one test: with no as-of, the read is the
        // one it always was, against the same table, returning the same row.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var instrumentId = await AddInstrumentAsync(scope, "PIE", "PITE");

        await ObserveAsync(scope, instrumentId, close: 100m, at: T1);
        await ReviseAsync(scope, instrumentId, close: 101m, at: T3);

        // Act
        await using var reader = await CreateScopeAsync();
        var current = await ReadCurrentAsync(reader, instrumentId);

        // Assert
        var bar = Assert.Single(current);
        Assert.Equal(101m, bar.Close);
        Assert.Equal(1, bar.Revision);
    }

    [Fact]
    public async Task The_open_window_and_the_stored_bar_agree()
    {
        // Two records of one fact. A disagreement would make every as-of answer
        // suspect, and the invariant is cheap enough to assert directly.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var instrumentId = await AddInstrumentAsync(scope, "PIF", "PITF");

        await ObserveAsync(scope, instrumentId, close: 100m, at: T1);
        await ReviseAsync(scope, instrumentId, close: 101m, at: T3);

        await using var reader = await CreateScopeAsync();

        var bars = await reader.Bars.ListForUpdateAsync(
            instrumentId,
            BarInterval.OneDay,
            Period,
            Period.AddDays(1),
            TestContext.Current.CancellationToken);

        var open = await reader.Bars.ListOpenRevisionsForUpdateAsync(
            instrumentId,
            BarInterval.OneDay,
            Period,
            Period.AddDays(1),
            TestContext.Current.CancellationToken);

        // Assert
        var bar = Assert.Single(bars);
        var revision = Assert.Single(open);

        Assert.Equal(bar.Close, revision.Close);
        Assert.Equal(bar.Volume, revision.Volume);
        Assert.Equal(bar.Source, revision.Source);
        Assert.Equal(bar.Revision, revision.Revision);
        Assert.Equal(bar.RevisedAtUtc, revision.ObservedFromUtc);
    }

    [Fact]
    public async Task The_first_window_opens_when_the_bar_was_ingested()
    {
        // The invariant the migration's backfill depends on: revision zero
        // began being observed exactly when the bar entered the system.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var instrumentId = await AddInstrumentAsync(scope, "PIG", "PITG");

        await ObserveAsync(scope, instrumentId, close: 100m, at: T1);

        await using var reader = await CreateScopeAsync();

        var bar = Assert.Single(await reader.Bars.ListForUpdateAsync(
            instrumentId,
            BarInterval.OneDay,
            Period,
            Period.AddDays(1),
            TestContext.Current.CancellationToken));

        var revision = Assert.Single(await reader.Bars.ListOpenRevisionsForUpdateAsync(
            instrumentId,
            BarInterval.OneDay,
            Period,
            Period.AddDays(1),
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(0, revision.Revision);
        Assert.Equal(bar.IngestedAtUtc, revision.ObservedFromUtc);
    }

    [Fact]
    public async Task The_same_statement_cannot_be_recorded_twice()
    {
        // The primary key is the concurrency guard this pipeline does not
        // otherwise have. Two writers racing the same restatement collide here
        // rather than silently overwriting each other.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var instrumentId = await AddInstrumentAsync(scope, "PIH", "PITH");

        await ObserveAsync(scope, instrumentId, close: 100m, at: T1);

        await using var second = await CreateScopeAsync();
        var duplicate = BarRevision.Snapshot(
            Bar(instrumentId, close: 100m, ingestedAtUtc: T1), T1);

        second.Bars.AddRevisions([duplicate]);

        // Act & Assert
        await Assert.ThrowsAnyAsync<Exception>(
            () => second.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>Stores a bar and opens its observation window.</summary>
    private static async Task ObserveAsync(
        MarketDataScope scope,
        InstrumentId instrumentId,
        decimal close,
        DateTimeOffset at)
    {
        var bar = Bar(instrumentId, close, at);

        scope.Bars.AddRange([bar]);
        scope.Bars.AddRevisions([BarRevision.Snapshot(bar, at)]);

        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Restates a bar the way the ingestion pipeline does: close the open
    /// window and open the next one at the same instant.
    /// </summary>
    private static async Task ReviseAsync(
        MarketDataScope scope,
        InstrumentId instrumentId,
        decimal close,
        DateTimeOffset at)
    {
        var held = Assert.Single(await scope.Bars.ListForUpdateAsync(
            instrumentId,
            BarInterval.OneDay,
            Period,
            Period.AddDays(1),
            TestContext.Current.CancellationToken));

        var open = Assert.Single(await scope.Bars.ListOpenRevisionsForUpdateAsync(
            instrumentId,
            BarInterval.OneDay,
            Period,
            Period.AddDays(1),
            TestContext.Current.CancellationToken));

        Assert.True(held.Revise(
            Price.Create(100m),
            Price.Create(110m),
            Price.Create(95m),
            Price.Create(close),
            1_000,
            null,
            Source,
            at));

        open.Supersede(at);
        scope.Bars.AddRevisions([BarRevision.Snapshot(held, at)]);

        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<IReadOnlyList<SeriesBar>> ReadAsOfAsync(
        MarketDataScope scope,
        InstrumentId instrumentId,
        DateTimeOffset knownAsOfUtc)
    {
        Assert.True(BarQuery.TryCreate(
            instrumentId,
            BarInterval.OneDay,
            fromUtc: null,
            toUtc: null,
            limit: null,
            out var query,
            out var problem,
            adjusted: false,
            knownAsOfUtc), problem);

        var series = await scope.MarketData.GetSeriesAsync(
            query, TestContext.Current.CancellationToken);

        return series.Bars;
    }

    private static async Task<IReadOnlyList<SeriesBar>> ReadCurrentAsync(
        MarketDataScope scope,
        InstrumentId instrumentId)
    {
        Assert.True(BarQuery.TryCreate(
            instrumentId,
            BarInterval.OneDay,
            fromUtc: null,
            toUtc: null,
            limit: null,
            out var query,
            out var problem,
            adjusted: false), problem);

        var series = await scope.MarketData.GetSeriesAsync(
            query, TestContext.Current.CancellationToken);

        return series.Bars;
    }

    private static OhlcvBar Bar(
        InstrumentId instrumentId,
        decimal close,
        DateTimeOffset ingestedAtUtc) =>
        OhlcvBar.Record(
            instrumentId,
            BarInterval.OneDay,
            Period,
            Price.Create(100m),
            Price.Create(110m),
            Price.Create(95m),
            Price.Create(close),
            1_000,
            null,
            Source,
            ingestedAtUtc);

    private static async Task<InstrumentId> AddInstrumentAsync(
        MarketDataScope scope,
        string venueCode,
        string ticker)
    {
        var exchange = Exchange.Register(
            ExchangeCode.Create(venueCode), $"{venueCode} Test Venue", "Asia/Ho_Chi_Minh", T1);

        scope.Exchanges.Add(exchange);
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        var instrument = Instrument.Register(
            exchange.Id,
            Ticker.Create(ticker),
            $"{ticker} Test Company",
            AssetType.Equity,
            CurrencyCode.Vnd,
            T1);

        instrument.List(T1);

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
            MarketData = _scope.ServiceProvider.GetRequiredService<IMarketDataQueryService>();
        }

        public IUnitOfWork UnitOfWork { get; }

        public IExchangeRepository Exchanges { get; }

        public IInstrumentRepository Instruments { get; }

        public IBarRepository Bars { get; }

        public IMarketDataQueryService MarketData { get; }

        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync();
            await _factory.DisposeAsync();
        }
    }
}
