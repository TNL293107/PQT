using Microsoft.Extensions.DependencyInjection;
using PersonalQuant.Application.Abstractions;
using PersonalQuant.Application.CorporateActions;
using PersonalQuant.Application.Exchanges;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Domain.Currencies;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.Domain.CorporateActions;

namespace PersonalQuant.IntegrationTests;

/// <summary>
/// Verifies corporate actions, their factors, and the adjusted read against
/// real PostgreSQL.
/// </summary>
/// <remarks>
/// Three properties can only be shown here. The natural key is a unique index,
/// not a check in C#. A factor is stored as two columns of a complex property,
/// and a round trip is the only proof the mapping is right. And the adjusted
/// series is assembled from two tables, so a query that reads the factors
/// correctly in memory can still read nothing at all from a database.
/// </remarks>
/// <param name="containers">Shared container fixture.</param>
[Collection(DependencyContainerTests.Name)]
public sealed class CorporateActionPersistenceTests(DependencyContainerFixture containers)
{
    private static readonly DateTimeOffset Monday = new(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 1, 0, 0, TimeSpan.Zero);
    private static readonly SourceCode Source = SourceCode.Create("TEST");

    [Fact]
    public async Task An_action_and_its_factor_round_trip()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var instrumentId = await AddInstrumentAsync(scope, "CAA", "CAA");

        var action = CorporateAction.Record(
            instrumentId,
            CorporateActionType.CashDividend,
            new DateOnly(2026, 8, 5),
            ratio: null,
            cashAmount: 2_000m,
            Source,
            Now);

        action.Schedule(
            new DateOnly(2026, 8, 6), new DateOnly(2026, 8, 20), new DateOnly(2026, 7, 1), Now);

