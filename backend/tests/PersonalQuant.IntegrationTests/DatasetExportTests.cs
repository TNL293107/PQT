using Microsoft.Extensions.DependencyInjection;
using Parquet;
using PersonalQuant.Application.Abstractions;
using PersonalQuant.Application.Datasets;
using PersonalQuant.Application.Exchanges;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Application.Universes;
using PersonalQuant.Domain.Currencies;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.Domain.Universes;

namespace PersonalQuant.IntegrationTests;

/// <summary>
/// Exports a point-in-time dataset with history from real PostgreSQL, through
/// the real composition root, onto disk.
/// </summary>
/// <remarks>
/// <para>
/// The unit tests run the export over an in-memory series that honours the
/// query bound the way the repository does. This proves that it does: that the
/// history read, which asks for the last N bars before a date with no start,
/// gets the bars nearest that date from the database rather than the oldest
/// ones, and that the removal announcement survives the trip from its column
/// to the manifest.
/// </para>
/// <para>
/// One name leaves and comes back inside the window, and one is admitted in
/// the middle of it, which are the two shapes history changes.
/// </para>
/// </remarks>
/// <param name="containers">Shared container fixture.</param>
[Collection(DependencyContainerTests.Name)]
public sealed class DatasetExportTests(DependencyContainerFixture containers) : IDisposable
{
    private static readonly SourceCode Source = SourceCode.Create("TEST");
    private static readonly DateTimeOffset RecordedAt = new(2026, 10, 11, 0, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly SeriesStart = new(2025, 6, 2);
    private static readonly DateOnly WindowFrom = new(2025, 7, 1);
    private static readonly DateOnly WindowTo = new(2025, 7, 18);

    private static readonly DateOnly Removed = new(2025, 7, 8);
    private static readonly DateOnly RemovalAnnounced = new(2025, 7, 1);
    private static readonly DateOnly Returned = new(2025, 7, 10);
    private static readonly DateOnly Admitted = new(2025, 7, 9);

    private readonly string root = Path.Combine(
        Path.GetTempPath(), "pqt-dataset-export", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task A_history_export_fills_each_series_from_before_admission_and_through_its_gap()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var (code, reentrant, joiner) = await ArrangeAsync(scope, "UDH");

        // Act
        var export = await scope.ExportAsync(code, history: 5);

        // Assert
        Assert.True(export.Succeeded, export.Problem);

        var manifest = export.Manifest!;
        var returning = manifest.Instruments.Single(instrument => instrument.InstrumentId == reentrant.Value);
        var admitted = manifest.Instruments.Single(instrument => instrument.InstrumentId == joiner.Value);

        Assert.Equal(DatasetManifest.CurrentSchemaVersion, manifest.SchemaVersion);
        Assert.Equal(5, manifest.HistorySessions);

        // The five sessions before the window for the name that was already a
        // member, and the five before its admission - three of them inside the
        // window - for the one that joined.
        Assert.Equal(new DateOnly(2025, 6, 24), returning.HistoryFrom);
        Assert.Equal(new DateOnly(2025, 7, 2), admitted.HistoryFrom);

        var sessions = await ReadSessionsAsync(manifest);

        Assert.Equal(Weekdays(new DateOnly(2025, 6, 24), WindowTo), sessions[reentrant.Value]);
        Assert.Equal(Weekdays(new DateOnly(2025, 7, 2), WindowTo), sessions[joiner.Value]);

        // Membership is what it was: the gap is still a gap, and the joiner
        // still joined on the ninth.
        Assert.Equal(
            [
                new DatasetSpell(WindowFrom, Removed, null, RemovalAnnounced),
                new DatasetSpell(Returned, null, new DateOnly(2025, 7, 3)),
            ],
            returning.Spells!);
        Assert.Equal([new DatasetSpell(Admitted, null, new DateOnly(2025, 6, 25))], admitted.Spells!);

        Assert.True((await scope.Datasets.VerifyAsync(
            manifest.DatasetId, manifest.DatasetVersion, TestContext.Current.CancellationToken)).IsIntact);
    }

    [Fact]
    public async Task Without_history_the_same_universe_exports_only_its_membership_sessions()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var (code, reentrant, joiner) = await ArrangeAsync(scope, "UDN");

        // Act
        var without = (await scope.ExportAsync(code, history: 0)).Manifest!;
        var with = (await scope.ExportAsync(code, history: 5)).Manifest!;

        // Assert
        Assert.NotEqual(without.DatasetId, with.DatasetId);
        Assert.Equal(0, without.HistorySessions);
        Assert.All(without.Instruments, instrument => Assert.Null(instrument.HistoryFrom));

        var sessions = await ReadSessionsAsync(without);

        Assert.Equal(
            [.. Weekdays(WindowFrom, Removed.AddDays(-1)), .. Weekdays(Returned, WindowTo)],
            sessions[reentrant.Value]);
        Assert.Equal(Weekdays(Admitted, WindowTo), sessions[joiner.Value]);
        Assert.Equal(32, with.RowCount);
        Assert.Equal(20, without.RowCount);
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

    /// <summary>
    /// A universe whose coverage spans the window, a name that leaves on the
    /// eighth and returns on the tenth, a name admitted on the ninth, and a bar
    /// for each on every weekday from June.
    /// </summary>
    private static async Task<(UniverseCode Code, InstrumentId Reentrant, InstrumentId Joiner)> ArrangeAsync(
        ExportScope scope,
        string prefix)
    {
        var code = UniverseCode.Create(prefix);
        var universe = Universe.Define(
            UniverseId.New(), code, $"{prefix} Test Universe", UniverseKind.Index, Source, RecordedAt);
        universe.DeclareCoverage(MembershipCoverage.Create(SeriesStart, null), Source, RecordedAt);
        scope.Universes.Add(universe);
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        var reentrant = await AddInstrumentAsync(scope, $"{prefix}R", $"{prefix}RA");
        var joiner = await AddInstrumentAsync(scope, $"{prefix}J", $"{prefix}JA");

        var first = UniverseMembership.Admit(universe.Id, reentrant, SeriesStart, null, Source, RecordedAt);
        first.Remove(Removed, RemovalAnnounced);
        scope.Universes.Add(first);
        scope.Universes.Add(UniverseMembership.Admit(
            universe.Id, reentrant, Returned, new DateOnly(2025, 7, 3), Source, RecordedAt));
        scope.Universes.Add(UniverseMembership.Admit(
            universe.Id, joiner, Admitted, new DateOnly(2025, 6, 25), Source, RecordedAt));

        scope.Bars.AddRange(
            [
                .. Weekdays(SeriesStart, WindowTo).SelectMany(day => new[] { Bar(reentrant, day), Bar(joiner, day) }),
            ]);

        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        return (code, reentrant, joiner);
    }

    private static OhlcvBar Bar(InstrumentId instrumentId, DateOnly session) =>
        OhlcvBar.Record(
            instrumentId,
            BarInterval.OneDay,
            new DateTimeOffset(session.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            Price.Create(100m),
            Price.Create(100m),
            Price.Create(100m),
            Price.Create(100m),
            1_000,
            null,
            Source,
            RecordedAt);

    private static async Task<InstrumentId> AddInstrumentAsync(ExportScope scope, string venueCode, string ticker)
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

    /// <summary>Reads each instrument's sessions back out of the written file.</summary>
    private async Task<Dictionary<Guid, List<DateOnly>>> ReadSessionsAsync(DatasetManifest manifest)
    {
        var path = Path.Combine(
            root, manifest.DatasetId, $"v{manifest.DatasetVersion}", IDatasetStore.BarsFileName);

        await using var stream = File.OpenRead(path);
        await using var reader = await ParquetReader.CreateAsync(
            stream, cancellationToken: TestContext.Current.CancellationToken);
        using var group = reader.OpenRowGroupReader(0);

        var ids = new string?[group.RowCount];
        var opened = new DateTime[group.RowCount];

        await group.ReadAsync(
            reader.Schema.DataFields[0], ids.AsMemory(), cancellationToken: TestContext.Current.CancellationToken);
        await group.ReadAsync<DateTime>(
            reader.Schema.DataFields[1], opened.AsMemory(), cancellationToken: TestContext.Current.CancellationToken);

        return ids
            .Zip(opened)
            .GroupBy(row => Guid.Parse(row.First!))
            .ToDictionary(group => group.Key, group => group.Select(row => DateOnly.FromDateTime(row.Second)).ToList());
    }

    private async Task<ExportScope> CreateScopeAsync()
    {
        var factory = PersonalQuantApiFactory.WithDatasetDirectory(containers.Postgres, containers.Redis, root);

        using var client = factory.CreateClient();
        _ = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        return new ExportScope(factory);
    }

    private sealed class ExportScope : IAsyncDisposable
    {
        private readonly PersonalQuantApiFactory _factory;
        private readonly AsyncServiceScope _scope;

        public ExportScope(PersonalQuantApiFactory factory)
        {
            _factory = factory;
            _scope = factory.Services.CreateAsyncScope();

            var services = _scope.ServiceProvider;

            UnitOfWork = services.GetRequiredService<IUnitOfWork>();
            Exchanges = services.GetRequiredService<IExchangeRepository>();
            Instruments = services.GetRequiredService<IInstrumentRepository>();
            Universes = services.GetRequiredService<IUniverseRepository>();
            Bars = services.GetRequiredService<IBarRepository>();
            Datasets = services.GetRequiredService<IDatasetExportService>();
        }

        public IUnitOfWork UnitOfWork { get; }

        public IExchangeRepository Exchanges { get; }

        public IInstrumentRepository Instruments { get; }

        public IUniverseRepository Universes { get; }

        public IBarRepository Bars { get; }

        public IDatasetExportService Datasets { get; }

        public Task<DatasetExport> ExportAsync(UniverseCode code, int history)
        {
            Assert.True(
                DatasetRequest.TryCreatePointInTime(
                    code, BarInterval.OneDay, WindowFrom, WindowTo, out var request, out var problem, historySessions: history),
                problem);

            return Datasets.ExportAsync(request, TestContext.Current.CancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync();
            await _factory.DisposeAsync();
        }
    }
}
