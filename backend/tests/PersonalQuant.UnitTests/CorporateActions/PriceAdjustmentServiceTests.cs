using Microsoft.Extensions.Logging.Abstractions;
using PersonalQuant.Application.CorporateActions;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Domain.CorporateActions;
using PersonalQuant.Domain.Currencies;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.UnitTests.CorporateActions.Fakes;
using PersonalQuant.UnitTests.Instruments.Fakes;
using PersonalQuant.UnitTests.MarketData.Fakes;

namespace PersonalQuant.UnitTests.CorporateActions;

/// <summary>
/// Verifies the engine that turns recorded actions into stored factors, and
/// the series that is read through them.
/// </summary>
/// <remarks>
/// Monday 2026-08-03 through Friday 2026-08-07, with a two-for-one split going
/// ex on the Wednesday. Prices are flat at 100 so the effect of the adjustment
/// is unmistakable: everything before Wednesday should read 50.
/// </remarks>
public sealed class PriceAdjustmentServiceTests
{
    private static readonly DateTimeOffset Monday = new(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Wednesday = new(2026, 8, 5);
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 1, 0, 0, TimeSpan.Zero);
    private static readonly SourceCode Source = SourceCode.Create("TEST");

    [Fact]
    public async Task A_split_produces_one_factor_measured_against_the_close_before_it()
    {
        var harness = new Harness();
        harness.StoreWeek();
        harness.Record(CorporateActionType.StockSplit, Wednesday, ratio: 2m);

        // Act
        var run = await harness.RecomputeAsync();

        // Assert
        Assert.Equal(1, run.ActionsConsidered);
        Assert.Equal(1, run.Computed);
        Assert.Empty(run.Rejections);

        var adjustment = Assert.Single(harness.Actions.Adjustments);
        Assert.Equal(0.5m, adjustment.Factor.Price);
        Assert.Equal(2m, adjustment.Factor.Shares);

        // Tuesday's close, not Wednesday's: the last price with the
        // entitlement attached.
        Assert.Equal(100m, adjustment.ReferenceClose.Value);
    }

    [Fact]
    public async Task Only_the_bars_before_the_ex_date_are_rescaled()
    {
        // The comparison the whole adjustment rests on. A bar opening on the
        // ex-date already trades without the entitlement.
        var harness = new Harness();
        harness.StoreWeek();
        harness.Record(CorporateActionType.StockSplit, Wednesday, ratio: 2m);
        await harness.RecomputeAsync();

        // Act
        var series = await harness.ReadAsync(adjusted: true);

        // Assert
        Assert.True(series.Adjusted);
        Assert.Equal([50m, 50m, 100m, 100m, 100m], series.Bars.Select(bar => bar.Close));
        Assert.Equal([2_000, 2_000, 1_000, 1_000, 1_000], series.Bars.Select(bar => bar.Volume));
    }

    [Fact]
    public async Task The_raw_series_is_never_touched()
    {
        // The point of storing factors beside the bars rather than rewriting
        // them: what printed is still available.
        var harness = new Harness();
        harness.StoreWeek();
        harness.Record(CorporateActionType.StockSplit, Wednesday, ratio: 2m);
        await harness.RecomputeAsync();

        // Act
        var raw = await harness.ReadAsync(adjusted: false);

        // Assert
        Assert.False(raw.Adjusted);
        Assert.All(raw.Bars, bar => Assert.Equal(100m, bar.Close));
        Assert.All(raw.Bars, bar => Assert.False(bar.IsAdjusted));
    }

    [Fact]
    public async Task Turnover_is_never_rescaled()
    {
        // It is the cash that changed hands, not a per-share quantity.
        var harness = new Harness();
        harness.StoreWeek(turnover: 100_000m);
        harness.Record(CorporateActionType.StockSplit, Wednesday, ratio: 2m);
        await harness.RecomputeAsync();

        var series = await harness.ReadAsync(adjusted: true);

        Assert.All(series.Bars, bar => Assert.Equal(100_000m, bar.Turnover));
    }

