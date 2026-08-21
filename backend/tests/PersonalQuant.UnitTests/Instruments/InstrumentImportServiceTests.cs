using Microsoft.Extensions.Logging.Abstractions;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.UnitTests.Instruments.Fakes;
using PersonalQuant.UnitTests.MarketData.Fakes;

namespace PersonalQuant.UnitTests.Instruments;

/// <summary>
/// Verifies the provider import pipeline: normalise the symbol, deduplicate,
/// record the alias, reject what cannot be reconciled.
/// </summary>
/// <remarks>
/// This is where the instrument master's promise is kept or broken — that
/// every provider's spelling of a security maps to one canonical identifier.
/// </remarks>
public sealed class InstrumentImportServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 26, 3, 0, 0, TimeSpan.Zero);
    private static readonly SourceCode Source = SourceCode.Create("VENDOR");

    [Fact]
    public async Task A_first_import_creates_the_securities_and_records_their_symbols()
    {
        var harness = new Harness();

        // Act
        var report = await harness.ImportAsync(
            Row("FPT.HM", "FPT Corporation"),
            Row("VNM.HM", "Vietnam Dairy Products"));

        // Assert
        Assert.Equal(2, report.RowsRead);
        Assert.Equal(2, report.Created);
        Assert.Equal(0, report.Matched);
        Assert.Empty(report.Rejections);
        Assert.Equal(2, harness.Master.Instruments.Count);
        Assert.Equal(2, harness.Master.Identifiers.Count);
        Assert.All(
            harness.Master.Identifiers,
            alias => Assert.Equal(IdentifierScheme.ProviderSymbol, alias.Scheme));
    }

    [Fact]
    public async Task Importing_the_same_list_twice_creates_nothing_the_second_time()
    {
        // The property the whole workstream exists for. A second run that
        // created everything again would mean deduplication does not work.
        var harness = new Harness();
        await harness.ImportAsync(Row("FPT.HM", "FPT Corporation"));

        // Act
        var report = await harness.ImportAsync(Row("FPT.HM", "FPT Corporation"));

        // Assert
        Assert.Equal(0, report.Created);
        Assert.Equal(1, report.Matched);
        Assert.Equal(0, report.Enriched);
        Assert.Single(harness.Master.Instruments);
        Assert.Single(harness.Master.Identifiers);
    }

    [Fact]
    public async Task Two_spellings_of_one_security_in_one_import_create_it_once()
    {
        // Nothing has been committed while the run is in flight, so the second
        // spelling can only find the first through the run's own index.
        var harness = new Harness();

        // Act
        var report = await harness.ImportAsync(
            Row("FPT", "FPT Corporation"),
            Row("FPT.HM", "FPT Corporation"));

        // Assert
        Assert.Equal(1, report.Created);
        Assert.Equal(1, report.Enriched);
        Assert.Single(harness.Master.Instruments);

        // Both spellings are recorded, so either resolves next time.
        Assert.Equal(2, harness.Master.Identifiers.Count);
    }

    [Fact]
    public async Task A_repeated_symbol_within_one_import_is_rejected()
    {
        var harness = new Harness();

        // Act
        var report = await harness.ImportAsync(
            Row("FPT.HM", "FPT Corporation"),
            Row("FPT.HM", "FPT Corporation"));

        // Assert
        Assert.Equal(1, report.Created);
        Assert.Equal(
            InstrumentImportRejectionReason.DuplicateWithinImport,
            Assert.Single(report.Rejections).Reason);
    }

    [Fact]
    public async Task A_second_provider_spelling_attaches_to_the_instrument_the_first_created()
    {
        // The done-condition of the phase: FPT.HM from one vendor and FPT:VN
        // from another are the same canonical security.
        var harness = new Harness();
        await harness.ImportAsync(Row("FPT.HM", "FPT Corporation"));

        var second = harness.WithSource(SourceCode.Create("OTHER"));

        // Act
        var report = await second.ImportAsync(Row("FPT:VN", "FPT Corp"));

        // Assert
        Assert.Equal(0, report.Created);
        Assert.Equal(1, report.Enriched);
        Assert.Single(harness.Master.Instruments);
        Assert.Equal(2, harness.Master.Identifiers.Count);
    }

    [Fact]
    public async Task An_isin_matches_a_security_whose_ticker_has_changed()
    {
        // A ticker change preserves identity, and the ISIN is what proves it.
        var harness = new Harness();
        await harness.ImportAsync(Row("FPT.HM", "FPT Corporation", isin: Isin));

        // Act — the same issue, now trading under a different ticker.
        var report = await harness.ImportAsync(Row("FPT2.HM", "FPT Corporation", isin: Isin));

        // Assert
        Assert.Equal(0, report.Created);
        Assert.Equal(1, report.Enriched);
        Assert.Single(harness.Master.Instruments);
    }

    [Fact]
    public async Task A_row_whose_identifiers_and_symbol_disagree_is_rejected()
    {
        // Resolving it either way would merge two securities or split one.
        var harness = new Harness();
        await harness.ImportAsync(
            Row("FPT.HM", "FPT Corporation", isin: Isin),
            Row("VNM.HM", "Vietnam Dairy Products"));

        // Act — VNM's own symbol resolves to VNM, but the ISIN says FPT.
        var report = await harness.ImportAsync(Row("VNM.HM", "Vietnam Dairy", isin: Isin));

        // Assert
        Assert.Equal(0, report.Created);
        Assert.Equal(
            InstrumentImportRejectionReason.ConflictingIdentity,
            Assert.Single(report.Rejections).Reason);
    }

    [Fact]
    public async Task A_row_naming_no_venue_is_rejected_rather_than_guessed_at()
    {
        var harness = new Harness();

        // Act — no exchange column and no venue decoration on the symbol.
        var report = await harness.ImportAsync(Row("FPT", "FPT Corporation", exchange: null));

        // Assert
        Assert.Equal(0, report.Created);
        Assert.Equal(
            InstrumentImportRejectionReason.UnknownExchange,
            Assert.Single(report.Rejections).Reason);
    }

    [Fact]
    public async Task A_row_naming_a_venue_the_system_does_not_hold_is_rejected()
    {
        var harness = new Harness();

        var report = await harness.ImportAsync(
            Row("ABC.HM", "A Company", exchange: "NASDAQ"));

        Assert.Equal(
            InstrumentImportRejectionReason.UnknownExchange,
            Assert.Single(report.Rejections).Reason);
    }

    [Fact]
    public async Task A_stated_exchange_beats_the_symbols_decoration()
    {
        // A vendor's suffix can lag an exchange transfer by months; the row's
        // own field is the more considered answer.
        var harness = new Harness();

        // Act
        await harness.ImportAsync(Row("FPT.HM", "FPT Corporation", exchange: "HNX"));

        // Assert
        var instrument = Assert.Single(harness.Master.Instruments);
        Assert.Equal(harness.Hnx, instrument.ExchangeId);
    }

    [Fact]
    public async Task A_malformed_identifier_is_rejected_without_touching_the_master()
    {
        var harness = new Harness();

        var report = await harness.ImportAsync(
            Row("FPT.HM", "FPT Corporation", isin: "US0378331004"));

        Assert.Equal(
            InstrumentImportRejectionReason.InvalidIdentifier,
            Assert.Single(report.Rejections).Reason);
        Assert.Empty(harness.Master.Instruments);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_row_with_no_name_is_rejected(string name)
    {
        var harness = new Harness();

        var report = await harness.ImportAsync(Row("FPT.HM", name));

        Assert.Equal(
            InstrumentImportRejectionReason.UnusableName,
            Assert.Single(report.Rejections).Reason);
    }

    [Fact]
    public async Task An_unreadable_symbol_is_rejected()
    {
        var harness = new Harness();

        var report = await harness.ImportAsync(Row("ABC.DEF", "An Ambiguous Company"));

        Assert.Equal(
            InstrumentImportRejectionReason.UnreadableSymbol,
            Assert.Single(report.Rejections).Reason);
    }

    [Fact]
    public async Task One_bad_row_does_not_stop_the_others()
    {
        // A symbol list is thousands of rows and some of them are always
        // wrong. Throwing would mean importing nothing.
        var harness = new Harness();

        var report = await harness.ImportAsync(
            Row("FPT.HM", "FPT Corporation"),
            Row("ABC.DEF", "An Ambiguous Company"),
            Row("VNM.HM", "Vietnam Dairy Products"));

        Assert.Equal(2, report.Created);
        Assert.Single(report.Rejections);
        Assert.Equal(3, report.RowsRead);
    }

    [Fact]
    public async Task A_created_instrument_takes_the_listing_date_the_source_carries()
    {
        var harness = new Harness();
        var listedOn = new DateOnly(2006, 12, 13);

        await harness.ImportAsync(Row("FPT.HM", "FPT Corporation", listedOn: listedOn));

        Assert.Equal(listedOn, Assert.Single(harness.Master.Instruments).ListedOn);
    }

    [Fact]
    public async Task A_created_instrument_without_a_listing_date_is_still_listed()
    {
        // Refusing to record that a security trades because its first trading
        // day is unknown would be a worse answer than leaving the date empty.
        var harness = new Harness();

        await harness.ImportAsync(Row("FPT.HM", "FPT Corporation"));

        var instrument = Assert.Single(harness.Master.Instruments);
        Assert.Equal(InstrumentStatus.Listed, instrument.Status);
        Assert.Null(instrument.ListedOn);
    }

    [Fact]
    public async Task An_unrecognised_asset_class_is_left_unspecified_rather_than_guessed()
    {
        var harness = new Harness();

        await harness.ImportAsync(Row("FPT.HM", "FPT Corporation", assetType: "COMMON STOCK"));

        Assert.Equal(AssetType.Unspecified, Assert.Single(harness.Master.Instruments).AssetType);
    }

    [Fact]
    public async Task A_stated_asset_class_is_taken()
    {
        var harness = new Harness();

        await harness.ImportAsync(Row("VNINDEX.HM", "VN-Index", assetType: "index"));

        Assert.Equal(AssetType.Index, Assert.Single(harness.Master.Instruments).AssetType);
    }

    [Fact]
    public async Task An_import_does_not_rewrite_what_the_master_already_holds()
    {
        // Correcting a record is a deliberate act, not something the next
        // nightly import does silently. This is the system of record.
        var harness = new Harness();
        await harness.ImportAsync(Row("FPT.HM", "FPT Corporation"));

        // Act
        await harness.ImportAsync(Row("FPT.HM", "A Different Name Entirely"));

        // Assert
        Assert.Equal("FPT Corporation", Assert.Single(harness.Master.Instruments).Name);
    }

    [Fact]
    public async Task An_unregistered_source_is_refused()
    {
        var harness = new Harness();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.ImportAsync(SourceCode.Create("ABSENT")));
    }

    [Fact]
    public async Task The_run_commits_once()
    {
        var harness = new Harness();

        await harness.ImportAsync(Row("FPT.HM", "FPT Corporation"), Row("VNM.HM", "Vinamilk"));

        Assert.Equal(1, harness.UnitOfWork.SaveCount);
    }

    /// <summary>A synthetic ISIN, valid by check digit and belonging to nobody.</summary>
    private const string Isin = "AU0000XVGZA3";

    private static ProviderInstrument Row(
        string symbol,
        string name,
        string? exchange = "HOSE",
        string? assetType = null,
        string? isin = null,
        DateOnly? listedOn = null) =>
        new(symbol, name, exchange, assetType, Currency: null, isin, Figi: null, listedOn);

    /// <summary>Wires the real import service over an in-memory master.</summary>
    private sealed class Harness
    {
        private readonly InMemoryExchanges _exchanges = new();

        public Harness()
        {
            Hose = _exchanges.Add("HOSE", Now);
            Hnx = _exchanges.Add("HNX", Now);
            Master = new InMemoryInstrumentMaster();
            UnitOfWork = new FakeUnitOfWork();
        }

        public InMemoryInstrumentMaster Master { get; }

        public FakeUnitOfWork UnitOfWork { get; }

        public ExchangeId Hose { get; }

        public ExchangeId Hnx { get; }

        public Harness WithSource(SourceCode source) => new(this, source);

        private Harness(Harness other, SourceCode source)
        {
            _exchanges = other._exchanges;
            Master = other.Master;
            UnitOfWork = other.UnitOfWork;
            Hose = other.Hose;
            Hnx = other.Hnx;
            OverrideSource = source;
        }

        private SourceCode? OverrideSource { get; }

        public Task<InstrumentImportReport> ImportAsync(params ProviderInstrument[] rows) =>
            RunAsync(null, rows);

        public Task<InstrumentImportReport> ImportAsync(SourceCode source) =>
            RunAsync(source, []);

        private async Task<InstrumentImportReport> RunAsync(
            SourceCode? requested,
            ProviderInstrument[] rows)
        {
            var provider = new ScriptedInstrumentProvider(OverrideSource ?? Source, rows);

            var service = new InstrumentImportService(
                [provider],
                Master,
                _exchanges,
                UnitOfWork,
                new FakeClock(Now),
                NullLogger<InstrumentImportService>.Instance);

            var report = await service.ImportAsync(
                requested, TestContext.Current.CancellationToken);

            // The real unit of work makes the staged writes visible; the fake
            // only counts, so the test does it explicitly.
            Master.Commit();

            return report;
        }
    }
}
