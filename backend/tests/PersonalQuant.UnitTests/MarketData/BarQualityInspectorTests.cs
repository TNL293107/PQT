using Microsoft.Extensions.Logging.Abstractions;
using PersonalQuant.Application.Exchanges;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Domain.Currencies;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.UnitTests.Instruments.Fakes;
using PersonalQuant.UnitTests.MarketData.Fakes;

namespace PersonalQuant.UnitTests.MarketData;

/// <summary>
/// Verifies the quality rules that need more than one bar to apply.
/// </summary>
/// <remarks>
/// Monday 2026-08-03 through Friday 2026-08-07 is a full trading week with no
/// holiday, which makes it a clean baseline: any missing session in it is the
/// rule firing rather than the calendar.
/// </remarks>
public sealed class BarQualityInspectorTests
{
    private static readonly DateTimeOffset Monday = new(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 1, 0, 0, TimeSpan.Zero);
    private static readonly SourceCode Source = SourceCode.Create("TEST");

    [Fact]
    public async Task A_clean_week_raises_nothing_and_stamps_the_bars_validated()
    {
        var harness = new Harness();
        harness.WithCalendarThrough(new DateOnly(2026, 12, 31));
        harness.Store(Week(100m, 103m, 105m, 104m, 106m));

        // Act
        var inspection = await harness.InspectAsync();

        // Assert
        Assert.Null(inspection.Skipped);
        Assert.Empty(inspection.Raised);
        Assert.Equal(5, inspection.BarsInspected);
        Assert.Equal(5, inspection.SessionsExpected);
        Assert.All(
            harness.Bars.All,
            bar => Assert.Equal(DataRules.ValidationVersion, bar.ValidationVersion));
    }