        scope.Actions.Add(action);
        scope.Actions.AddAdjustment(PriceAdjustment.For(
            action,
            AdjustmentFactor.Create(0.98m, 1m),
            Price.Create(100_000m),
            DataRules.AdjustmentVersion,
            Now));

        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        await using var reader = await CreateScopeAsync();
        var stored = Assert.Single(
            await reader.Actions.ListAsync(instrumentId, TestContext.Current.CancellationToken));
        var factor = Assert.Single(
            await reader.Actions.ListAdjustmentsAsync(instrumentId, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(CorporateActionType.CashDividend, stored.Type);
        Assert.Equal(2_000m, stored.CashAmount);
        Assert.Null(stored.Ratio);
        Assert.Equal(new DateOnly(2026, 8, 6), stored.RecordDate);
        Assert.Equal(new DateOnly(2026, 7, 1), stored.AnnouncedOn);
        Assert.Equal("TEST", stored.Source.Value);

        Assert.Equal(0.98m, factor.Factor.Price);
        Assert.Equal(1m, factor.Factor.Shares);
        Assert.Equal(100_000m, factor.ReferenceClose.Value);
        Assert.True(factor.IsCurrentFor(stored));
    }

    [Fact]
    public async Task One_instrument_may_hold_only_one_action_of_a_type_on_a_date()
    {
        // The natural key an import re-runs against. Without it a nightly
        // provider pull records the same dividend every night, and the series
        // is rescaled once more each time.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var instrumentId = await AddInstrumentAsync(scope, "CAB", "CAB");

        scope.Actions.Add(Split(instrumentId, new DateOnly(2026, 8, 5)));
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        await using var clash = await CreateScopeAsync();
        clash.Actions.Add(Split(instrumentId, new DateOnly(2026, 8, 5)));

        // Assert
        await Assert.ThrowsAnyAsync<Exception>(
            () => clash.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Two_kinds_of_action_may_share_an_ex_date()
    {
        // Routine in Vietnam: a cash dividend and a bonus issue go ex on the
        // same morning, and both rescale the series.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var instrumentId = await AddInstrumentAsync(scope, "CAC", "CAC");
        var exDate = new DateOnly(2026, 8, 5);

        scope.Actions.Add(Split(instrumentId, exDate));
        scope.Actions.Add(CorporateAction.Record(
            instrumentId, CorporateActionType.CashDividend, exDate, null, 1_500m, Source, Now));

        // Act
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        await using var reader = await CreateScopeAsync();
        var stored = await reader.Actions.ListAsync(
            instrumentId, TestContext.Current.CancellationToken);

        Assert.Equal(2, stored.Count);
    }

    [Fact]
    public async Task An_action_is_found_by_the_key_an_import_re_runs_against()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var instrumentId = await AddInstrumentAsync(scope, "CAD", "CAD");

        scope.Actions.Add(Split(instrumentId, new DateOnly(2026, 8, 5)));
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        await using var reader = await CreateScopeAsync();

        var found = await reader.Actions.FindAsync(
            instrumentId,
            CorporateActionType.StockSplit,
            new DateOnly(2026, 8, 5),
            TestContext.Current.CancellationToken);

        var missed = await reader.Actions.FindAsync(
            instrumentId,
            CorporateActionType.StockSplit,
            new DateOnly(2026, 8, 6),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(found);
        Assert.Null(missed);
    }

    [Fact]
    public async Task A_recompute_writes_a_factor_and_leaves_it_alone_on_a_second_pass()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var instrumentId = await AddInstrumentAsync(scope, "CAE", "CAE");

        scope.Bars.AddRange([Bar(instrumentId, Monday, 100_000m)]);
        scope.Actions.Add(Split(instrumentId, new DateOnly(2026, 8, 5)));
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        await using var first = await CreateScopeAsync();
        var one = await first.Adjustments.RecomputeAsync(
            instrumentId, TestContext.Current.CancellationToken);
        await first.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var second = await CreateScopeAsync();
        var two = await second.Adjustments.RecomputeAsync(
            instrumentId, TestContext.Current.CancellationToken);
        await second.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, one.Computed);
        Assert.Equal(0, one.Unchanged);
        Assert.Empty(one.Rejections);

        Assert.Equal(0, two.Computed);
        Assert.Equal(1, two.Unchanged);

        await using var reader = await CreateScopeAsync();
        var factor = Assert.Single(
            await reader.Actions.ListAdjustmentsAsync(instrumentId, TestContext.Current.CancellationToken));

        Assert.Equal(0.5m, factor.Factor.Price);
        Assert.Equal(2m, factor.Factor.Shares);
    }

    [Fact]
    public async Task The_adjusted_read_rescales_only_what_precedes_the_ex_date()
    {
        // The whole point of the phase, assembled from two tables. Three
        // sessions at 100,000 before a two-for-one, one at 50,000 after: the
        // adjusted series is flat, the raw series halves.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var instrumentId = await AddInstrumentAsync(scope, "CAF", "CAF");

        scope.Bars.AddRange(
        [
            Bar(instrumentId, Monday, 100_000m),
            Bar(instrumentId, Monday.AddDays(1), 100_000m),
            Bar(instrumentId, Monday.AddDays(2), 100_000m),
            Bar(instrumentId, Monday.AddDays(3), 50_000m),
        ]);

        scope.Actions.Add(Split(instrumentId, new DateOnly(2026, 8, 6)));
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var recompute = await CreateScopeAsync();
        _ = await recompute.Adjustments.RecomputeAsync(
            instrumentId, TestContext.Current.CancellationToken);
        await recompute.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        await using var reader = await CreateScopeAsync();
        var adjusted = await reader.Query.GetSeriesAsync(
            Query(instrumentId, adjusted: true), TestContext.Current.CancellationToken);
        var raw = await reader.Query.GetSeriesAsync(
            Query(instrumentId, adjusted: false), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(adjusted.Adjusted);
        Assert.Equal(4, adjusted.Bars.Count);
        Assert.All(adjusted.Bars, bar => Assert.Equal(50_000m, bar.Close));
        Assert.Equal(2_000, adjusted.Bars[0].Volume);
        Assert.Equal(1_000, adjusted.Bars[3].Volume);

        Assert.False(raw.Adjusted);
        Assert.Equal(100_000m, raw.Bars[0].Close);
        Assert.Equal(1_000, raw.Bars[0].Volume);
        Assert.All(raw.Bars, bar => Assert.Equal(1m, bar.PriceFactor));
    }

    [Fact]
    public async Task A_cancelled_action_drops_its_factor_and_the_series_returns_to_raw()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var instrumentId = await AddInstrumentAsync(scope, "CAG", "CAG");

        scope.Bars.AddRange([Bar(instrumentId, Monday, 100_000m)]);
        scope.Actions.Add(Split(instrumentId, new DateOnly(2026, 8, 5)));
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var recompute = await CreateScopeAsync();
        _ = await recompute.Adjustments.RecomputeAsync(
            instrumentId, TestContext.Current.CancellationToken);
        await recompute.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        await using var canceller = await CreateScopeAsync();
        var tracked = Assert.Single(
            await canceller.Actions.ListAsync(instrumentId, TestContext.Current.CancellationToken));
        tracked.Cancel("The issuer withdrew it.", Now.AddDays(1));
        await canceller.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var again = await CreateScopeAsync();
        var run = await again.Adjustments.RecomputeAsync(
            instrumentId, TestContext.Current.CancellationToken);
        await again.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, run.Removed);

        await using var reader = await CreateScopeAsync();
        Assert.Empty(await reader.Actions.ListAdjustmentsAsync(
            instrumentId, TestContext.Current.CancellationToken));

        var series = await reader.Query.GetSeriesAsync(
            Query(instrumentId, adjusted: true), TestContext.Current.CancellationToken);

        Assert.Equal(100_000m, series.Bars[0].Close);
    }