    [Fact]
    public async Task Two_actions_compound_on_the_bars_before_both()
    {
        // A bar before the earlier ex-date carries both factors; one between
        // them carries only the later.
        var harness = new Harness();
        harness.StoreWeek();
        harness.Record(CorporateActionType.StockSplit, new DateOnly(2026, 8, 4), ratio: 2m);
        harness.Record(CorporateActionType.StockSplit, new DateOnly(2026, 8, 6), ratio: 2m);

        await harness.RecomputeAsync();

        // Act
        var series = await harness.ReadAsync(adjusted: true);

        // Assert — Monday carries ×0.25, Tuesday and Wednesday ×0.5, then raw.
        Assert.Equal([25m, 50m, 50m, 100m, 100m], series.Bars.Select(bar => bar.Close));
    }

    [Fact]
    public async Task Recomputing_twice_changes_nothing_the_second_time()
    {
        var harness = new Harness();
        harness.StoreWeek();
        harness.Record(CorporateActionType.StockSplit, Wednesday, ratio: 2m);

        await harness.RecomputeAsync();

        // Act
        var second = await harness.RecomputeAsync();

        // Assert
        Assert.Equal(0, second.Computed);
        Assert.Equal(1, second.Unchanged);
        Assert.Single(harness.Actions.Adjustments);
    }

    [Fact]
    public async Task An_amended_action_replaces_its_factor()
    {
        // A source restating a ratio makes the factor derived from it describe
        // an event that has since changed.
        var harness = new Harness();
        harness.StoreWeek();
        var action = harness.Record(CorporateActionType.StockSplit, Wednesday, ratio: 2m);
        await harness.RecomputeAsync();

        action.Amend(Wednesday, ratio: 4m, cashAmount: null, "Restated.", Now);

        // Act
        var run = await harness.RecomputeAsync();

        // Assert
        Assert.Equal(1, run.Computed);
        Assert.Equal(0, run.Unchanged);
        Assert.Equal(0.25m, Assert.Single(harness.Actions.Adjustments).Factor.Price);
    }

    [Fact]
    public async Task A_cancelled_action_has_its_factor_removed()
    {
        // Cancelled rather than deleted, so the series stops being adjusted for
        // it while the record of the announcement survives.
        var harness = new Harness();
        harness.StoreWeek();
        var action = harness.Record(CorporateActionType.StockSplit, Wednesday, ratio: 2m);
        await harness.RecomputeAsync();

        action.Cancel("The issuer withdrew it.", Now);

        // Act
        var run = await harness.RecomputeAsync();

        // Assert
        Assert.Equal(1, run.Removed);
        Assert.Empty(harness.Actions.Adjustments);

        var series = await harness.ReadAsync(adjusted: true);
        Assert.All(series.Bars, bar => Assert.Equal(100m, bar.Close));
    }

    [Fact]
    public async Task An_action_with_no_price_before_it_is_reported_rather_than_skipped()
    {
        // Usually an action predating the ingested history: the series is
        // correct from the ex-date onwards and has nothing earlier to rescale.
        var harness = new Harness();
        harness.StoreWeek();
        harness.Record(CorporateActionType.StockSplit, new DateOnly(2020, 1, 6), ratio: 2m);

        // Act
        var run = await harness.RecomputeAsync();

        // Assert
        var rejection = Assert.Single(run.Rejections);
        Assert.Contains("no close to measure against", rejection.Detail, StringComparison.Ordinal);
        Assert.Empty(harness.Actions.Adjustments);
    }

    [Fact]
    public async Task A_dividend_in_the_wrong_unit_is_reported_rather_than_applied()
    {
        // The rejection that matters: a factor of zero would erase the history
        // it was meant to rescale.
        var harness = new Harness();
        harness.StoreWeek();
        harness.Record(CorporateActionType.CashDividend, Wednesday, cashAmount: 500m);

        var run = await harness.RecomputeAsync();

        Assert.Single(run.Rejections);
        Assert.Empty(harness.Actions.Adjustments);
    }

