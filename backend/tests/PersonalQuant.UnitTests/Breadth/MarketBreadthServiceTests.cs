using Microsoft.Extensions.Logging.Abstractions;
using PersonalQuant.Application.Breadth;
using PersonalQuant.Application.Exchanges;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Application.Universes;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.Universes;
using PersonalQuant.UnitTests.Instruments.Fakes;
using PersonalQuant.UnitTests.MarketData.Fakes;
using static PersonalQuant.UnitTests.Breadth.BreadthValues;

namespace PersonalQuant.UnitTests.Breadth;

/// <summary>
/// Verifies the one definition of breadth ADR-026 records.
/// </summary>
/// <remarks>
/// Every refusal here is a case where answering would be quietly wrong: a
/// dividend counted as a decline, a suspended name counted as unchanged, a
/// holiday answered with yesterday's closes, a year nobody sourced answered as
/// an empty index.
/// </remarks>
public sealed class MarketBreadthServiceTests
{
    private static readonly DateOnly Monday = new(2026, 9, 28);
    private static readonly DateOnly Tuesday = new(2026, 9, 29);
    private static readonly DateOnly Wednesday = new(2026, 9, 30);

    [Fact]
    public async Task Counts_each_member_by_its_close_against_the_previous_session()
    {
        var harness = new Harness();
        var up = harness.Member("AAA");
        var down = harness.Member("BBB");
        var flat = harness.Member("CCC");

        harness.Closes(up, (Tuesday, 100m, 10), (Wednesday, 103m, 500));
        harness.Closes(down, (Tuesday, 50m, 10), (Wednesday, 49m, 300));
        harness.Closes(flat, (Tuesday, 20m, 10), (Wednesday, 20m, 70));

        var breadth = await harness.ComputedAsync(Wednesday);

        Assert.Equal((1, 1, 1, 0), (breadth.Advancing, breadth.Declining, breadth.Unchanged, breadth.NoData));
        Assert.Equal(500, breadth.AdvancingVolume);
        Assert.Equal(300, breadth.DecliningVolume);
        Assert.Equal(70, breadth.UnchangedVolume);
        Assert.False(breadth.SessionWasDefaulted);

        var aaa = Assert.Single(breadth.Members, member => member.Ticker.Value == "AAA");
        Assert.Equal(Tuesday, aaa.PreviousSession);
        Assert.Equal(0.03m, aaa.Change);
        Assert.False(aaa.AdjustedForAction);

        var venue = Assert.Single(breadth.Venues);
        Assert.Equal(("HOSE", Tuesday), (venue.Code.Value, venue.PreviousSession));
    }

    [Fact]
    public async Task An_ex_dividend_gap_is_not_a_decline()
    {
        // 25,000 cum, a 1,000 dividend, 24,000 ex: the board shows red, and the
        // red is the dividend. Restated onto the session's basis the previous
        // close is 24,000, and the name did not move.
        var harness = new Harness();
        var payer = harness.Member("DIV");
        harness.Closes(payer, (Tuesday, 25_000m, 10), (Wednesday, 24_000m, 10));
        harness.Reader.Add(Factor(payer, Wednesday, 0.96m));

        var breadth = await harness.ComputedAsync(Wednesday);

        var member = Assert.Single(breadth.Members);
        Assert.Equal(BreadthCategory.Unchanged, member.Category);
        Assert.Equal(25_000m, member.RawPreviousClose);
        Assert.Equal(24_000m, member.PreviousClose);
        Assert.Equal(0m, member.Change);
        Assert.True(member.AdjustedForAction);
        Assert.Equal(0, breadth.Declining);
    }

    [Fact]
    public async Task A_factor_going_ex_on_another_day_does_not_touch_the_session()
    {
        // The day before and the day after. Neither sits between the two
        // closes, so neither may restate the previous one.
        var harness = new Harness();
        var payer = harness.Member("DIV");
        harness.Closes(payer, (Tuesday, 100m, 10), (Wednesday, 99m, 10));
        harness.Reader.Add(Factor(payer, Tuesday, 0.5m));
        harness.Reader.Add(Factor(payer, new DateOnly(2026, 10, 1), 0.5m));

        var member = Assert.Single((await harness.ComputedAsync(Wednesday)).Members);

        Assert.Equal(BreadthCategory.Declining, member.Category);
        Assert.Equal(100m, member.PreviousClose);
    }

