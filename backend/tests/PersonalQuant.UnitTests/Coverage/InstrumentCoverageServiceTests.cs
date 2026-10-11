using PersonalQuant.Application.Coverage;
using PersonalQuant.Application.Exchanges;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Application.Universes;
using PersonalQuant.Domain.Currencies;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.UnitTests.Coverage.Fakes;
using PersonalQuant.UnitTests.Instruments.Fakes;
using PersonalQuant.UnitTests.MarketData.Fakes;

namespace PersonalQuant.UnitTests.Coverage;

/// <summary>
/// Verifies the coverage registry's state decisions.
/// </summary>
/// <remarks>
/// The rules under test are the honesty rule of ADR-026: an absent dataset and
/// an unmeasured one never come back the same. Each test pins one place where
/// the two could be confused — a calendar that does not reach, a universe that
/// declared nothing, a resolution no source serves.
/// </remarks>
public sealed class InstrumentCoverageServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 1, 0, 0, TimeSpan.Zero);

    // The week of 3 August 2026, Monday to Friday.
    private static readonly DateOnly Monday = new(2026, 8, 3);
    private static readonly DateOnly Tuesday = new(2026, 8, 4);
    private static readonly DateOnly Wednesday = new(2026, 8, 5);
    private static readonly DateOnly Thursday = new(2026, 8, 6);
    private static readonly DateOnly Friday = new(2026, 8, 7);

    [Fact]
    public async Task An_unknown_instrument_has_no_coverage()
    {
        var harness = new Harness();

        var coverage = await harness.DescribeAsync(InstrumentId.New());

        Assert.Null(coverage);
    }

    [Fact]
    public async Task Every_resolution_is_reported_daily_first_whether_held_or_not()
    {
        var harness = new Harness();

        var coverage = await harness.DescribeAsync(harness.Instrument);

        Assert.NotNull(coverage);
        Assert.Equal(
            [
                BarInterval.OneDay,
                BarInterval.OneHour,
                BarInterval.ThirtyMinutes,
                BarInterval.FifteenMinutes,
                BarInterval.FiveMinutes,
                BarInterval.OneMinute,
            ],
            coverage.Prices.Select(price => price.Interval));
    }

    [Fact]
    public async Task A_held_daily_series_states_its_extent_and_sources()
    {
        var harness = new Harness();
        harness.Venues.DeclareCoverage(harness.Venue, new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1));
        harness.Store.HoldDaily(harness.Instrument, "VNX", Monday, Tuesday);
        harness.Store.HoldDaily(harness.Instrument, "CAFEF", Thursday, Friday);
        harness.Store.Find(harness.Instrument, BarInterval.OneDay);

        var daily = Daily(await harness.DescribeAsync(harness.Instrument));

        Assert.Equal(CoverageState.Held, daily.State);
        Assert.Equal(Monday, daily.FirstSession);
        Assert.Equal(Friday, daily.LastSession);
        Assert.Equal(4, daily.Bars);
        Assert.Equal(["CAFEF", "VNX"], daily.Sources.Select(source => source.Value));
        Assert.Equal(1, daily.OpenFindings);
    }

    [Fact]
    public async Task Missing_sessions_are_counted_against_the_calendar_and_a_bar_on_a_holiday_hides_none()
    {
        // Wednesday is a closure and holds a bar anyway; Thursday traded and
        // holds nothing. Counting bars against sessions would call the week
        // complete — four bars, four sessions — and miss Thursday entirely.
        var harness = new Harness();
        harness.Venues.DeclareCoverage(harness.Venue, new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1));
        harness.Venues.AddHoliday(TradingHoliday.Record(harness.Venue, Wednesday, "A closure", Now));
        harness.Store.HoldDaily(harness.Instrument, "VNX", Monday, Tuesday, Wednesday, Friday);

        var daily = Daily(await harness.DescribeAsync(harness.Instrument));

        Assert.Equal(4, daily.SessionsExpected);
        Assert.Equal(1, daily.MissingSessions);
        Assert.Null(daily.Note);
    }

    [Fact]
    public async Task Missing_sessions_are_absent_not_zero_when_the_venue_declares_no_calendar()
    {
        var harness = new Harness();
        harness.Store.HoldDaily(harness.Instrument, "VNX", Monday, Tuesday, Wednesday, Thursday, Friday);

        var daily = Daily(await harness.DescribeAsync(harness.Instrument));

        Assert.Equal(CoverageState.Held, daily.State);
        Assert.Null(daily.SessionsExpected);
        Assert.Null(daily.MissingSessions);
        Assert.Contains("calendar is not declared", daily.Note, StringComparison.Ordinal);
        Assert.Equal(0, harness.Store.SessionCounts);
    }

    [Fact]
    public async Task Missing_sessions_are_absent_when_the_calendar_covers_only_part_of_the_span()
    {
        // Transcribed from Wednesday: Monday and Tuesday cannot be checked, and
        // a figure for the rest would be a figure for a span nobody asked about.
        var harness = new Harness();
        harness.Venues.DeclareCoverage(harness.Venue, Wednesday, new DateOnly(2027, 1, 1));
        harness.Store.HoldDaily(harness.Instrument, "VNX", Monday, Friday);

        var daily = Daily(await harness.DescribeAsync(harness.Instrument));

        Assert.Null(daily.MissingSessions);
        Assert.Null(daily.SessionsExpected);
    }

    [Fact]
    public async Task An_intraday_series_is_held_without_a_session_count()
    {
        var harness = new Harness();
        harness.Venues.DeclareCoverage(harness.Venue, new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1));
        harness.Store.Hold(
            harness.Instrument, BarInterval.OneHour, "VNX", new DateTimeOffset(2026, 8, 3, 2, 0, 0, TimeSpan.Zero));

        var coverage = await harness.DescribeAsync(harness.Instrument);
        var hourly = Assert.Single(coverage!.Prices, price => price.Interval == BarInterval.OneHour);

        Assert.Equal(CoverageState.Held, hourly.State);
        Assert.Equal(Monday, hourly.FirstSession);
        Assert.Null(hourly.MissingSessions);
        Assert.Contains("daily series only", hourly.Note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_collected_resolution_with_no_bars_is_not_held_and_keeps_its_last_attempt()
    {
        var harness = new Harness();
        var failed = new IngestionAttempt(
            BarInterval.OneDay, SourceCode.Create("VNX"), Now.AddHours(-2), Now.AddHours(-1), IngestionOutcome.Failed);

        harness.Store.Attempt(
            harness.Instrument,
            failed with { StartedAtUtc = Now.AddDays(-3), Outcome = IngestionOutcome.Succeeded });
        harness.Store.Attempt(harness.Instrument, failed);

        var daily = Daily(await harness.DescribeAsync(harness.Instrument));

        Assert.Equal(CoverageState.NotHeld, daily.State);
        Assert.Equal(0, daily.Bars);
        Assert.Null(daily.FirstSession);
        Assert.Null(daily.MissingSessions);
        Assert.Equal(failed, daily.LastAttempt);
    }

    [Fact]
    public async Task A_resolution_no_registered_source_serves_is_not_collected()
    {
        var harness = new Harness(intervals: new HashSet<BarInterval> { BarInterval.OneDay });

        var coverage = await harness.DescribeAsync(harness.Instrument);

        Assert.Equal(CoverageState.NotHeld, Daily(coverage).State);
        Assert.All(
            coverage!.Prices.Where(price => price.Interval != BarInterval.OneDay),
            price =>
            {
                Assert.Equal(CoverageState.NotCollected, price.State);
                Assert.Contains("No registered source serves", price.Note, StringComparison.Ordinal);
            });
    }

    [Fact]
    public async Task A_source_for_another_venue_does_not_make_this_one_collected()
    {
        var harness = new Harness(exchanges: new HashSet<ExchangeCode> { ExchangeCode.Create("HNX") });

        var daily = Daily(await harness.DescribeAsync(harness.Instrument));

        Assert.Equal(CoverageState.NotCollected, daily.State);
    }

    [Fact]
    public async Task Bars_already_held_are_held_even_where_no_source_serves_them_now()
    {
        var harness = new Harness(intervals: new HashSet<BarInterval> { BarInterval.OneDay });
        harness.Store.Hold(
            harness.Instrument, BarInterval.FiveMinutes, "OLD", new DateTimeOffset(2026, 8, 3, 2, 5, 0, TimeSpan.Zero));

        var coverage = await harness.DescribeAsync(harness.Instrument);

        Assert.Equal(
            CoverageState.Held,
            Assert.Single(coverage!.Prices, price => price.Interval == BarInterval.FiveMinutes).State);
    }

    [Fact]
    public async Task No_corporate_action_recorded_is_not_held()
    {
        var harness = new Harness();

        var coverage = await harness.DescribeAsync(harness.Instrument);

        Assert.Equal(CoverageState.NotHeld, coverage!.CorporateActions.State);
        Assert.Equal(0, coverage.CorporateActions.Count);
        Assert.Null(coverage.CorporateActions.FirstExDate);
    }

    [Fact]
    public async Task Recorded_corporate_actions_are_held_with_their_extent()
    {
        var harness = new Harness();
        harness.Store.Actions(
            harness.Instrument,
            new CorporateActionExtent(5, 1, 2, new DateOnly(2016, 5, 27), new DateOnly(2026, 6, 1)));

        var actions = (await harness.DescribeAsync(harness.Instrument))!.CorporateActions;

        Assert.Equal(CoverageState.Held, actions.State);
        Assert.Equal(5, actions.Count);
        Assert.Equal(1, actions.Cancelled);
        Assert.Equal(2, actions.WithoutAnnouncementDate);
        Assert.Equal(new DateOnly(2016, 5, 27), actions.FirstExDate);
        Assert.Equal(new DateOnly(2026, 6, 1), actions.LastExDate);
    }

    [Fact]
    public async Task A_spell_makes_membership_held()
    {
        var harness = new Harness();
        var vn30 = harness.Universes.Define("VN30", new DateOnly(2024, 1, 1));
        var spell = new UniverseSpell(harness.Instrument, new DateOnly(2024, 1, 2), null, new DateOnly(2023, 12, 20));
        harness.Store.Spell(harness.Instrument, vn30.Id, spell);

        var membership = Membership(await harness.DescribeAsync(harness.Instrument), "VN30");

        Assert.Equal(CoverageState.Held, membership.State);
        Assert.Equal([spell], membership.Spells);
        Assert.Equal(new DateOnly(2024, 1, 1), membership.DeclaredFrom);
        Assert.Null(membership.DeclaredUntil);
    }

    [Fact]
    public async Task No_spell_inside_a_declared_window_is_not_held_for_that_window_only()
    {
        var harness = new Harness();
        harness.Universes.Define("VN30", new DateOnly(2024, 1, 1), new DateOnly(2026, 1, 1));

        var membership = Membership(await harness.DescribeAsync(harness.Instrument), "VN30");

        Assert.Equal(CoverageState.NotHeld, membership.State);
        Assert.Equal(new DateOnly(2024, 1, 1), membership.DeclaredFrom);
        Assert.Equal(new DateOnly(2026, 1, 1), membership.DeclaredUntil);

        // The claim is half-open; the sentence names the last date it covers.
        Assert.Contains("from 2024-01-01 through 2025-12-31", membership.Note, StringComparison.Ordinal);
        Assert.Contains("outside it, unknown", membership.Note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_spell_in_a_universe_that_declared_nothing_is_not_measured_rather_than_not_held()
    {
        var harness = new Harness();
        harness.Universes.Define("VNMID");

        var membership = Membership(await harness.DescribeAsync(harness.Instrument), "VNMID");

        Assert.Equal(CoverageState.NotMeasured, membership.State);
        Assert.Null(membership.DeclaredFrom);
        Assert.Empty(membership.Spells);
        Assert.Contains("says nothing", membership.Note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_spell_in_a_universe_that_declared_nothing_is_still_held()
    {
        var harness = new Harness();
        var universe = harness.Universes.Define("VNMID");
        harness.Store.Spell(
            harness.Instrument, universe.Id, new UniverseSpell(harness.Instrument, Monday, null, null));

        var membership = Membership(await harness.DescribeAsync(harness.Instrument), "VNMID");

        Assert.Equal(CoverageState.Held, membership.State);
        Assert.Contains("declares no coverage", membership.Note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_defined_universe_is_listed_in_code_order_and_spells_stay_with_their_universe()
    {
        var harness = new Harness();
        var vn30 = harness.Universes.Define("VN30", new DateOnly(2024, 1, 1));
        harness.Universes.Define("HOSEALL", new DateOnly(2024, 1, 1));
        harness.Store.Spell(
            harness.Instrument, vn30.Id, new UniverseSpell(harness.Instrument, Monday, null, null));

        var coverage = await harness.DescribeAsync(harness.Instrument);

        Assert.Equal(["HOSEALL", "VN30"], coverage!.Universes.Select(universe => universe.Code.Value));
        Assert.Equal(CoverageState.NotHeld, coverage.Universes[0].State);
        Assert.Equal(CoverageState.Held, coverage.Universes[1].State);
    }

    [Fact]
    public async Task Fundamentals_are_not_collected_until_phase_6()
    {
        var harness = new Harness();

        var coverage = await harness.DescribeAsync(harness.Instrument);

        Assert.Equal(CoverageState.NotCollected, coverage!.Fundamentals.State);
        Assert.Equal("Phase 6", coverage.Fundamentals.Note);
    }

    [Fact]
    public async Task The_instrument_is_identified_by_ticker_name_and_venue()
    {
        var harness = new Harness();

        var coverage = await harness.DescribeAsync(harness.Instrument);

        Assert.Equal("FPT", coverage!.Ticker.Value);
        Assert.Equal("FPT Corporation", coverage.Name);
        Assert.Equal("HOSE", coverage.Exchange.Value);
    }

    private static PriceCoverage Daily(InstrumentCoverage? coverage)
    {
        Assert.NotNull(coverage);

        return Assert.Single(coverage.Prices, price => price.Interval == BarInterval.OneDay);
    }

    private static UniverseMembershipCoverage Membership(InstrumentCoverage? coverage, string code)
    {
        Assert.NotNull(coverage);

        return Assert.Single(coverage.Universes, universe => universe.Code.Value == code);
    }

    /// <summary>
    /// One listed instrument on one venue, a provider, and empty stores.
    /// </summary>
    /// <remarks>
    /// The trading calendar is the real one over an in-memory venue store, so
    /// the coverage claim and the closures are decided by the code production
    /// runs rather than by a fake that agrees with the test.
    /// </remarks>
    private sealed class Harness
    {
        private readonly InstrumentCoverageService _service;

        public Harness(
            IReadOnlySet<BarInterval>? intervals = null,
            IReadOnlySet<ExchangeCode>? exchanges = null)
        {
            Venue = Venues.Add("HOSE", Now);

            var instrument = Domain.Instruments.Instrument.Register(
                Venue, Ticker.Create("FPT"), "FPT Corporation", AssetType.Equity, CurrencyCode.Vnd, Now);

            instrument.List(Now);
            Master.Seed(instrument);
            Instrument = instrument.Id;

            var source = SourceCode.Create("VNX");
            var provider = new ScriptedProvider(
                source,
                _ => throw new InvalidOperationException("Coverage never fetches."))
            {
                Capability = TestCapability.For(source, intervals: intervals, exchanges: exchanges),
            };

            _service = new InstrumentCoverageService(
                Master,
                Venues,
                new TradingCalendar(Venues),
                Store,
                Universes,
                new MarketDataProviderRegistry([provider]));
        }

        public InMemoryInstrumentMaster Master { get; } = new();

        public InMemoryExchanges Venues { get; } = new();

        public FakeCoverageRepository Store { get; } = new();

        public ListedUniverses Universes { get; } = new();

        public ExchangeId Venue { get; }

        public InstrumentId Instrument { get; }

        public Task<InstrumentCoverage?> DescribeAsync(InstrumentId instrumentId) =>
            _service.DescribeAsync(instrumentId, TestContext.Current.CancellationToken);
    }
}