    [Fact]
    public async Task An_action_closes_the_price_limit_finding_on_its_ex_date()
    {
        // Phase 3 recorded the discontinuity and left it open on purpose.
        // This is the phase that answers it, and the answer is a recorded
        // resolution rather than an edited row.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var instrumentId = await AddInstrumentAsync(scope, "CAH", "CAH");
        var exDay = Monday.AddDays(2);

        scope.Bars.AddRange([Bar(instrumentId, Monday, 100_000m), Bar(instrumentId, exDay, 50_000m)]);
        scope.Actions.Add(Split(instrumentId, DateOnly.FromDateTime(exDay.UtcDateTime)));
        scope.Issues.Add(DataQualityIssue.Raise(
            instrumentId,
            BarInterval.OneDay,
            exDay,
            DataQualityIssueKind.PriceLimitBreach,
            "The close moved -50%, beyond the band.",
            DataRules.ValidationVersion,
            Now));

        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        await using var recompute = await CreateScopeAsync();
        var run = await recompute.Adjustments.RecomputeAsync(
            instrumentId, TestContext.Current.CancellationToken);
        await recompute.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, run.IssuesExplained);

        await using var reader = await CreateScopeAsync();
        Assert.Empty(await reader.Issues.ListOpenAsync(
            instrumentId, BarInterval.OneDay, 10, TestContext.Current.CancellationToken));

        var stored = Assert.Single(await reader.Issues.ListAsync(
            instrumentId,
            BarInterval.OneDay,
            Monday,
            Monday.AddDays(7),
            TestContext.Current.CancellationToken));

