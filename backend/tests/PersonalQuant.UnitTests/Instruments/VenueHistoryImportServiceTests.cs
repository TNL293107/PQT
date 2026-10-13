using PersonalQuant.Application.Instruments;
using PersonalQuant.Domain.Currencies;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.UnitTests.CorporateActions.Fakes;
using PersonalQuant.UnitTests.Instruments.Fakes;
using PersonalQuant.UnitTests.MarketData.Fakes;

namespace PersonalQuant.UnitTests.Instruments;

/// <summary>
/// Verifies that a venue history is recorded whole or not at all, and that
/// recording it re-judges breaches on the venue each session traded on.
/// </summary>
public sealed class VenueHistoryImportServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 2, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Moved = new(2025, 1, 20);

    [Fact]
    public async Task A_history_that_tiles_time_is_recorded_and_a_restatement_changes_nothing()
    {
        var harness = new Harness();
        harness.State(("UPCOM", null, Moved), ("HOSE", Moved, null));

        var first = await harness.ImportAsync();
        var second = await harness.ImportAsync();

        Assert.Equal((1, 0), (first.InstrumentsRecorded, first.InstrumentsUnchanged));
        Assert.Equal((0, 1), (second.InstrumentsRecorded, second.InstrumentsUnchanged));
        Assert.Equal(2, harness.History.Spells.Count);
        Assert.Empty(second.Rejections);
    }

    [Theory]
    [InlineData("2025-01-21")]
    [InlineData("2025-01-17")]
    public async Task A_gap_or_an_overlap_between_spells_is_refused_whole(string secondStart)
    {
        var harness = new Harness();
        harness.State(("UPCOM", null, Moved), ("HOSE", DateOnly.Parse(secondStart, System.Globalization.CultureInfo.InvariantCulture), null));

        var report = await harness.ImportAsync();

        var rejection = Assert.Single(report.Rejections);
        Assert.Equal(VenueHistoryRejectionReason.NotContiguous, rejection.Reason);
        Assert.Empty(harness.History.Spells);
    }

    [Fact]
    public async Task A_history_ending_somewhere_the_master_does_not_have_it_is_refused()
    {
        // The master has the name on HOSE; a history ending on HNX would make
        // every current session's band the wrong one.
        var harness = new Harness();
        harness.State(("UPCOM", null, Moved), ("HNX", Moved, null));

        var rejection = Assert.Single((await harness.ImportAsync()).Rejections);

        Assert.Equal(VenueHistoryRejectionReason.DisagreesWithCurrentVenue, rejection.Reason);
        Assert.Contains("pqt instrument transfer", rejection.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_history_contradicting_the_recorded_one_is_refused()
    {
        var harness = new Harness();
        harness.State(("UPCOM", null, Moved), ("HOSE", Moved, null));
        await harness.ImportAsync();

        harness.State(("UPCOM", null, Moved.AddDays(7)), ("HOSE", Moved.AddDays(7), null));
        var rejection = Assert.Single((await harness.ImportAsync()).Rejections);

        Assert.Equal(VenueHistoryRejectionReason.ContradictsRecordedHistory, rejection.Reason);
        Assert.Equal(Moved, harness.History.Spells.Single(spell => spell.EffectiveTo is not null).EffectiveTo);
    }

    [Fact]
    public async Task Recording_the_history_explains_a_breach_inside_the_old_venues_band_and_no_other()
    {
        // −12% on UPCOM is an ordinary day under its ±15% band; −20% is not.
        var harness = new Harness();
        var ordinary = harness.Breach(new DateOnly(2024, 3, 5), 100m, 88m);
        var real = harness.Breach(new DateOnly(2024, 4, 16), 100m, 80m);
        harness.State(("UPCOM", null, Moved), ("HOSE", Moved, null));

        var report = await harness.ImportAsync();

        Assert.Equal(1, report.FindingsRejudged);
        Assert.False(ordinary.IsOpen);
        Assert.Contains("UPCOM", ordinary.Resolution, StringComparison.Ordinal);
        Assert.True(real.IsOpen);
    }

    [Fact]
    public async Task An_unknown_symbol_is_refused()
    {
        var harness = new Harness();
        harness.State(("UPCOM", null, Moved), ("HOSE", Moved, null));

        var report = await harness.ImportAsync(symbol: "NOPE.HM");

        Assert.Equal(VenueHistoryRejectionReason.UnknownInstrument, Assert.Single(report.Rejections).Reason);
    }

    /// <summary>Three venues, one instrument on HOSE aliased as BSR.HM, and the real service.</summary>
    private sealed class Harness
    {
        private static readonly SourceCode File = SourceCode.Create("FILE");

        private readonly InMemoryExchanges _exchanges = new();
        private readonly InMemoryInstrumentMaster _master = new();
        private readonly FakeQualityRepository _issues = new();
        private readonly FakeBarRepository _bars = new();
        private readonly ScriptedProvider _provider = new();
        private readonly InstrumentId _instrument;

        public Harness()
        {
            var hose = _exchanges.Add("HOSE", Now, 7m);
            _exchanges.Add("HNX", Now, 10m);
            _exchanges.Add("UPCOM", Now, 15m);

            var instrument = Instrument.Register(
                hose, Ticker.Create("BSR"), "Binh Son Refining", AssetType.Equity, CurrencyCode.Vnd, Now);
            instrument.List(Now);
            _master.Seed(instrument);
            _master.Seed(InstrumentIdentifier.Record(
                instrument.Id, IdentifierValue.Create(IdentifierScheme.ProviderSymbol, "BSR.HM"), File, Now));
            _instrument = instrument.Id;
        }

        public InMemoryVenueHistory History { get; } = new();

        public void State(params (string Venue, DateOnly? From, DateOnly? To)[] spells) =>
            _provider.Rows = [.. spells.Select((spell, index) =>
                new ProviderVenueSpell("BSR.HM", spell.Venue, spell.From, spell.To, index + 2))];

        /// <summary>Stores a move between two sessions and an open breach for it.</summary>
        public DataQualityIssue Breach(DateOnly session, decimal before, decimal after)
        {
            _bars.AddRange([Bar(session.AddDays(-1), before), Bar(session, after)]);

            var issue = DataQualityIssue.Raise(
                _instrument, BarInterval.OneDay, Midnight(session), DataQualityIssueKind.PriceLimitBreach,
                "The close moved beyond HOSE's ±7% band.", DataRules.ValidationVersion, Now);

            _issues.Seed(issue);
            return issue;
        }

        public Task<VenueHistoryImportReport> ImportAsync(string? symbol = null)
        {
            if (symbol is not null)
            {
                _provider.Rows = [.. _provider.Rows.Select(row => row with { Symbol = symbol })];
            }

            return new VenueHistoryImportService(
                    [_provider], _master, _exchanges, History, _bars, _issues, new FakeUnitOfWork(), new FakeClock(Now))
                .ImportAsync(TestContext.Current.CancellationToken);
        }

        private OhlcvBar Bar(DateOnly session, decimal close) =>
            OhlcvBar.Record(
                _instrument, BarInterval.OneDay, Midnight(session),
                Price.Create(close), Price.Create(close), Price.Create(close), Price.Create(close),
                1_000, null, File, Now);

        private static DateTimeOffset Midnight(DateOnly date) =>
            new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    }

    private sealed class ScriptedProvider : IVenueHistoryProvider
    {
        public IReadOnlyList<ProviderVenueSpell> Rows { get; set; } = [];

        public SourceCode Code { get; } = SourceCode.Create("FILE");

        public Task<IReadOnlyList<ProviderVenueSpell>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Rows);
    }
}