    [Fact]
    public async Task A_move_beyond_the_venues_band_is_raised()
    {
        // A 7% venue and a close that halves overnight: a split, a bad print,
        // a halt or a symbol change, and the prices cannot say which.
        var harness = new Harness();
        harness.WithCalendarThrough(new DateOnly(2026, 12, 31));
        harness.Store(Week(100m, 100m, 50m, 50m, 50m));

        // Act
        var inspection = await harness.InspectAsync();

        // Assert
        var issue = Assert.Single(inspection.Raised);
        Assert.Equal(DataQualityIssueKind.PriceLimitBreach, issue.Kind);
        Assert.Equal(Monday.AddDays(2), issue.SessionAtUtc);
        Assert.True(issue.IsOpen);
        Assert.Contains("band", issue.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_move_inside_the_band_is_not_raised()
    {
        var harness = new Harness();
        harness.WithCalendarThrough(new DateOnly(2026, 12, 31));

        // Six per cent a day, every day, on a seven per cent venue.
        harness.Store(Week(100m, 106m, 112.36m, 119.10m, 126.24m));

        var inspection = await harness.InspectAsync();

        Assert.Empty(inspection.Raised);
    }

    [Fact]
    public async Task An_expected_session_with_no_bar_is_raised()
    {
        var harness = new Harness();
        harness.WithCalendarThrough(new DateOnly(2026, 12, 31));

        // Wednesday absent from an otherwise complete week.
        harness.Store(
            Bar(Monday, 100m),
            Bar(Monday.AddDays(1), 101m),
            Bar(Monday.AddDays(3), 102m),
            Bar(Monday.AddDays(4), 103m));

        // Act
        var inspection = await harness.InspectAsync();

        // Assert
        var issue = Assert.Single(inspection.Raised);
        Assert.Equal(DataQualityIssueKind.MissingSession, issue.Kind);
        Assert.Equal(Monday.AddDays(2), issue.SessionAtUtc);
    }

    [Fact]
    public async Task A_recorded_holiday_is_not_a_missing_session()
    {
        // The whole reason the calendar exists. Without it, every public
        // holiday looks exactly like a failed ingestion run.
        var harness = new Harness();
        harness.WithCalendarThrough(new DateOnly(2026, 12, 31));
        harness.WithHoliday(new DateOnly(2026, 8, 5), "Test closure");

        harness.Store(
            Bar(Monday, 100m),
            Bar(Monday.AddDays(1), 101m),
            Bar(Monday.AddDays(3), 102m),
            Bar(Monday.AddDays(4), 103m));

        // Act
        var inspection = await harness.InspectAsync();

        // Assert
        Assert.Empty(inspection.Raised);
        Assert.Equal(4, inspection.SessionsExpected);
    }

    [Fact]
    public async Task A_weekend_is_never_an_expected_session()
    {
        var harness = new Harness();
        harness.WithCalendarThrough(new DateOnly(2026, 12, 31));
        harness.Store(Week(100m, 101m, 102m, 103m, 104m));

        // Act — the range runs to the following Monday, taking in a weekend.
        var inspection = await harness.InspectAsync(days: 7);

        // Assert
        Assert.Empty(inspection.Raised);
        Assert.Equal(5, inspection.SessionsExpected);
    }

    [Fact]
    public async Task A_bar_on_a_recorded_closure_is_raised()
    {
        // Usually the calendar being wrong rather than the data, and worth
        // knowing either way: a calendar quietly wrong makes every
        // completeness figure computed against it wrong too.
        var harness = new Harness();
        harness.WithCalendarThrough(new DateOnly(2026, 12, 31));
        harness.WithHoliday(new DateOnly(2026, 8, 5), "Test closure");

        harness.Store(Week(100m, 101m, 102m, 103m, 104m));

        // Act
        var inspection = await harness.InspectAsync();

        // Assert
        var issue = Assert.Single(inspection.Raised);
        Assert.Equal(DataQualityIssueKind.UnexpectedSession, issue.Kind);
        Assert.Equal(Monday.AddDays(2), issue.SessionAtUtc);
    }

    [Fact]
    public async Task Calendar_checks_are_skipped_when_the_calendar_does_not_cover_the_window()
    {
        // Raising a finding for every real holiday would bury the genuine ones
        // and make the completeness figure meaningless.
        var harness = new Harness();
        harness.Store(Bar(Monday, 100m));

        // Act — no calendar recorded at all.
        var inspection = await harness.InspectAsync();

        // Assert
        Assert.Null(inspection.Skipped);
        Assert.Empty(inspection.Raised);
        Assert.Equal(0, inspection.SessionsExpected);
    }

    [Fact]
    public async Task Re_inspecting_the_same_range_raises_nothing_a_second_time()
    {
        // A nightly run must not raise yesterday's finding again, or a
        // dismissal made on Monday is buried by Friday.
        var harness = new Harness();
        harness.WithCalendarThrough(new DateOnly(2026, 12, 31));
        harness.Store(Week(100m, 100m, 50m, 50m, 50m));

        var first = await harness.InspectAsync();
        harness.CommitIssues();

        // Act
        var second = await harness.InspectAsync();

        // Assert
        Assert.Single(first.Raised);
        Assert.Empty(second.Raised);
    }

    [Fact]
    public async Task An_index_is_exempt_from_the_price_band()
    {
        // A limit binds orders, and an index is calculated rather than traded.
        var harness = new Harness(assetType: AssetType.Index);
        harness.WithCalendarThrough(new DateOnly(2026, 12, 31));
        harness.Store(Week(100m, 100m, 50m, 50m, 50m));

        var inspection = await harness.InspectAsync();

        Assert.Empty(inspection.Raised);
    }

    [Fact]
    public async Task A_venue_with_no_recorded_band_is_not_checked_against_one()
    {
        // A venue whose limit has not been recorded is not a venue with no
        // limit, and guessing would raise false findings.
        var harness = new Harness(dailyPriceLimitPercent: null);
        harness.WithCalendarThrough(new DateOnly(2026, 12, 31));
        harness.Store(Week(100m, 100m, 50m, 50m, 50m));

        var inspection = await harness.InspectAsync();

        Assert.Empty(inspection.Raised);
    }

    [Fact]
    public async Task The_first_bar_is_compared_against_the_session_before_the_range()
    {
        // A run that ingests one day at a time would otherwise never check
        // anything: every range would hold a single bar with no predecessor.
        var harness = new Harness();
        harness.WithCalendarThrough(new DateOnly(2026, 12, 31));
        harness.Store(Bar(Monday, 100m));

        // Act — a range covering only Tuesday, whose close halves.
        harness.Store(Bar(Monday.AddDays(1), 50m));
        var inspection = await harness.InspectAsync(from: Monday.AddDays(1), days: 1);

        // Assert
        var issue = Assert.Single(inspection.Raised);
        Assert.Equal(DataQualityIssueKind.PriceLimitBreach, issue.Kind);
        Assert.Equal(Monday.AddDays(1), issue.SessionAtUtc);
    }

    [Fact]
    public async Task Bars_staged_but_not_committed_are_inspected()
    {
        // What lets ingestion store a bar and the finding about it in one
        // transaction.
        var harness = new Harness();
        harness.WithCalendarThrough(new DateOnly(2026, 12, 31));
        harness.Store(Bar(Monday, 100m));

        // Act
        var inspection = await harness.InspectAsync(pending: [Bar(Monday.AddDays(1), 50m)], days: 2);

        // Assert
        var issue = Assert.Single(inspection.Raised);
        Assert.Equal(DataQualityIssueKind.PriceLimitBreach, issue.Kind);
    }

    [Theory]
    [InlineData(BarInterval.OneMinute)]
    [InlineData(BarInterval.OneHour)]
    public async Task Intraday_resolutions_are_not_checked(BarInterval interval)
    {
        // A price limit governs a session. Comparing two five-minute bars
        // against it would flag nothing on a day a security moved its full
        // band and everything on a day it gapped at the open.
        var harness = new Harness();

        var inspection = await harness.InspectAsync(interval: interval);

        Assert.NotNull(inspection.Skipped);
        Assert.Empty(inspection.Raised);
    }

    [Fact]
    public async Task An_unknown_instrument_is_reported_rather_than_checked()
    {
        var harness = new Harness(knownInstrument: false);

        var inspection = await harness.InspectAsync();

        Assert.Contains("No instrument", inspection.Skipped, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_finding_can_be_explained_and_stops_being_open()
    {
        // The other half of "stays open until something accounts for it".
        // Without a way to close one, the open set only grows and the
        // consistency score decays permanently.
        var harness = new Harness();
        harness.WithCalendarThrough(new DateOnly(2026, 12, 31));
        harness.Store(Week(100m, 100m, 50m, 50m, 50m));

        var inspection = await harness.InspectAsync();
        harness.CommitIssues();

        var raised = Assert.Single(inspection.Raised);

        // Act
        var resolved = await harness.ResolveAsync(
            raised.Id, DataQualityResolution.Explained, "A two-for-one split on 2026-08-05.");

        // Assert
        Assert.NotNull(resolved);
        Assert.False(resolved.IsOpen);
        Assert.Equal(DataQualityIssueStatus.Explained, resolved.Status);
        Assert.Empty(await harness.ListOpenAsync());
    }

    [Fact]
    public async Task A_finding_can_be_dismissed_as_not_a_problem()
    {
        var harness = new Harness();
        harness.WithCalendarThrough(new DateOnly(2026, 12, 31));
        harness.Store(Week(100m, 100m, 50m, 50m, 50m));

        var inspection = await harness.InspectAsync();
        harness.CommitIssues();

        var resolved = await harness.ResolveAsync(
            inspection.Raised[0].Id,
            DataQualityResolution.Dismissed,
            "The venue confirmed the print.");

        Assert.Equal(DataQualityIssueStatus.Dismissed, resolved!.Status);
    }

    [Fact]
    public async Task Resolving_a_finding_that_does_not_exist_reports_nothing()
    {
        var harness = new Harness();

        var resolved = await harness.ResolveAsync(
            DataQualityIssueId.New(), DataQualityResolution.Dismissed, "Benign.");

        Assert.Null(resolved);
    }

    [Fact]
    public async Task A_dismissal_survives_the_next_inspection()
    {
        // A nightly run must not raise a fresh copy of a finding somebody has
        // already decided about.
        var harness = new Harness();
        harness.WithCalendarThrough(new DateOnly(2026, 12, 31));
        harness.Store(Week(100m, 100m, 50m, 50m, 50m));

        var first = await harness.InspectAsync();
        harness.CommitIssues();
        await harness.ResolveAsync(
            first.Raised[0].Id, DataQualityResolution.Dismissed, "Benign.");

        // Act
        var second = await harness.InspectAsync();

        // Assert
        Assert.Empty(second.Raised);
        Assert.Empty(await harness.ListOpenAsync());
    }

    private static OhlcvBar[] Week(
        decimal monday,
        decimal tuesday,
        decimal wednesday,
        decimal thursday,
        decimal friday) =>
        [
            Bar(Monday, monday),
            Bar(Monday.AddDays(1), tuesday),
            Bar(Monday.AddDays(2), wednesday),
            Bar(Monday.AddDays(3), thursday),
            Bar(Monday.AddDays(4), friday),
        ];

    private static OhlcvBar Bar(DateTimeOffset openedAtUtc, decimal close) =>
        OhlcvBar.Record(
            HarnessInstrumentId,
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

    /// <summary>
    /// Fixed so the bars a test builds and the instrument the harness resolves
    /// agree without the test having to thread an identifier through.
    /// </summary>
    private static readonly InstrumentId HarnessInstrumentId = InstrumentId.New();

    /// <summary>Wires the real inspector over in-memory repositories.</summary>
    private sealed class Harness
    {
        private readonly InMemoryExchanges _exchanges = new();
        private readonly InMemoryInstrumentMaster _master = new();
        private readonly FakeDataQualityRepository _issues = new();
        private readonly BarQualityInspector _inspector;
        private readonly DataQualityService _quality;

        public Harness(
            bool knownInstrument = true,
            AssetType assetType = AssetType.Equity,
            decimal? dailyPriceLimitPercent = 7m)
        {
            var venue = _exchanges.Add("QLT", Now, dailyPriceLimitPercent);

            if (knownInstrument)
            {
                var instrument = Instrument.Register(
                    venue, Ticker.Create("QLA"), "Quality Company", assetType, CurrencyCode.Vnd, Now);

                instrument.List(Now);
                _master.Seed(WithIdentity(instrument));
            }

            Bars = new FakeBarRepository();

            _inspector = new BarQualityInspector(
                _master,
                _exchanges,
                new TradingCalendar(_exchanges),
                Bars,
                _issues,
                new FakeClock(Now),
                NullLogger<BarQualityInspector>.Instance);

            _quality = new DataQualityService(
                _master,
                new TradingCalendar(_exchanges),
                Bars,
                _issues,
                new FakeIngestionJournal(),
                new CommittingUnitOfWork(_issues),
                new FakeClock(Now));
        }

        public FakeBarRepository Bars { get; }

        public void Store(params OhlcvBar[] bars) => Bars.AddRange(bars);

        public void WithHoliday(DateOnly date, string name) =>
            _exchanges.AddHoliday(TradingHoliday.Record(VenueId, date, name, Now));

        /// <summary>
        /// Declares that the venue's calendar was transcribed up to a date.
        /// </summary>
        /// <remarks>
        /// This used to plant a closure far in the future, because coverage was
        /// inferred from the furthest recorded one. That made the tests encode
        /// the defect: a calendar was "covering" precisely when it happened to
        /// hold a late holiday, which is how a year with no rows at all came to
        /// look covered in production.
        /// </remarks>
        public void WithCalendarThrough(DateOnly through) =>
            _exchanges.DeclareCoverage(VenueId, new DateOnly(2000, 1, 1), through.AddDays(1));

        /// <summary>Makes staged findings visible, as a commit would.</summary>
        public void CommitIssues() => _issues.Commit();

        public Task<DataQualityIssue?> ResolveAsync(
            DataQualityIssueId issueId,
            DataQualityResolution outcome,
            string reason) =>
            _quality.ResolveIssueAsync(
                issueId, outcome, reason, TestContext.Current.CancellationToken);

        public Task<IReadOnlyList<DataQualityIssue>> ListOpenAsync() =>
            _quality.ListOpenIssuesAsync(
                HarnessInstrumentId, BarInterval.OneDay, 50, TestContext.Current.CancellationToken);

        public Task<QualityInspection> InspectAsync(
            BarInterval interval = BarInterval.OneDay,
            DateTimeOffset? from = null,
            int days = 5,
            IReadOnlyList<OhlcvBar>? pending = null)
        {
            var start = from ?? Monday;

            return _inspector.InspectAsync(
                HarnessInstrumentId,
                interval,
                start,
                start.AddDays(days),
                pending ?? [],
                TestContext.Current.CancellationToken);
        }

        private ExchangeId VenueId =>
            _exchanges.ListAsync(TestContext.Current.CancellationToken).Result[0].Id;

        /// <summary>
        /// Forces the instrument's identifier to the one the test bars carry.
        /// </summary>
        /// <remarks>
        /// The aggregate issues its own identifier, and the bars are built
        /// before it exists. Reflection is the narrowest way to make the two
        /// agree without adding a test-only setter to the domain.
        /// </remarks>
        private static Instrument WithIdentity(Instrument instrument)
        {
            typeof(Instrument)
                .GetProperty(nameof(Instrument.Id))!
                .SetValue(instrument, HarnessInstrumentId);

            return instrument;
        }
    }

    /// <summary>
    /// A unit of work that makes the findings store's staged writes visible.
    /// </summary>
    /// <remarks>
    /// The resolution path commits through the unit of work, so the fake has to
    /// do what the real one does or the test would assert against writes that
    /// never landed.
    /// </remarks>
    private sealed class CommittingUnitOfWork(FakeDataQualityRepository issues)
        : PersonalQuant.Application.Abstractions.IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            issues.Commit();

            return Task.FromResult(0);
        }
    }

    /// <summary>An in-memory findings store that stages until told to commit.</summary>
    private sealed class FakeDataQualityRepository : IDataQualityRepository
    {
        private readonly List<DataQualityIssue> _committed = [];
        private readonly List<DataQualityIssue> _staged = [];

        public void Commit()
        {
            _committed.AddRange(_staged);
            _staged.Clear();
        }

        public Task<IReadOnlyList<DataQualityIssue>> ListAsync(
            InstrumentId instrumentId,
            BarInterval interval,
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DataQualityIssue>>(
                [.. _committed.Where(issue =>
                    issue.InstrumentId == instrumentId
                    && issue.Interval == interval
                    && issue.SessionAtUtc >= fromUtc
                    && issue.SessionAtUtc < toUtc)]);

        public Task<IReadOnlyList<DataQualityIssue>> ListOpenAsync(
            InstrumentId instrumentId,
            BarInterval interval,
            int limit,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DataQualityIssue>>(
                [.. _committed.Where(issue => issue.IsOpen).Take(limit)]);

        public Task<IReadOnlyDictionary<DataQualityIssueKind, int>> CountOpenByKindAsync(
            InstrumentId instrumentId,
            BarInterval interval,
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<DataQualityIssueKind, int>>(
                _committed
                    .Where(issue => issue.IsOpen)
                    .GroupBy(issue => issue.Kind)
                    .ToDictionary(group => group.Key, group => group.Count()));

        public Task<DataQualityIssue?> FindAsync(
            DataQualityIssueId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_committed.Find(issue => issue.Id == id));

        public void Add(DataQualityIssue issue) => _staged.Add(issue);
    }
}
