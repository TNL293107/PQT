using PersonalQuant.Application.CorporateActions;
using PersonalQuant.Domain.CorporateActions;
using PersonalQuant.Domain.Currencies;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.UnitTests.CorporateActions.Fakes;
using PersonalQuant.UnitTests.Instruments.Fakes;
using PersonalQuant.UnitTests.MarketData.Fakes;

namespace PersonalQuant.UnitTests.CorporateActions;

/// <summary>
/// Verifies that a sweep is recorded against the right issuer, append-only (ADR-032).
/// </summary>
public sealed class CorporateActionCoverageImportServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly From = new(2023, 7, 1);
    private static readonly DateOnly Swept = new(2026, 10, 21);

    [Fact]
    public async Task A_sweep_is_recorded_and_a_restatement_changes_nothing()
    {
        var harness = new Harness();
        harness.State(new ProviderCorporateActionCoverage("VNM.HM", From, Swept, "VSDC notices", 2));

        var first = await harness.ImportAsync();
        var second = await harness.ImportAsync();

        Assert.Equal((1, 0), (first.Recorded, first.AlreadyHeld));
        Assert.Equal((0, 1), (second.Recorded, second.AlreadyHeld));
        var record = Assert.Single(harness.Coverage.Records);
        Assert.Equal((harness.Issuer, From, Swept, "VSDC notices"), (record.InstrumentId, record.CompleteFrom, record.SweptOn, record.Source));
    }

    [Fact]
    public async Task A_later_sweep_is_a_new_record_beside_the_first()
    {
        var harness = new Harness();
        harness.State(new ProviderCorporateActionCoverage("VNM.HM", From, Swept, "VSDC notices", 2));
        await harness.ImportAsync();

        harness.State(
            new ProviderCorporateActionCoverage("VNM.HM", From, Swept, "VSDC notices", 2),
            new ProviderCorporateActionCoverage("VNM.HM", From, Swept.AddMonths(3), "VSDC notices", 3));
        var report = await harness.ImportAsync();

        Assert.Equal((1, 1), (report.Recorded, report.AlreadyHeld));
        Assert.Equal(2, harness.Coverage.Records.Count);
    }

    [Fact]
    public async Task A_changed_claim_for_a_held_sweep_is_refused_not_edited()
    {
        var harness = new Harness();
        harness.State(new ProviderCorporateActionCoverage("VNM.HM", From, Swept, "VSDC notices", 2));
        await harness.ImportAsync();

        harness.State(new ProviderCorporateActionCoverage("VNM.HM", new DateOnly(2022, 1, 1), Swept, "VSDC notices", 2));
        var report = await harness.ImportAsync();

        Assert.Contains("is not edited", Assert.Single(report.Rejections).Reason, StringComparison.Ordinal);
        Assert.Equal(From, Assert.Single(harness.Coverage.Records).CompleteFrom);
    }

    [Theory]
    [InlineData("VNM", "not a symbol")]
    [InlineData("XYZ.HM", "not a symbol")]
    public async Task A_symbol_the_source_never_recorded_is_refused(string symbol, string reason)
    {
        // The bare ticker is not the FILE source's alias: no guess is made.
        var harness = new Harness();
        harness.State(new ProviderCorporateActionCoverage(symbol, From, Swept, "VSDC notices", 2));

        var report = await harness.ImportAsync();

        Assert.Contains(reason, Assert.Single(report.Rejections).Reason, StringComparison.Ordinal);
        Assert.Empty(harness.Coverage.Records);
    }

    [Fact]
    public async Task A_row_with_a_missing_date_or_a_sweep_before_its_start_is_refused_by_line()
    {
        var harness = new Harness();
        harness.State(
            new ProviderCorporateActionCoverage("VNM.HM", null, Swept, "VSDC notices", 2),
            new ProviderCorporateActionCoverage("VNM.HM", Swept, From, "VSDC notices", 3),
            new ProviderCorporateActionCoverage("VNM.HM", From, Swept, null, 4));

        var report = await harness.ImportAsync();

        Assert.Equal([2, 3, 4], report.Rejections.Select(rejection => rejection.LineNumber));
        Assert.Empty(harness.Coverage.Records);
    }

    [Fact]
    public void A_sweep_covers_a_stretch_only_inside_it()
    {
        var sweep = CorporateActionCoverage.Record(InstrumentId.New(), From, Swept, "VSDC notices", Now);

        Assert.True(sweep.Covers(From, Swept));
        Assert.False(sweep.Covers(From.AddDays(-1), Swept));
        Assert.False(sweep.Covers(From, Swept.AddDays(1)));
    }

    /// <summary>One instrument aliased as VNM.HM by the FILE source, and the real service.</summary>
    private sealed class Harness
    {
        private static readonly SourceCode File = SourceCode.Create("FILE");

        private readonly InMemoryInstrumentMaster _master = new();
        private readonly ScriptedProvider _provider = new();

        public Harness()
        {
            var entity = Instrument.Register(
                ExchangeId.New(), Ticker.Create("VNM"), "Vinamilk", AssetType.Equity, CurrencyCode.Vnd, Now);
            _master.Seed(entity);
            _master.Seed(InstrumentIdentifier.Record(
                entity.Id, IdentifierValue.Create(IdentifierScheme.ProviderSymbol, "VNM.HM"), File, Now));
            Issuer = entity.Id;
        }

        public InstrumentId Issuer { get; }

        public FakeCorporateActionCoverageRepository Coverage { get; } = new();

        public void State(params ProviderCorporateActionCoverage[] rows) => _provider.Rows = rows;

        public Task<CorporateActionCoverageImportReport> ImportAsync() =>
            new CorporateActionCoverageImportService([_provider], _master, Coverage, new FakeUnitOfWork(), new FakeClock(Now))
                .ImportAsync(TestContext.Current.CancellationToken);
    }

    private sealed class ScriptedProvider : ICorporateActionCoverageProvider
    {
        public IReadOnlyList<ProviderCorporateActionCoverage> Rows { get; set; } = [];

        public SourceCode Code { get; } = SourceCode.Create("FILE");

        public Task<IReadOnlyList<ProviderCorporateActionCoverage>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Rows);
    }
}