    [Fact]
    public async Task A_source_that_adjusts_at_source_is_not_adjusted_again()
    {
        var harness = new Harness(sourceAdjustsAtSource: true);
        var payer = harness.Member("DIV");
        harness.Closes(payer, (Tuesday, 24_000m, 10), (Wednesday, 24_000m, 10));
        harness.Reader.Add(Factor(payer, Wednesday, 0.96m));

        var member = Assert.Single((await harness.ComputedAsync(Wednesday)).Members);

        Assert.Equal(BreadthCategory.Unchanged, member.Category);
        Assert.Equal(24_000m, member.PreviousClose);
    }

    [Fact]
    public async Task A_member_missing_either_bar_is_no_data_and_never_unchanged()
    {
        var harness = new Harness();
        var suspended = harness.Member("SUS");
        var fresh = harness.Member("NEW");
        var never = harness.Member("NIL");
        harness.Closes(suspended, (Tuesday, 10m, 10));
        harness.Closes(fresh, (Wednesday, 10m, 40));

        var breadth = await harness.ComputedAsync(Wednesday);

        Assert.Equal(3, breadth.NoData);
        Assert.Equal(0, breadth.Unchanged);
        Assert.All(breadth.Members, member => Assert.Null(member.Change));

        var missingToday = Assert.Single(breadth.Members, member => member.InstrumentId == suspended);
        Assert.Equal(10m, missingToday.RawPreviousClose);
        Assert.Null(missingToday.Close);

        var missingBefore = Assert.Single(breadth.Members, member => member.InstrumentId == fresh);
        Assert.Equal(10m, missingBefore.Close);
        Assert.Equal(40, missingBefore.Volume);
        Assert.Contains(breadth.Members, member => member.InstrumentId == never);
    }

    [Fact]
    public async Task A_member_admitted_on_the_session_counts_and_one_removed_on_it_does_not()
    {
        // Half-open spells: a review that swaps one name for another on the
        // Wednesday puts each on exactly one side of it.
        var harness = new Harness();
        var joiner = harness.Member("JOIN", from: Wednesday);
        var leaver = harness.Member("LEFT", until: Wednesday);
        var earlier = harness.Member("GONE", until: Monday);
        harness.Closes(joiner, (Tuesday, 10m, 10), (Wednesday, 11m, 10));
        harness.Closes(leaver, (Tuesday, 10m, 10), (Wednesday, 9m, 10));
        harness.Closes(earlier, (Tuesday, 10m, 10), (Wednesday, 9m, 10));

        var breadth = await harness.ComputedAsync(Wednesday);

        var member = Assert.Single(breadth.Members);
        Assert.Equal(joiner, member.InstrumentId);
        Assert.Equal(BreadthCategory.Advancing, member.Category);
    }

    [Fact]
    public async Task The_previous_session_skips_a_recorded_closure()
    {
        var harness = new Harness();
        var holiday = new DateOnly(2026, 9, 2);
        harness.Closure(harness.Hose, holiday);
        var member = harness.Member("AAA");
        harness.Closes(member, (new DateOnly(2026, 9, 1), 10m, 10), (new DateOnly(2026, 9, 3), 12m, 10));

        var breadth = await harness.ComputedAsync(new DateOnly(2026, 9, 3));

        Assert.Equal(new DateOnly(2026, 9, 1), Assert.Single(breadth.Members).PreviousSession);
        Assert.Equal(1, breadth.Advancing);
    }

    [Fact]
    public async Task Each_member_is_measured_against_its_own_venue_s_previous_session()
    {
        var harness = new Harness();
        var hnx = harness.Venue("HNX");
        harness.Closure(hnx, Tuesday);
        var onHose = harness.Member("AAA");
        var onHnx = harness.Member("BBB", venue: hnx);
        harness.Closes(onHose, (Tuesday, 10m, 10), (Wednesday, 11m, 10));
        harness.Closes(onHnx, (Monday, 10m, 10), (Wednesday, 9m, 10));

        var breadth = await harness.ComputedAsync(Wednesday);

        Assert.Equal((1, 1), (breadth.Advancing, breadth.Declining));
        Assert.Equal(
            [("HNX", Monday), ("HOSE", Tuesday)],
            breadth.Venues.Select(venue => (venue.Code.Value, venue.PreviousSession)));
    }