        Assert.Equal(DataQualityIssueStatus.Explained, stored.Status);
        Assert.Contains("StockSplit", stored.Resolution, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_action_with_no_price_before_it_is_rejected_rather_than_guessed()
    {
        // Two of the five formulas divide by the previous close. A listing's
        // first-ever action has none, and inventing one would silently rescale
        // the whole series by a made-up number.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var instrumentId = await AddInstrumentAsync(scope, "CAI", "CAI");

        scope.Actions.Add(CorporateAction.Record(
            instrumentId,
            CorporateActionType.CashDividend,
            new DateOnly(2026, 8, 5),
            null,
            2_000m,
            Source,
            Now));

        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        await using var recompute = await CreateScopeAsync();
        var run = await recompute.Adjustments.RecomputeAsync(
            instrumentId, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, run.Computed);
        var rejection = Assert.Single(run.Rejections);
        Assert.Equal(CorporateActionType.CashDividend, rejection.Type);
    }

    private static BarQuery Query(InstrumentId instrumentId, bool adjusted)
    {
        Assert.True(BarQuery.TryCreate(
            instrumentId,
            BarInterval.OneDay,
            null,
            null,
            null,
            out var query,
            out _,
            adjusted));

        return query;
    }

    private static CorporateAction Split(InstrumentId instrumentId, DateOnly exDate) =>
        CorporateAction.Record(
            instrumentId, CorporateActionType.StockSplit, exDate, 2m, null, Source, Now);

    private static OhlcvBar Bar(
        InstrumentId instrumentId,
        DateTimeOffset openedAtUtc,
        decimal close) =>
        OhlcvBar.Record(
            instrumentId,
            BarInterval.OneDay,
            openedAtUtc,
            Price.Create(close),
            Price.Create(close),
            Price.Create(close),
            Price.Create(close),
            1_000,
            null,
            Source,
            Now);

    private static async Task<InstrumentId> AddInstrumentAsync(
        CorporateActionScope scope,
        string venueCode,
        string ticker)
    {
        var exchange = Exchange.Register(
            ExchangeCode.Create(venueCode),
            $"{venueCode} Test Venue",
            "Asia/Ho_Chi_Minh",
            Now,
            mic: null,
            PriceLimit.FromPercent(7m));

        scope.Exchanges.Add(exchange);

        var instrument = Instrument.Register(
            exchange.Id,
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

    private async Task<CorporateActionScope> CreateScopeAsync()
    {
        var factory = PersonalQuantApiFactory.WithDependencies(
            containers.Postgres,
            containers.Redis,
            applyMigrations: true);

        using var client = factory.CreateClient();
        _ = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        return new CorporateActionScope(factory);
    }

    /// <summary>
    /// Owns a host and a DI scope, so every test reads and writes through the
    /// real composition root.
    /// </summary>
    private sealed class CorporateActionScope : IAsyncDisposable
    {
        private readonly PersonalQuantApiFactory _factory;
        private readonly AsyncServiceScope _scope;

        public CorporateActionScope(PersonalQuantApiFactory factory)
        {
            _factory = factory;
            _scope = factory.Services.CreateAsyncScope();

            UnitOfWork = _scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            Exchanges = _scope.ServiceProvider.GetRequiredService<IExchangeRepository>();
            Instruments = _scope.ServiceProvider.GetRequiredService<IInstrumentRepository>();
            Bars = _scope.ServiceProvider.GetRequiredService<IBarRepository>();
            Issues = _scope.ServiceProvider.GetRequiredService<IDataQualityRepository>();
            Actions = _scope.ServiceProvider.GetRequiredService<ICorporateActionRepository>();
            Adjustments = _scope.ServiceProvider.GetRequiredService<IPriceAdjustmentService>();
            Query = _scope.ServiceProvider.GetRequiredService<IMarketDataQueryService>();
        }

        public IUnitOfWork UnitOfWork { get; }

        public IExchangeRepository Exchanges { get; }

        public IInstrumentRepository Instruments { get; }

        public IBarRepository Bars { get; }

        public IDataQualityRepository Issues { get; }

        public ICorporateActionRepository Actions { get; }

        public IPriceAdjustmentService Adjustments { get; }

        public IMarketDataQueryService Query { get; }

        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync();
            await _factory.DisposeAsync();
        }
    }
}
