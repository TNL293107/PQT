using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Parquet;
using PersonalQuant.Application.Abstractions;
using PersonalQuant.Application.Datasets;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Application.Universes;
using PersonalQuant.Domain.Currencies;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.Domain.Universes;
using PersonalQuant.Infrastructure.Datasets;
using PersonalQuant.UnitTests.MarketData.Fakes;

namespace PersonalQuant.UnitTests.Datasets;

/// <summary>
/// Verifies the export end to end, over the real Parquet store.
/// </summary>
/// <remarks>
/// The store is not faked. Everything U5 promises — a file on disk, a hash that
/// matches it, a manifest that can be read back — is a property of the bytes
/// actually written, and a fake writer would verify none of it. The directory
/// is a temporary one and is removed afterwards.
/// </remarks>
public sealed class DatasetExportServiceTests : IDisposable
{
    private static readonly DateOnly From = new(2026, 8, 3);
    private static readonly DateOnly To = new(2026, 8, 7);
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 1, 0, 0, TimeSpan.Zero);
    private static readonly UniverseCode Vn30 = UniverseCode.Create("VN30");

    private readonly string root = Path.Combine(
        Path.GetTempPath(), "pqt-datasets", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task An_export_writes_a_file_a_manifest_and_a_hash_that_matches()
    {
        var harness = new Harness(root);

        var export = await harness.ExportAsync();

        Assert.True(export.Succeeded);

        var manifest = export.Manifest!;
        var file = Assert.Single(manifest.Files);

        Assert.Equal(IDatasetStore.BarsFileName, file.Name);
        Assert.Equal(5, file.RowCount);
        Assert.True(File.Exists(Path.Combine(root, manifest.DatasetId, "v1", file.Name)));

        var verification = await harness.VerifyAsync(manifest.DatasetId, 1);

        Assert.True(verification.IsIntact);
        Assert.Equal(1, verification.FilesChecked);
    }

    [Fact]
    public async Task A_changed_file_fails_verification()
    {
        // The whole point of recording a digest. A dataset whose bytes no longer
        // match its manifest must be a hard error, because a reproducible result
        // computed from a silently changed input is worse than no result.
        var harness = new Harness(root);
        var manifest = (await harness.ExportAsync()).Manifest!;

        await File.WriteAllTextAsync(
            Path.Combine(root, manifest.DatasetId, "v1", IDatasetStore.BarsFileName),
            "not a parquet file",
            TestContext.Current.CancellationToken);

        var verification = await harness.VerifyAsync(manifest.DatasetId, 1);

        Assert.False(verification.IsIntact);
        Assert.Contains(verification.Problems, problem => problem.Contains("hashes to", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_edited_manifest_fails_verification_even_with_the_data_untouched()
    {
        // A manifest is as editable as the data it describes. Checking only the
        // files would let somebody change the as-of a dataset claims, or the
        // policy it was built under, and leave every hash matching.
        var harness = new Harness(root);
        var manifest = (await harness.ExportAsync()).Manifest!;
        var path = Path.Combine(root, manifest.DatasetId, "v1", IDatasetStore.ManifestFileName);

        var json = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        await File.WriteAllTextAsync(
            path,
            json.Replace("\"strict\"", "\"permissive\"", StringComparison.Ordinal),
            TestContext.Current.CancellationToken);

        var verification = await harness.VerifyAsync(manifest.DatasetId, 1);

        Assert.False(verification.IsIntact);
        Assert.Contains(
            verification.Problems,
            problem => problem.Contains("fields have been changed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_second_export_of_the_same_parameters_is_a_new_version_of_the_same_dataset()
    {
        var harness = new Harness(root);

        var one = (await harness.ExportAsync()).Manifest!;
        var two = (await harness.ExportAsync()).Manifest!;

        Assert.Equal(one.DatasetId, two.DatasetId);
        Assert.Equal(1, one.DatasetVersion);
        Assert.Equal(2, two.DatasetVersion);

        // The reproducibility property. The version says which run this was;
        // the content hash says what the data is, and rebuilding identical
        // parameters over identical data must not change it.
        Assert.Equal(one.ContentHash, two.ContentHash);
    }

    [Fact]
    public async Task An_unknown_membership_refuses_rather_than_exporting_an_empty_market()
    {
        // The refusal U2 exists to make possible. An empty dataset would produce
        // a backtest that reports no positions and no error.
        var harness = new Harness(root, membershipKnown: false);

        var export = await harness.ExportAsync();

        Assert.False(export.Succeeded);
        Assert.Contains("is not known", export.Problem, StringComparison.Ordinal);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task A_universe_with_no_stored_bars_refuses_rather_than_writing_nothing()
    {
        var harness = new Harness(root, storeBars: false);

        var export = await harness.ExportAsync();

        Assert.False(export.Succeeded);
        Assert.Contains("empty dataset", export.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_source_nobody_has_stated_terms_for_is_recorded_as_unlicensed()
    {
        // The true position for every Vietnamese source this system reads. A
        // dataset that left with no statement attached would be one somebody
        // eventually redistributes.
        var harness = new Harness(root);

        var manifest = (await harness.ExportAsync()).Manifest!;
        var source = Assert.Single(manifest.Sources);

        Assert.Equal("TEST", source.Code);
        Assert.Equal(IDatasetLicenceRegistry.UnstatedNote, source.LicenceNote);
    }

    [Fact]
    public async Task The_manifest_carries_the_reading_the_bars_were_taken_under()
    {
        var harness = new Harness(root);

        var manifest = (await harness.ExportAsync()).Manifest!;

        Assert.Equal(Domain.CorporateActions.AnnouncementPolicy.Strict, manifest.AnnouncementPolicy);
        Assert.True(manifest.Adjusted);
        Assert.Equal(DataRules.TransformationVersion, manifest.TransformationVersion);
        Assert.Equal(DataRules.ValidationVersion, manifest.ValidationVersion);
        Assert.Equal(DataRules.AdjustmentVersion, manifest.AdjustmentVersion);
        Assert.Equal(DatasetManifest.CurrentSchemaVersion, manifest.SchemaVersion);

        // The label lives here, stamped with the instant it was valid, and
        // nowhere in the rows.
        var instrument = Assert.Single(manifest.Instruments);
        Assert.Equal("EXP", instrument.TickerAtAsOf);
    }

    [Fact]
    public async Task The_written_file_reads_back_as_parquet_with_the_columns_the_contract_names()
    {
        // Hashing bytes proves they have not changed; it does not prove they
        // are a Parquet file, nor that the columns arrived in the order the
        // schema declares. A row group written out of order still reads
        // without error, with every value one column to the left.
        var harness = new Harness(root);
        var manifest = (await harness.ExportAsync()).Manifest!;
        var path = Path.Combine(root, manifest.DatasetId, "v1", IDatasetStore.BarsFileName);

        await using var stream = File.OpenRead(path);
        await using var reader = await ParquetReader.CreateAsync(
            stream, cancellationToken: TestContext.Current.CancellationToken);

        var fields = reader.Schema.DataFields;

        Assert.Equal(
            [
                "instrument_id",
                "opened_at_utc",
                "open",
                "high",
                "low",
                "close",
                "volume",
                "turnover",
                "source",
                "revision",
                "price_factor",
                "share_factor",
            ],
            fields.Select(field => field.Name));

        using var group = reader.OpenRowGroupReader(0);

        Assert.Equal(5, group.RowCount);

        var close = new string?[group.RowCount];
        await group.ReadAsync(
            fields[5], close.AsMemory(), cancellationToken: TestContext.Current.CancellationToken);

        // Prices are text, exactly as stored. A decimal forced through binary
        // floating point stops being the number the market printed.
        Assert.All(close, value => Assert.Equal("100", value));

        var open = new string?[group.RowCount];
        await group.ReadAsync(
            fields[2], open.AsMemory(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.All(open, value => Assert.Equal("100.5", value));

        // The first bar's turnover was absent and the rest were not, so the
        // column has to carry the absence rather than a zero.
        var turnover = new string?[group.RowCount];
        await group.ReadAsync(
            fields[7], turnover.AsMemory(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(turnover[0]);
        Assert.Equal("100000", turnover[1]);
    }

    /// <summary>Wires the real export over in-memory reads and a temp directory.</summary>
    private sealed class Harness
    {
        private readonly DatasetExportService service;

        public Harness(string root, bool membershipKnown = true, bool storeBars = true)
        {
            InstrumentId = InstrumentId.New();

            var store = new ParquetDatasetStore(
                Options.Create(new DatasetOptions { Directory = root }));

            service = new DatasetExportService(
                new FakeUniverseCatalog(membershipKnown ? [InstrumentId] : null),
                new FakeInstrumentDetails(InstrumentId),
                new FakeSeries(InstrumentId, storeBars),
                store,
                store,
                new FakeBuild(),
                new FakeClock(Now),
                NullLogger<DatasetExportService>.Instance);
        }

        public InstrumentId InstrumentId { get; }

        public Task<DatasetExport> ExportAsync()
        {
            Assert.True(DatasetRequest.TryCreate(
                Vn30, To, BarInterval.OneDay, From, To, out var request, out var problem), problem);

            return service.ExportAsync(request, TestContext.Current.CancellationToken);
        }

        public Task<DatasetVerification> VerifyAsync(string datasetId, int version) =>
            service.VerifyAsync(datasetId, version, TestContext.Current.CancellationToken);
    }

    private sealed class FakeUniverseCatalog(IReadOnlyList<InstrumentId>? members) : IUniverseCatalog
    {
        public Task<UniverseConstituents> ConstituentsAsOfAsync(
            UniverseCode code,
            DateOnly asOf,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(members is null
                ? UniverseConstituents.Unknown(code, asOf, UniverseUnknownReason.NoCoverageDeclared)
                : UniverseConstituents.Known(code, asOf, members));
    }

    /// <summary>
    /// Supplies the one thing the export asks the instrument master for: the
    /// label an identifier carried.
    /// </summary>
    /// <remarks>
    /// Local rather than the shared in-memory master, which raises on the
    /// detail read because no import path exercises it. A fake that answered
    /// eleven questions to be asked one would hide which of them the export
    /// actually depends on.
    /// </remarks>
    private sealed class FakeInstrumentDetails(InstrumentId known) : IInstrumentRepository
    {
        public Task<InstrumentDetail?> FindDetailByIdAsync(
            InstrumentId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(id == known
                ? new InstrumentDetail(
                    id,
                    Ticker.Create("EXP"),
                    "Exported Company",
                    AssetType.Equity,
                    ExchangeCode.Create("HOSE"),
                    "Ho Chi Minh Stock Exchange",
                    CurrencyCode.Vnd,
                    InstrumentStatus.Listed,
                    ListedOn: null,
                    DelistedOn: null,
                    Classification: null,
                    Aliases: [])
                : null);

        public Task<Instrument?> FindByIdAsync(InstrumentId id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by the export.");

        public Task<Instrument?> FindActiveByTickerAsync(ExchangeId exchangeId, Ticker ticker, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by the export.");

        public Task<bool> IsTickerTakenAsync(ExchangeId exchangeId, Ticker ticker, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by the export.");

        public Task<IReadOnlyList<InstrumentSearchResult>> SearchAsync(InstrumentSearchCriteria criteria, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by the export.");

        public Task<IReadOnlyList<InstrumentSearchResult>> ListActiveByTickerAsync(Ticker ticker, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by the export.");

        public Task<InstrumentSearchResult?> FindResultByIdAsync(InstrumentId id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by the export.");

        public Task<InstrumentPage> ListAsync(InstrumentListCriteria criteria, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by the export.");

        public Task<InstrumentIdentifier?> FindIdentifierAsync(IdentifierValue value, SourceCode? source, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by the export.");

        public Task<IReadOnlyList<InstrumentIdentifier>> ListIdentifiersAsync(InstrumentId id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by the export.");

        public Task<IReadOnlyList<RelatedInstrument>> ListRelatedAsync(InstrumentId id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by the export.");

        public void Add(Instrument instrument) =>
            throw new NotSupportedException("The export never writes.");

        public void AddIdentifier(InstrumentIdentifier identifier) =>
            throw new NotSupportedException("The export never writes.");
    }

    private sealed class FakeBuild : IBuildIdentity
    {
        public string? Commit => "0123456789abcdef";
    }

    private sealed class FakeSeries(InstrumentId instrumentId, bool storeBars)
        : IMarketDataQueryService
    {
        public Task<BarSeries> GetSeriesAsync(
            BarQuery query,
            CancellationToken cancellationToken = default)
        {
            if (!storeBars)
            {
                return Task.FromResult(new BarSeries(
                    query.InstrumentId, query.Interval, true, []));
            }

            var bars = Enumerable.Range(0, 5)
                .Select(offset => new SeriesBar(
                    new DateTimeOffset(From.AddDays(offset).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                    100.5m,
                    101m,
                    99m,
                    100m,
                    1_000,
                    offset == 0 ? null : 100_000m,
                    SourceCode.Create("TEST"),
                    1,
                    1m,
                    1m))
                .Where(bar => query.FromUtc is not { } start || bar.OpenedAtUtc >= start)
                .Where(bar => query.ToUtc is not { } end || bar.OpenedAtUtc < end)
                .ToList();

            return Task.FromResult(new BarSeries(instrumentId, query.Interval, true, bars));
        }

        public Task<IReadOnlyList<IngestionRun>> ListRecentRunsAsync(
            InstrumentId instrumentId,
            BarInterval interval,
            int limit,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IngestionRun>>([]);
    }
}
