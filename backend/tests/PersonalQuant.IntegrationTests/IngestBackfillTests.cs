extern alias cli;

using System.Text;
using Microsoft.Extensions.DependencyInjection;
using cli::PersonalQuant.Cli.CommandLine;
using cli::PersonalQuant.Cli.Commands;
using PersonalQuant.Application.Abstractions;
using PersonalQuant.Application.Exchanges;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Application.Universes;
using PersonalQuant.Domain.Currencies;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.IntegrationTests;

/// <summary>
/// Drives <c>ingest backfill</c> through the real pipeline and real PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// The unit tests drive the walk against a fake ingestion service that
/// returns whatever range it is told to. That proves the steering and none of
/// what the steering depends on: that the pipeline floors each start to a
/// period, truncates each window to the source's declared bound, records the
/// truncated end as the run's range, and skips once no finished period is
/// left. If any of those drifted, the walk could stop short or never stop,
/// and every unit test would stay green.
/// </para>
/// <para>
/// The source here is in memory and serves a bar for every weekday, with a
/// call bound of ten periods. Everything between the command line and the
/// <c>quant</c> schema is the real composition root.
/// </para>
/// </remarks>
/// <param name="containers">Shared container fixture.</param>
[Collection(DependencyContainerTests.Name)]
public sealed class IngestBackfillTests(DependencyContainerFixture containers)
{
    private static readonly DateTimeOffset RecordedAt = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_backfill_fills_the_gap_before_bars_already_held()
    {
        // The ACB regression, end to end. The series already holds March, so
        // the checkpoint sits at April. A backfill from January has to walk
        // January and February window by window rather than jump to the
        // checkpoint after its first pass and report success.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        var source = new WeekdaySource();
        await using var scope = await CreateScopeAsync(source);
        var instrumentId = await AddInstrumentAsync(scope, "IBF", "IBFA");

        var march = await scope.RunAsync(
            "ingest", "backfill", "--instrument", "IBFA", "--source", WeekdaySource.Code,
            "--interval", "1d", "--from", "2025-03-03", "--to", "2025-03-31");

        Assert.Equal(ExitCode.Ok, march);

        var calls = source.Calls.Count;

        // Act
        var code = await scope.RunAsync(
            "ingest", "backfill", "--instrument", "IBFA", "--source", WeekdaySource.Code,
            "--interval", "1d", "--from", "2025-01-02", "--to", "2025-03-31");

        // Assert
        Assert.Equal(ExitCode.Ok, code);

        var stored = await ReadDatesAsync(scope, instrumentId);

        Assert.Equal(Weekdays(new DateOnly(2025, 1, 2), new DateOnly(2025, 3, 31)), stored);

        // Ninety calendar days at ten a call is nine windows, each starting
        // where the one before it stopped. Fewer would mean a window was
        // skipped; more would mean the walk re-asked for one it had covered.
        var walk = source.Calls.Skip(calls).ToList();

        Assert.Equal(9, walk.Count);
        Assert.Equal(new DateTimeOffset(2025, 1, 2, 0, 0, 0, TimeSpan.Zero), walk[0].FromUtc);

        for (var index = 1; index < walk.Count; index++)
        {
            Assert.Equal(walk[index - 1].ToUtc, walk[index].FromUtc);
        }

        Assert.Equal(new DateTimeOffset(2025, 4, 1, 0, 0, 0, TimeSpan.Zero), walk[^1].ToUtc);
        Assert.Contains("IBFA: 9 passes", scope.Result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_an_end_a_backfill_stops_at_the_last_finished_session()
    {
        // The walk's other exit: no --to, so it ends when the pipeline skips a
        // pass because no finished period is left. The termination relies on
        // the pipeline handing back a period-aligned end that the next pass
        // starts from; were it ever not, this would run to --max-passes.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        var source = new WeekdaySource();
        await using var scope = await CreateScopeAsync(source);
        var instrumentId = await AddInstrumentAsync(scope, "IBG", "IBGA");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = today.AddDays(-25);

        // Act
        var code = await scope.RunAsync(
            "ingest", "backfill", "--instrument", "IBGA", "--source", WeekdaySource.Code,
            "--interval", "1d", "--from", from.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            "--max-passes", "10");

        // Assert
        Assert.Equal(ExitCode.Ok, code);
        Assert.DoesNotContain("without reaching the end", scope.Problems, StringComparison.Ordinal);

        var stored = await ReadDatesAsync(scope, instrumentId);

        // Today has not finished, so it is not stored; every weekday before it is.
        // Twenty-five days is three windows; the fourth pass finds nothing left
        // and is skipped before it reaches the source.
        Assert.Equal(Weekdays(from, today.AddDays(-1)), stored);
        Assert.Equal(3, source.Calls.Count);
        Assert.Contains("IBGA: 4 passes", scope.Result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_series_held_in_redis_is_not_ingested_until_it_is_released()
    {
        // The scheduled host and the CLI, through the real composition root and
        // the real Redis: a pass over a series another process holds reaches
        // neither the source nor the bars, and records why.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        var source = new WeekdaySource();
        await using var scope = await CreateScopeAsync(source);
        var instrumentId = await AddInstrumentAsync(scope, "IBH", "IBHA");

        var elsewhere = await scope.SeriesLock.TryAcquireAsync(
            instrumentId, BarInterval.OneDay, TestContext.Current.CancellationToken);
        Assert.NotNull(elsewhere);

        // Act
        var held = await scope.RunAsync(
            "ingest", "run", "--instrument", "IBHA", "--source", WeekdaySource.Code,
            "--interval", "1d", "--from", "2025-03-03", "--to", "2025-03-10");

        await elsewhere.DisposeAsync();

        var released = await scope.RunAsync(
            "ingest", "run", "--instrument", "IBHA", "--source", WeekdaySource.Code,
            "--interval", "1d", "--from", "2025-03-03", "--to", "2025-03-10");

        // Assert
        Assert.Equal(ExitCode.Refused, held);
        Assert.Contains("in progress", scope.Problems, StringComparison.Ordinal);
        Assert.Equal(ExitCode.Ok, released);
        Assert.Single(source.Calls);
        Assert.Equal(
            Weekdays(new DateOnly(2025, 3, 3), new DateOnly(2025, 3, 10)),
            await ReadDatesAsync(scope, instrumentId));
    }

    private static List<DateOnly> Weekdays(DateOnly first, DateOnly last)
    {
        var days = new List<DateOnly>();

        for (var day = first; day <= last; day = day.AddDays(1))
        {
            if (day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            {
                days.Add(day);
            }
        }

        return days;
    }

    private static async Task<List<DateOnly>> ReadDatesAsync(BackfillScope scope, InstrumentId instrumentId)
    {
        Assert.True(
            BarQuery.TryCreate(
                instrumentId, BarInterval.OneDay, null, null, null, out var query, out var problem),
            problem);

        var series = await scope.MarketData.GetSeriesAsync(query, TestContext.Current.CancellationToken);

        return [.. series.Bars.Select(bar => DateOnly.FromDateTime(bar.OpenedAtUtc.UtcDateTime))];
    }

    private static async Task<InstrumentId> AddInstrumentAsync(
        BackfillScope scope,
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

    private async Task<BackfillScope> CreateScopeAsync(IMarketDataProvider source)
    {
        var factory = PersonalQuantApiFactory.WithMarketDataProvider(
            containers.Postgres,
            containers.Redis,
            source);

        using var client = factory.CreateClient();
        _ = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        return new BackfillScope(factory);
    }

    /// <summary>
    /// A source that serves one flat bar for every weekday it is asked about,
    /// ten periods at a time, and records each request.
    /// </summary>
    private sealed class WeekdaySource : IMarketDataProvider
    {
        public const string Code = "IBFS";

        private readonly List<MarketDataRequest> _calls = [];

        SourceCode IMarketDataProvider.Code { get; } = SourceCode.Create(Code);

        public ProviderCapability Capability { get; } = new()
        {
            Code = SourceCode.Create(Code),
            DisplayName = "In-memory weekday source",
            Intervals = new HashSet<BarInterval> { BarInterval.OneDay },
            ReportedFields = new ProviderReportedFields(),
            Limitations = new ProviderLimitations
            {
                MaxPeriodsPerCall = 10,
                AdjustsPricesAtSource = false,
            },
        };

        public IReadOnlyList<MarketDataRequest> Calls
        {
            get
            {
                lock (_calls)
                {
                    return [.. _calls];
                }
            }
        }

        public Task<MarketDataFetchResult> FetchBarsAsync(
            MarketDataRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            lock (_calls)
            {
                _calls.Add(request);
            }

            var bars = new List<ProviderBar>();
            var payload = new StringBuilder("timestamp,open,high,low,close,volume\n");

            for (var opened = request.FromUtc; opened < request.ToUtc; opened = opened.AddDays(1))
            {
                if (opened.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                {
                    continue;
                }

                bars.Add(new ProviderBar(opened, 100m, 100m, 100m, 100m, 1_000, null));
                payload.Append(System.Globalization.CultureInfo.InvariantCulture, $"{opened:yyyy-MM-dd},100,100,100,100,1000\n");
            }

            return Task.FromResult(new MarketDataFetchResult(payload.ToString(), "text/csv", bars));
        }
    }

    /// <summary>
    /// Owns a host, a DI scope and the command, so the backfill runs through
    /// the same composition root the API host is built from.
    /// </summary>
    private sealed class BackfillScope : IAsyncDisposable
    {
        private readonly PersonalQuantApiFactory _factory;
        private readonly AsyncServiceScope _scope;
        private readonly StringBuilder _result = new();
        private readonly StringBuilder _problems = new();

        public BackfillScope(PersonalQuantApiFactory factory)
        {
            _factory = factory;
            _scope = factory.Services.CreateAsyncScope();

            var services = _scope.ServiceProvider;

            UnitOfWork = services.GetRequiredService<IUnitOfWork>();
            Exchanges = services.GetRequiredService<IExchangeRepository>();
            Instruments = services.GetRequiredService<IInstrumentRepository>();
            MarketData = services.GetRequiredService<IMarketDataQueryService>();
            SeriesLock = services.GetRequiredService<ISeriesLock>();

            Commands = new IngestCommands(
                new Lazy<IMarketDataIngestionService>(services.GetRequiredService<IMarketDataIngestionService>),
                new Lazy<IInstrumentResolver>(services.GetRequiredService<IInstrumentResolver>),
                new Lazy<IUniverseCatalog>(services.GetRequiredService<IUniverseCatalog>),
                new Output(new StringWriter(_result), new StringWriter(_problems)));
        }

        public IUnitOfWork UnitOfWork { get; }

        public IExchangeRepository Exchanges { get; }

        public IInstrumentRepository Instruments { get; }

        public IMarketDataQueryService MarketData { get; }

        public ISeriesLock SeriesLock { get; }

        public string Result => _result.ToString();

        public string Problems => _problems.ToString();

        private IngestCommands Commands { get; }

        public async Task<int> RunAsync(params string[] args)
        {
            Assert.True(CommandArguments.TryParse(args, out var command, out var problem), problem);

            return await Commands.RunAsync(command, TestContext.Current.CancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync();
            await _factory.DisposeAsync();
        }
    }
}