    [Fact]
    public async Task An_action_that_rescales_nothing_produces_no_factor()
    {
        var harness = new Harness();
        harness.StoreWeek();
        harness.Record(CorporateActionType.SymbolChange, Wednesday);

        var run = await harness.RecomputeAsync();

        Assert.Equal(0, run.Computed);
        Assert.Empty(run.Rejections);
        Assert.Empty(harness.Actions.Adjustments);
    }

    [Fact]
    public async Task An_action_explains_the_price_limit_finding_on_its_ex_date()
    {
        // The loop Phase 3 left open. A breach on an ex-date is the
        // discontinuity the action caused.
        var harness = new Harness();
        harness.StoreWeek();
        harness.RaiseBreach(Wednesday);
        harness.Record(CorporateActionType.StockSplit, Wednesday, ratio: 2m);

        // Act
        var run = await harness.RecomputeAsync();

        // Assert
        Assert.Equal(1, run.IssuesExplained);

        var issue = Assert.Single(harness.Issues.All);
        Assert.Equal(DataQualityIssueStatus.Explained, issue.Status);
        Assert.Contains("StockSplit", issue.Resolution, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_finding_on_another_day_is_left_alone()
    {
        var harness = new Harness();
        harness.StoreWeek();
        harness.RaiseBreach(new DateOnly(2026, 8, 4));
        harness.Record(CorporateActionType.StockSplit, Wednesday, ratio: 2m);

        var run = await harness.RecomputeAsync();

        Assert.Equal(0, run.IssuesExplained);
        Assert.True(Assert.Single(harness.Issues.All).IsOpen);
    }

    [Fact]
    public async Task A_series_with_no_actions_reads_back_unadjusted()
    {
        // Which is the same thing as a series whose actions rescale nothing —
        // hence the count of rescaled bars beside the flag.
        var harness = new Harness();
        harness.StoreWeek();

        var run = await harness.RecomputeAsync();
        var series = await harness.ReadAsync(adjusted: true);

        Assert.Equal(0, run.ActionsConsidered);
        Assert.True(series.Adjusted);
        Assert.All(series.Bars, bar => Assert.False(bar.IsAdjusted));
    }

    [Fact]
    public async Task An_action_the_market_had_not_been_told_about_does_not_rescale_an_as_of_read()
    {
        // The look-ahead U4 closes. Prices were already read as of the instant;
        // without this the factors applied to them are today's, and a strategy
        // simulated on the Monday sees a split announced on the Tuesday.
        var harness = new Harness();
        harness.StoreWeek();
        harness.ObserveWeek();

        var action = harness.Record(CorporateActionType.StockSplit, Wednesday, ratio: 2m);
        action.Schedule(null, null, new DateOnly(2026, 8, 4), Now);
        await harness.RecomputeAsync();

        // Act - as of the Monday, a day before the announcement.
        var series = await harness.ReadAsOfAsync(Monday, AnnouncementPolicy.Strict);

        // Assert
        Assert.Equal(0, series.AdjustmentsApplied);
        Assert.Equal(1, series.AdjustmentsWithheld);
        Assert.All(series.Bars, bar => Assert.Equal(100m, bar.Close));
    }

    [Fact]
    public async Task An_action_already_announced_rescales_an_as_of_read()
    {
        var harness = new Harness();
        harness.StoreWeek();
        harness.ObserveWeek();

        var action = harness.Record(CorporateActionType.StockSplit, Wednesday, ratio: 2m);
        action.Schedule(null, null, new DateOnly(2026, 8, 4), Now);
        await harness.RecomputeAsync();

        // Act - as of the Friday, well after the announcement.
        var series = await harness.ReadAsOfAsync(
            new DateTimeOffset(2026, 8, 7, 0, 0, 0, TimeSpan.Zero), AnnouncementPolicy.Strict);

        // Assert
        Assert.Equal(1, series.AdjustmentsApplied);
        Assert.Equal(0, series.AdjustmentsWithheld);
        Assert.Equal(50m, series.Bars[0].Close);
    }

    [Fact]
    public async Task An_action_announced_on_the_as_of_day_itself_is_applied()
    {
        // Inclusive, because it was public that day. Excluding it would
        // misstate what a strategy trading that session could read.
        var harness = new Harness();
        harness.StoreWeek();
        harness.ObserveWeek();

        var action = harness.Record(CorporateActionType.StockSplit, Wednesday, ratio: 2m);
        action.Schedule(null, null, new DateOnly(2026, 8, 4), Now);
        await harness.RecomputeAsync();

        var series = await harness.ReadAsOfAsync(
            new DateTimeOffset(2026, 8, 4, 9, 0, 0, TimeSpan.Zero), AnnouncementPolicy.Strict);

        Assert.Equal(1, series.AdjustmentsApplied);
    }

    [Theory]
    [InlineData(AnnouncementPolicy.Strict, 0, 1)]
    [InlineData(AnnouncementPolicy.Permissive, 1, 0)]
    public async Task An_unknown_announcement_date_is_decided_by_the_policy(
        AnnouncementPolicy policy,
        int expectedApplied,
        int expectedWithheld)
    {
        // Most Vietnamese action records carry an ex-date and nothing else, so
        // this is the common case rather than the corner one. The two readings
        // are opposites, and the answer says which it used.
        var harness = new Harness();
        harness.StoreWeek();
        harness.ObserveWeek();
        harness.Record(CorporateActionType.StockSplit, Wednesday, ratio: 2m);
        await harness.RecomputeAsync();

        var series = await harness.ReadAsOfAsync(Monday, policy);

        Assert.Equal(policy, series.AnnouncementPolicy);
        Assert.Equal(expectedApplied, series.AdjustmentsApplied);
        Assert.Equal(expectedWithheld, series.AdjustmentsWithheld);
    }

    [Fact]
    public async Task A_current_read_applies_everything_recorded_whatever_the_policy()
    {
        // "As of now" means everything this system knows. A strict current read
        // that withheld actions for want of an announcement date would report a
        // series no instant justifies.
        var harness = new Harness();
        harness.StoreWeek();
        harness.Record(CorporateActionType.StockSplit, Wednesday, ratio: 2m);
        await harness.RecomputeAsync();

        var series = await harness.ReadAsync(adjusted: true);

        Assert.Null(series.KnownAsOfUtc);
        Assert.Equal(1, series.AdjustmentsApplied);
        Assert.Equal(0, series.AdjustmentsWithheld);
        Assert.Equal(50m, series.Bars[0].Close);
    }

    [Fact]
    public async Task A_later_announcement_date_makes_the_stored_factor_stale()
    {
        // Schedule does not bump the action's version, so without the
        // announcement date in the staleness check a factor computed before the
        // date arrived would keep the old null and go on being excluded from
        // every strict as-of read.
        var harness = new Harness();
        harness.StoreWeek();

        var action = harness.Record(CorporateActionType.StockSplit, Wednesday, ratio: 2m);
        await harness.RecomputeAsync();

        Assert.Null(Assert.Single(harness.Actions.Adjustments).AnnouncedOn);

        // Act - the source supplies what it did not have the first time.
        action.Schedule(null, null, new DateOnly(2026, 8, 4), Now);
        var run = await harness.RecomputeAsync();

        // Assert
        Assert.Equal(1, run.Computed);
        Assert.Equal(0, run.Unchanged);
        Assert.Equal(
            new DateOnly(2026, 8, 4),
            Assert.Single(harness.Actions.Adjustments).AnnouncedOn);
    }

    [Fact]
    public async Task An_action_no_discontinuity_supports_raises_a_finding()
    {
        // The gap Phase 4 recorded against itself. These prices are flat at 100
        // all week, so a two-for-one split going ex on the Wednesday claims a
        // halving that never happened - a ratio, an amount or an ex-date
        // transcribed wrongly, and one that rescales everything before it.
        var harness = new Harness(dailyPriceLimitPercent: 7m);
        harness.StoreWeek();
        harness.Record(CorporateActionType.StockSplit, Wednesday, ratio: 2m);

        // Act
        var run = await harness.RecomputeAsync();

        // Assert
        Assert.Equal(1, run.IssuesRaised);

        var issue = Assert.Single(harness.Issues.All);
        Assert.Equal(DataQualityIssueKind.ActionWithoutDiscontinuity, issue.Kind);
        Assert.Equal(Wednesday, DateOnly.FromDateTime(issue.SessionAtUtc.UtcDateTime));

        // The factor is stored regardless. Refusing it would lose the record of
        // what the source said.
        Assert.Single(harness.Actions.Adjustments);
    }

    [Fact]
    public async Task An_action_the_prices_do_support_raises_nothing()
    {
        var harness = new Harness(dailyPriceLimitPercent: 7m);
        harness.StoreWeek();
        harness.HalveFrom(Wednesday);
        harness.Record(CorporateActionType.StockSplit, Wednesday, ratio: 2m);

        var run = await harness.RecomputeAsync();

        Assert.Equal(0, run.IssuesRaised);
        Assert.Empty(harness.Issues.All);
    }

    [Fact]
    public async Task Actions_sharing_an_ex_date_are_judged_by_their_combined_factor()
    {
        // FPT, 27 May 2016: a 1,000 dong cash dividend and a 20:3 stock
        // dividend went ex on the same session. Neither explains the gap on its
        // own - the dividend alone implies a close 13% above what printed, and
        // a check that reads one action at a time reports a correctly
        // transcribed entitlement as a transcription error. The prices support
        // the pair, and the pair is what the series is read through.
        var harness = new Harness(dailyPriceLimitPercent: 7m);
        harness.StoreWeek();

        // ((100 - 2) / 100) x (1 / 1.15) = 0.852174, against a close of 85.
        harness.CloseFrom(Wednesday, 85m);
        harness.Record(CorporateActionType.CashDividend, Wednesday, cashAmount: 2m);
        harness.Record(CorporateActionType.StockDividend, Wednesday, ratio: 0.15m);

        // Act
        var run = await harness.RecomputeAsync();

        // Assert
        Assert.Equal(2, run.Computed);
        Assert.Equal(0, run.IssuesRaised);
        Assert.Empty(harness.Issues.All);
    }

    [Fact]
    public async Task A_combination_no_discontinuity_supports_still_raises_one_finding()
    {
        // The other half of the same rule. Reading the actions together must
        // not become a way for a wrong one to hide behind a right one: these
        // prices are flat, so the pair claims a 15% drop that never happened.
        // One finding, because the session is what is in doubt rather than
        // either row individually - which of the two is wrong is exactly what
        // the finding asks somebody to look at.
        var harness = new Harness(dailyPriceLimitPercent: 7m);
        harness.StoreWeek();
        harness.Record(CorporateActionType.CashDividend, Wednesday, cashAmount: 2m);
        harness.Record(CorporateActionType.StockDividend, Wednesday, ratio: 0.15m);

        // Act
        var run = await harness.RecomputeAsync();

        // Assert
        Assert.Equal(1, run.IssuesRaised);

        var issue = Assert.Single(harness.Issues.All);
        Assert.Equal(DataQualityIssueKind.ActionWithoutDiscontinuity, issue.Kind);
        Assert.Equal(Wednesday, DateOnly.FromDateTime(issue.SessionAtUtc.UtcDateTime));
    }

    [Fact]
    public async Task A_venue_with_no_band_cannot_contradict_an_action()
    {
        // Nothing to measure the discrepancy against. Skipped rather than
        // guessed at a threshold, which is what the whole check rests on.
        var harness = new Harness();
        harness.StoreWeek();
        harness.Record(CorporateActionType.StockSplit, Wednesday, ratio: 2m);

        var run = await harness.RecomputeAsync();

        Assert.Equal(0, run.IssuesRaised);
    }

    /// <summary>Wires the real engine and read path over in-memory ports.</summary>
    [Fact]
    public async Task A_series_its_source_already_adjusted_is_not_adjusted_again()
    {
        // The failure this prevents is silent. A source-adjusted series
        // rescaled by PQT's own factors stays plausible and smooth, and is
        // wrong by the product of every factor since — no quality rule can see
        // it, and no chart looks odd.
        var harness = new Harness(sourceAdjusts: true);
        harness.StoreWeek();
        harness.Record(CorporateActionType.StockSplit, Wednesday, ratio: 2m);
        await harness.RecomputeAsync();

        // Act
        var series = await harness.ReadAsync(adjusted: true);

        // Assert
        Assert.True(series.Adjusted);
        Assert.True(series.AdjustedAtSource);
        Assert.All(series.Bars, bar => Assert.Equal(100m, bar.Close));
        Assert.All(series.Bars, bar => Assert.False(bar.IsAdjusted));
    }

    [Fact]
    public async Task A_raw_read_of_a_source_adjusted_series_is_still_raw()
    {
        // Asking for what printed still returns what was stored. This system
        // did not adjust it, and cannot un-adjust what the source did — the
        // answer says which it is rather than pretending.
        var harness = new Harness(sourceAdjusts: true);
        harness.StoreWeek();

        var series = await harness.ReadAsync(adjusted: false);

        Assert.False(series.Adjusted);
        Assert.False(series.AdjustedAtSource);
    }

    private sealed class Harness
    {
        private readonly PriceAdjustmentService _service;
        private readonly MarketDataQueryService _query;

        public Harness(bool sourceAdjusts = false, decimal? dailyPriceLimitPercent = null)
        {
            InstrumentId = InstrumentId.New();
            Bars = new FakeBarRepository();
            Actions = new FakeCorporateActionRepository();
            Issues = new FakeQualityRepository();

            // No band by default, which switches the discontinuity check off:
            // most of these tests are about the arithmetic and the staleness
            // rules, and a venue would make every one of them assert a second
            // thing it was not written to test.
            var exchanges = new InMemoryExchanges();
            var master = new InMemoryInstrumentMaster();
            var venue = exchanges.Add("ADJ", Now, dailyPriceLimitPercent);
            var instrument = Instrument.Register(
                venue, Ticker.Create("ADJ"), "Adjusted Company", AssetType.Equity, CurrencyCode.Vnd, Now);

            instrument.List(Now);
            master.Seed(WithIdentity(instrument, InstrumentId));

            _service = new PriceAdjustmentService(
                Actions,
                Bars,
                Issues,
                master,
                exchanges,
                new FakeUnitOfWork(),
                new FakeClock(Now),
                NullLogger<PriceAdjustmentService>.Instance);

            // With nothing registered no source declares that it adjusts at
            // source, and the read rescales exactly as it always has.
            var registry = sourceAdjusts
                ? new MarketDataProviderRegistry(
                    [
                        new ScriptedProvider(Source, _ => throw new NotSupportedException())
                        {
                            Capability = TestCapability.For(Source, adjustsPricesAtSource: true),
                        },
                    ])
                : new MarketDataProviderRegistry([]);

            _query = new MarketDataQueryService(
                Bars,
                new FakeIngestionJournal(),
                Actions,
                registry);
        }

        public InstrumentId InstrumentId { get; }

        public FakeBarRepository Bars { get; }

        public FakeCorporateActionRepository Actions { get; }

        public FakeQualityRepository Issues { get; }

        public void StoreWeek(decimal? turnover = null) =>
            Bars.AddRange(
                [.. Enumerable.Range(0, 5).Select(offset => OhlcvBar.Record(
                    InstrumentId,
                    BarInterval.OneDay,
                    Monday.AddDays(offset),
                    Price.Create(100m),
                    Price.Create(100m),
                    Price.Create(100m),
                    Price.Create(100m),
                    1_000,
                    turnover,
                    Source,
                    Now))]);

        public CorporateAction Record(
            CorporateActionType type,
            DateOnly exDate,
            decimal? ratio = null,
            decimal? cashAmount = null)
        {
            var action = CorporateAction.Record(
                InstrumentId, type, exDate, ratio, cashAmount, Source, Now);

            Actions.Add(action);
            return action;
        }

        /// <summary>
        /// Forces the seeded instrument to carry the identifier the fake bar
        /// and action repositories are keyed by.
        /// </summary>
        private static Instrument WithIdentity(Instrument instrument, InstrumentId id)
        {
            typeof(Instrument)
                .GetProperty(nameof(Instrument.Id))!
                .SetValue(instrument, id);

            return instrument;
        }

        /// <summary>
        /// Mirrors every stored bar into the observation history, observed
        /// from the Monday.
        /// </summary>
        /// <remarks>
        /// Every revision is known at any as-of these tests use, so what an
        /// as-of read returns is decided entirely by the announcement filter.
        /// A test that had to reason about revision windows as well would stop
        /// being a test of the announcement policy.
        /// </remarks>
        public void ObserveWeek() =>
            Bars.AddRevisions([.. Bars.All.Select(bar => BarRevision.Snapshot(bar, Monday))]);

        /// <summary>Reads the series as of an instant, under a policy.</summary>
        public Task<BarSeries> ReadAsOfAsync(
            DateTimeOffset knownAsOf,
            AnnouncementPolicy policy)
        {
            Assert.True(
                BarQuery.TryCreate(
                    InstrumentId,
                    BarInterval.OneDay,
                    null,
                    null,
                    null,
                    out var query,
                    out var problem,
                    adjusted: true,
                    knownAsOf,
                    policy),
                problem);

            return _query.GetSeriesAsync(query, TestContext.Current.CancellationToken);
        }

        /// <summary>
        /// Halves every close from a date onward, so the prices show the
        /// discontinuity a split on that date implies.
        /// </summary>
        public void HalveFrom(DateOnly exDate)
        {
            var ex = new DateTimeOffset(exDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

            foreach (var bar in Bars.All.Where(bar => bar.OpenedAtUtc >= ex).ToList())
            {
                bar.Revise(
                    Price.Create(50m),
                    Price.Create(50m),
                    Price.Create(50m),
                    Price.Create(50m),
                    bar.Volume,
                    bar.Turnover,
                    Source,
                    Now);
            }
        }

        /// <summary>
        /// Sets every close from a date onward, so the prices show the exact
        /// discontinuity a combination of actions implies.
        /// </summary>
        public void CloseFrom(DateOnly exDate, decimal close)
        {
            var ex = new DateTimeOffset(exDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

            foreach (var bar in Bars.All.Where(bar => bar.OpenedAtUtc >= ex).ToList())
            {
                bar.Revise(
                    Price.Create(close),
                    Price.Create(close),
                    Price.Create(close),
                    Price.Create(close),
                    bar.Volume,
                    bar.Turnover,
                    Source,
                    Now);
            }
        }

        public void RaiseBreach(DateOnly session) =>
            Issues.Seed(DataQualityIssue.Raise(
                InstrumentId,
                BarInterval.OneDay,
                new DateTimeOffset(session.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                DataQualityIssueKind.PriceLimitBreach,
                "The close moved beyond the band.",
                DataRules.ValidationVersion,
                Now));

        public Task<AdjustmentRun> RecomputeAsync() =>
            _service.RecomputeAsync(InstrumentId, TestContext.Current.CancellationToken);

        public Task<BarSeries> ReadAsync(bool adjusted)
        {
            Assert.True(
                BarQuery.TryCreate(
                    InstrumentId,
                    BarInterval.OneDay,
                    null,
                    null,
                    null,
                    out var query,
                    out var problem,
                    adjusted),
                problem);

            return _query.GetSeriesAsync(query, TestContext.Current.CancellationToken);
        }
    }
}