    [Fact]
    public async Task Reads_do_not_grow_with_the_number_of_members()
    {
        var harness = new Harness();

        for (var index = 0; index < 30; index++)
        {
            var member = harness.Member($"M{index:00}");
            harness.Closes(member, (Tuesday, 10m, 10), (Wednesday, 10m + index, 10));
        }

        var breadth = await harness.ComputedAsync(Wednesday);

        Assert.Equal(30, breadth.Members.Count);
        Assert.Equal(3, harness.Reader.Reads);
    }

    [Fact]
    public async Task A_covered_day_on_which_the_universe_held_nothing_is_an_empty_count()
    {
        var harness = new Harness();

        var breadth = await harness.ComputedAsync(Wednesday);

        Assert.Empty(breadth.Members);
        Assert.Empty(breadth.Venues);
    }

    [Fact]
    public async Task An_unknown_universe_is_refused()
    {
        var harness = new Harness();

        var result = await harness.Service.ComputeAsync(
            UniverseCode.Create("NOPE"), Wednesday, TestContext.Current.CancellationToken);

        Assert.Equal(BreadthOutcome.UnknownUniverse, result.Outcome);
        Assert.Null(result.Breadth);
    }

    [Fact]
    public async Task A_session_outside_the_membership_claim_is_refused_rather_than_counted_empty()
    {
        var harness = new Harness();
        harness.Member("AAA");

        var result = await harness.ComputeAsync(new DateOnly(2025, 12, 30));

        Assert.Equal(BreadthOutcome.MembershipUnknown, result.Outcome);
        Assert.Contains("not an empty one", result.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(2026, 10, 3)]
    [InlineData(2026, 9, 2)]
    public async Task A_day_the_venue_did_not_trade_is_refused(int year, int month, int day)
    {
        var harness = new Harness();
        harness.Closure(harness.Hose, new DateOnly(2026, 9, 2));
        harness.Member("AAA");

        var result = await harness.ComputeAsync(new DateOnly(year, month, day));

        Assert.Equal(BreadthOutcome.NotATradingDay, result.Outcome);
        Assert.Contains("HOSE", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_session_the_calendar_was_not_transcribed_for_is_refused()
    {
        var harness = new Harness();
        harness.Member("AAA");

        var result = await harness.ComputeAsync(new DateOnly(2027, 1, 4));

        Assert.Equal(BreadthOutcome.CalendarNotCovered, result.Outcome);
    }

    [Fact]
    public async Task A_session_whose_previous_session_the_calendar_does_not_reach_is_refused()
    {
        // The calendar starts on Monday 5 January, so the Friday before is not
        // transcribed: it may have been a closure, and nothing says.
        var harness = new Harness();
        harness.Member("AAA");

        var result = await harness.ComputeAsync(Harness.CoverageStart);

        Assert.Equal(BreadthOutcome.CalendarNotCovered, result.Outcome);
        Assert.Contains("session before", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_session_the_latest_one_a_member_has_a_bar_for_is_counted()
    {
        // Today is Saturday 3 October; the newest member bar is Friday's.
        var harness = new Harness();
        var member = harness.Member("AAA");
        var friday = new DateOnly(2026, 10, 2);
        harness.Closes(member, (new DateOnly(2026, 10, 1), 10m, 10), (friday, 11m, 10));

        var result = await harness.ComputeAsync(null);

        Assert.Equal(BreadthOutcome.Computed, result.Outcome);
        Assert.Equal(friday, result.Breadth!.Session);
        Assert.True(result.Breadth.SessionWasDefaulted);
    }

    [Fact]
    public async Task The_default_session_ignores_bars_from_after_a_name_left()
    {
        // The leaver kept trading after its removal; those bars are not a
        // session of this universe's.
        var harness = new Harness();
        var stays = harness.Member("STAY");
        var leaver = harness.Member("LEFT", until: new DateOnly(2026, 9, 25));
        harness.Closes(stays, (Tuesday, 10m, 10), (Wednesday, 10m, 10));
        harness.Closes(leaver, (new DateOnly(2026, 10, 2), 10m, 10));

        var result = await harness.ComputeAsync(null);

        Assert.Equal(Wednesday, result.Breadth!.Session);
    }

    [Fact]
    public async Task Without_a_session_and_without_recent_bars_the_answer_is_no_data()
    {
        var harness = new Harness();
        harness.Member("AAA");

        var result = await harness.ComputeAsync(null);

        Assert.Equal(BreadthOutcome.NoData, result.Outcome);
        Assert.Null(result.Session);
    }

    [Fact]
    public async Task Without_a_session_a_universe_that_claims_nothing_is_refused()
    {
        var harness = new Harness(declareCoverage: false);

        var result = await harness.ComputeAsync(null);

        Assert.Equal(BreadthOutcome.MembershipUnknown, result.Outcome);
    }

    [Fact]
    public async Task The_directory_counts_today_s_members_only_where_the_claim_reaches()
    {
        var harness = new Harness();
        harness.Member("AAA");
        harness.Member("BBB", until: Monday);
        var lapsed = Universe.Define(
            UniverseId.New(), UniverseCode.Create("OLD"), "Lapsed", UniverseKind.Custom, Source, Harness.Recorded);
        lapsed.DeclareCoverage(
            MembershipCoverage.Create(Harness.CoverageStart, Monday), Source, Harness.Recorded);
        harness.Universes.Add(lapsed);

        var directory = new UniverseDirectory(harness.Universes, new FakeClock(Harness.Now));
        var listed = await directory.ListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["OLD", "VN30T"], listed.Select(summary => summary.Code.Value));
        Assert.Null(listed[0].MemberCount);
        Assert.Equal(1, listed[1].MemberCount);
        Assert.Equal(new DateOnly(2026, 10, 3), listed[1].AsOf);
    }

    /// <summary>One venue, one index, and the service over real calendar logic.</summary>
    private sealed class Harness
    {
        public static readonly DateTimeOffset Recorded = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public static readonly DateTimeOffset Now = new(2026, 10, 3, 2, 0, 0, TimeSpan.Zero);
        public static readonly DateOnly CoverageStart = new(2026, 1, 5);

        private readonly InMemoryExchanges _exchanges = new();
        private readonly Universe _universe;

        public Harness(bool sourceAdjustsAtSource = false, bool declareCoverage = true)
        {
            Hose = Venue("HOSE");

            _universe = Universe.Define(
                UniverseId.New(), UniverseCode.Create("VN30T"), "Test index", UniverseKind.Index, Source, Recorded);

            if (declareCoverage)
            {
                _universe.DeclareCoverage(MembershipCoverage.Create(CoverageStart, null), Source, Recorded);
            }

            Universes.Add(_universe);

            var registry = sourceAdjustsAtSource
                ? new MarketDataProviderRegistry(
                    [
                        new ScriptedProvider(Source, _ => throw new NotSupportedException())
                        {
                            Capability = TestCapability.For(Source, adjustsPricesAtSource: true),
                        },
                    ])
                : new MarketDataProviderRegistry([]);

            Service = new MarketBreadthService(
                Universes,
                new UniverseCatalog(Universes),
                new TradingCalendar(_exchanges),
                Reader,
                registry,
                new FakeClock(Now),
                NullLogger<MarketBreadthService>.Instance);
        }

        public ExchangeId Hose { get; }

        public InMemoryUniverses Universes { get; } = new();

        public FakeBreadthReader Reader { get; } = new();

        public MarketBreadthService Service { get; }

        public ExchangeId Venue(string code)
        {
            var id = _exchanges.Add(code, Recorded);
            _exchanges.DeclareCoverage(id, CoverageStart, new DateOnly(2027, 1, 1));
            return id;
        }

        public void Closure(ExchangeId venue, DateOnly date) =>
            _exchanges.AddHoliday(TradingHoliday.Record(venue, date, "Closure", Recorded));

        public InstrumentId Member(
            string ticker,
            DateOnly? from = null,
            DateOnly? until = null,
            ExchangeId? venue = null)
        {
            var instrument = Instrument(ticker, venue ?? Hose);
            Reader.Add(instrument);

            var spell = UniverseMembership.Admit(
                _universe.Id, instrument.InstrumentId, from ?? CoverageStart, null, Source, Recorded);

            if (until is { } end)
            {
                spell.Remove(end);
            }

            Universes.Add(spell);
            return instrument.InstrumentId;
        }

        public void Closes(InstrumentId id, params (DateOnly Session, decimal Close, long Volume)[] closes)
        {
            foreach (var (session, close, volume) in closes)
            {
                Reader.Add(Close(id, session, close, volume));
            }
        }

        public Task<BreadthResult> ComputeAsync(DateOnly? session) =>
            Service.ComputeAsync(_universe.Code, session, TestContext.Current.CancellationToken);

        public async Task<MarketBreadth> ComputedAsync(DateOnly session)
        {
            var result = await ComputeAsync(session);

            Assert.True(result.Outcome == BreadthOutcome.Computed, result.Detail);
            return result.Breadth!;
        }
    }
}
