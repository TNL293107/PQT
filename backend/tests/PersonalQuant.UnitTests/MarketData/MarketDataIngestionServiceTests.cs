using Microsoft.Extensions.Logging.Abstractions;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.UnitTests.MarketData.Fakes;

namespace PersonalQuant.UnitTests.MarketData;

/// <summary>
/// Verifies the ingestion pipeline end to end above the database.
/// </summary>
/// <remarks>
/// The real normaliser, fetcher, registry and policy are used; only the ports
/// that would reach PostgreSQL are substituted. What is under test is the
/// sequence — fetch, validate, deduplicate, persist, audit — and the decisions
/// around it, none of which need a database to be wrong.
/// </remarks>
public sealed class MarketDataIngestionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 26, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Yesterday = new(2026, 8, 25, 0, 0, 0, TimeSpan.Zero);
    private static readonly SourceCode Source = SourceCode.Create("STUB");

    [Fact]
    public async Task A_clean_run_stores_the_bars_retains_the_payload_and_records_the_run()
    {
        var harness = new Harness();
        harness.Returns(Bar(Yesterday.AddDays(-1)), Bar(Yesterday));

        // Act
        var run = await harness.IngestAsync();

        // Assert
        Assert.Equal(IngestionOutcome.Succeeded, run.Outcome);
        Assert.Equal(2, run.BarsFetched);
        Assert.Equal(2, run.BarsAccepted);
        Assert.Equal(0, run.BarsRejected);
        Assert.Equal(2, run.BarsStored);
        Assert.Equal(0, run.BarsRevised);
        Assert.Equal(2, harness.Bars.All.Count);
        Assert.Single(harness.Journal.RawBatches);
        Assert.Equal(harness.Journal.RawBatches[0].Id, run.RawBatchId);
        Assert.Single(harness.Journal.Runs);
        Assert.Equal(1, harness.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task The_quality_rules_see_the_bars_before_they_are_committed()
    {
        // A bar committed without the finding about it would look clean, and
        // nothing would know to re-check it.
        var harness = new Harness();
        harness.Returns(Bar(Yesterday.AddDays(-1)), Bar(Yesterday));

        // Act
        await harness.IngestAsync();

        // Assert
        Assert.Equal(1, harness.Inspector.InspectionCount);
        Assert.Equal(2, harness.Inspector.LastPending.Count);
        Assert.Equal(1, harness.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task The_checkpoint_lands_on_the_newest_bar_actually_stored()
    {
        // Never on the end of the requested range. A request for a week that
        // returned three days must resume on the fourth.
        var harness = new Harness();
        harness.Returns(Bar(Yesterday.AddDays(-4)), Bar(Yesterday.AddDays(-3)));

        // Act
        await harness.IngestAsync();

        // Assert
        var checkpoint = Assert.Single(harness.Journal.Checkpoints);
        Assert.Equal(Yesterday.AddDays(-3), checkpoint.LastBarOpenedAtUtc);
        Assert.Equal(Yesterday.AddDays(-2), checkpoint.ResumeFromUtc);
    }

    [Fact]
    public async Task A_second_run_resumes_from_the_checkpoint_rather_than_the_backfill_start()
    {
        var harness = new Harness();
        harness.Returns(Bar(Yesterday.AddDays(-2)), Bar(Yesterday.AddDays(-1)));
        await harness.IngestAsync();

        DateTimeOffset? secondFrom = null;
        harness.Intercept(request =>
        {
            secondFrom = request.FromUtc;
            return [Bar(Yesterday)];
        });

        // Act
        await harness.IngestAsync();

        // Assert
        Assert.Equal(Yesterday, secondFrom);
        Assert.Equal(3, harness.Bars.All.Count);
    }

    [Fact]
    public async Task Re_fetching_an_unchanged_period_stores_nothing_and_revises_nothing()
    {
        // The normal case for an overlapping range. Counting it as a revision
        // would make every schedule look like a provider restating history.
        var harness = new Harness();
        harness.Returns(Bar(Yesterday));
        await harness.IngestAsync();

        harness.Intercept(_ => [Bar(Yesterday)]);

        // Act
        var run = await harness.IngestAsync(from: Yesterday);

        // Assert
        Assert.Equal(0, run.BarsStored);
        Assert.Equal(0, run.BarsRevised);
        Assert.Single(harness.Bars.All);
    }

    [Fact]
    public async Task A_restated_period_is_revised_rather_than_duplicated()
    {
        var harness = new Harness();
        harness.Returns(Bar(Yesterday, close: 105m));
        await harness.IngestAsync();

        harness.Intercept(_ => [Bar(Yesterday, close: 108m)]);

        // Act
        var run = await harness.IngestAsync(from: Yesterday);

        // Assert
        Assert.Equal(0, run.BarsStored);
        Assert.Equal(1, run.BarsRevised);
        var stored = Assert.Single(harness.Bars.All);
        Assert.Equal(108m, stored.Close.Value);
        Assert.Equal(1, stored.Revision);
    }

    [Fact]
    public async Task Rejected_rows_are_counted_and_the_run_still_succeeds()
    {
        // A source sending some bad rows is not a failed run; it is a run with
        // rejections, and the counts are what distinguish the two.
        var harness = new Harness();
        harness.Returns(
            Bar(Yesterday.AddDays(-1)),
            Bar(Yesterday) with { High = 1m });

        // Act
        var run = await harness.IngestAsync();

        // Assert
        Assert.Equal(IngestionOutcome.Succeeded, run.Outcome);
        Assert.Equal(2, run.BarsFetched);
        Assert.Equal(1, run.BarsAccepted);
        Assert.Equal(1, run.BarsRejected);
        Assert.Equal(1, run.BarsStored);
    }

    [Fact]
    public async Task A_provider_failure_is_recorded_and_advances_nothing()
    {
        var harness = new Harness();
        harness.Fails(new MarketDataProviderException("the source is down", isTransient: true));

        // Act
        var run = await harness.IngestAsync();

        // Assert
        Assert.Equal(IngestionOutcome.Failed, run.Outcome);
        Assert.Equal("the source is down", run.FailureReason);
        Assert.Equal(3, run.Attempts);
        Assert.Empty(harness.Bars.All);
        Assert.Empty(harness.Journal.Checkpoints);
        Assert.Empty(harness.Journal.RawBatches);
        Assert.Single(harness.Journal.Runs);
    }

    [Fact]
    public async Task An_unknown_instrument_is_skipped_with_a_reason()
    {
        var harness = new Harness(knownInstrument: false);
        harness.Returns(Bar(Yesterday));

        // Act
        var run = await harness.IngestAsync();

        // Assert
        Assert.Equal(IngestionOutcome.Skipped, run.Outcome);
        Assert.Contains("No instrument exists", run.FailureReason, StringComparison.Ordinal);
        Assert.Equal(0, harness.Provider.CallCount);
    }

    [Fact]
    public async Task An_unregistered_source_is_skipped_with_a_reason()
    {
        var harness = new Harness();
        harness.Returns(Bar(Yesterday));

        // Act
        var run = await harness.IngestAsync(source: SourceCode.Create("ABSENT"));

        // Assert
        Assert.Equal(IngestionOutcome.Skipped, run.Outcome);
        Assert.Contains("ABSENT", run.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_resolution_the_source_does_not_serve_is_skipped_rather_than_retried()
    {
        // Failing three times against an endpoint that was never going to
        // answer costs a rate-limit allowance and explains nothing.
        var harness = new Harness(
            supportedIntervals: new HashSet<BarInterval> { BarInterval.OneDay });
        harness.Returns(Bar(Yesterday));

        // Act
        var run = await harness.IngestAsync(interval: BarInterval.OneHour);

        // Assert
        Assert.Equal(IngestionOutcome.Skipped, run.Outcome);
        Assert.Equal(0, harness.Provider.CallCount);
    }

    [Fact]
    public async Task With_two_sources_only_one_of_which_serves_the_resolution_that_one_runs()
    {
        // The rule that changes when a second provider appears: an unnamed
        // instruction resolves when exactly one registered source can serve it,
        // not only when exactly one is registered. Under the old rule this was
        // a skipped run, and the nightly pass silently stopped ingesting.
        var intraday = new ScriptedProvider(
            SourceCode.Create("INTRA"),
            _ => throw new NotSupportedException("The wrong source was called."))
        {
            Capability = TestCapability.For(
                SourceCode.Create("INTRA"),
                new HashSet<BarInterval> { BarInterval.OneMinute }),
        };

        var harness = new Harness(
            supportedIntervals: new HashSet<BarInterval> { BarInterval.OneDay },
            second: intraday);
        harness.Returns(Bar(Yesterday));

        // Act
        var run = await harness.IngestAsync();

        // Assert
        Assert.NotEqual(IngestionOutcome.Skipped, run.Outcome);
        Assert.Equal(1, harness.Provider.CallCount);
        Assert.Equal(0, intraday.CallCount);
    }

    [Fact]
    public async Task A_source_that_fails_is_never_followed_by_a_second_one()
    {
        // The property, not the absence of a feature. A second source is a
        // second symbology, a second adjustment convention and a second
        // restatement policy; falling through to one would assemble a series
        // out of both and nothing downstream could tell.
        var standby = new ScriptedProvider(
            SourceCode.Create("STANDBY"),
            _ => throw new NotSupportedException("A fallback was attempted."))
        {
            Capability = TestCapability.For(
                SourceCode.Create("STANDBY"),
                new HashSet<BarInterval> { BarInterval.OneMinute }),
        };

        var harness = new Harness(
            supportedIntervals: new HashSet<BarInterval> { BarInterval.OneDay },
            second: standby);
        harness.Fails(new MarketDataProviderException("The source is down.", isTransient: true));

        // Act
        var run = await harness.IngestAsync();

        // Assert
        Assert.Equal(IngestionOutcome.Failed, run.Outcome);
        Assert.Equal(0, standby.CallCount);
    }

    [Fact]
    public async Task Two_sources_that_could_both_serve_it_skip_the_run_rather_than_pick_one()
    {
        // Ambiguity is an error and stays one. Choosing by registration order
        // would attribute the series to whichever was composed first.
        var other = new ScriptedProvider(
            SourceCode.Create("OTHER"),
            _ => throw new NotSupportedException("The wrong source was called."));

        var harness = new Harness(second: other);
        harness.Returns(Bar(Yesterday));

        // Act
        var run = await harness.IngestAsync();

        // Assert
        Assert.Equal(IngestionOutcome.Skipped, run.Outcome);
        Assert.Contains("OTHER", run.FailureReason, StringComparison.Ordinal);
        Assert.Equal(0, harness.Provider.CallCount);
        Assert.Equal(0, other.CallCount);
    }

    [Fact]
    public async Task A_resolution_no_source_serves_names_the_source_and_the_resolution()
    {
        // The reason lands in the run that explains a gap in a series. "The
        // provider cannot serve this request" is a gap nobody can close.
        var harness = new Harness(
            supportedIntervals: new HashSet<BarInterval> { BarInterval.OneDay });
        harness.Returns(Bar(Yesterday));

        // Act
        var run = await harness.IngestAsync(interval: BarInterval.OneHour);

        // Assert
        Assert.Equal(IngestionOutcome.Skipped, run.Outcome);
        Assert.Contains("OneHour", run.FailureReason, StringComparison.Ordinal);
        Assert.Contains(Source.Value, run.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_run_with_no_finished_period_since_the_last_one_is_skipped()
    {
        var harness = new Harness();
        harness.Returns(Bar(Yesterday));
        await harness.IngestAsync();

        // Act — the clock has not moved, so nothing new has closed.
        var run = await harness.IngestAsync();

        // Assert
        Assert.Equal(IngestionOutcome.Skipped, run.Outcome);
        Assert.Contains("No period has finished", run.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_period_in_progress_is_never_requested()
    {
        // A daily bar fetched at midday is a real number that will be a
        // different real number by the close.
        var harness = new Harness();
        DateTimeOffset? requestedTo = null;
        harness.Intercept(request =>
        {
            requestedTo = request.ToUtc;
            return [Bar(Yesterday)];
        });

        // Act
        await harness.IngestAsync();

        // Assert — the clock reads 03:00 on the 26th, so the 26th is still open.
        Assert.Equal(new DateTimeOffset(2026, 8, 26, 0, 0, 0, TimeSpan.Zero), requestedTo);
    }

    [Fact]
    public async Task An_end_beyond_the_last_finished_period_is_clamped()
    {
        var harness = new Harness();
        DateTimeOffset? requestedTo = null;
        harness.Intercept(request =>
        {
            requestedTo = request.ToUtc;
            return [Bar(Yesterday)];
        });

        // Act
        await harness.IngestAsync(to: Now.AddDays(30));

        // Assert
        Assert.Equal(new DateTimeOffset(2026, 8, 26, 0, 0, 0, TimeSpan.Zero), requestedTo);
    }

    [Fact]
    public async Task A_range_longer_than_one_request_allows_is_truncated_rather_than_refused()
    {
        // A large backfill completes over several runs instead of failing on
        // the first.
        var harness = new Harness();
        MarketDataRequest? seen = null;
        harness.Intercept(request =>
        {
            seen = request;
            return [];
        });

        // Act
        var run = await harness.IngestAsync(
            interval: BarInterval.OneMinute,
            from: Now.AddYears(-1));

        // Assert
        Assert.NotEqual(IngestionOutcome.Skipped, run.Outcome);
        Assert.NotNull(seen);
        Assert.Equal(MarketDataRequest.MaxPeriods, seen.Periods);
    }

    [Fact]
    public async Task A_successful_run_that_returned_nothing_leaves_the_checkpoint_where_it_was()
    {
        var harness = new Harness();
        harness.Returns(Bar(Yesterday.AddDays(-1)));
        await harness.IngestAsync();

        harness.Intercept(_ => []);
        harness.Clock.UtcNow = Now.AddDays(2);

        // Act
        var run = await harness.IngestAsync();

        // Assert
        Assert.Equal(IngestionOutcome.Succeeded, run.Outcome);
        var checkpoint = Assert.Single(harness.Journal.Checkpoints);
        Assert.Equal(Yesterday.AddDays(-1), checkpoint.LastBarOpenedAtUtc);
        Assert.Equal(Now.AddDays(2), checkpoint.LastSucceededAtUtc);
    }

    [Fact]
    public async Task No_checkpoint_is_created_when_a_first_run_returns_nothing()
    {
        // Creating one at the requested start would claim a range had been
        // covered that produced no data.
        var harness = new Harness();
        harness.Intercept(_ => []);

        // Act
        var run = await harness.IngestAsync();

        // Assert
        Assert.Equal(IngestionOutcome.Succeeded, run.Outcome);
        Assert.Empty(harness.Journal.Checkpoints);
    }

    [Fact]
    public async Task A_stored_bar_opens_an_observation_window_at_the_run_instant()
    {
        var harness = new Harness();
        harness.Returns(Bar(Yesterday));

        // Act
        await harness.IngestAsync();

        // Assert
        var revision = Assert.Single(harness.Bars.Revisions);
        Assert.Equal(0, revision.Revision);
        Assert.Equal(Now, revision.ObservedFromUtc);
        Assert.Null(revision.ObservedToUtc);
        Assert.True(revision.IsCurrent);
    }

    [Fact]
    public async Task The_first_window_opens_when_the_bar_says_it_was_ingested()
    {
        // The two views of first observation must agree. If they can drift, the
        // migration that seeds a history from ingested_at_utc is seeding the
        // wrong instant and nothing would say so.
        var harness = new Harness();
        harness.Returns(Bar(Yesterday));

        // Act
        await harness.IngestAsync();

        // Assert
        var bar = Assert.Single(harness.Bars.All);
        var revision = Assert.Single(harness.Bars.Revisions);
        Assert.Equal(bar.IngestedAtUtc, revision.ObservedFromUtc);
    }

    [Fact]
    public async Task A_restatement_closes_the_old_window_where_the_new_one_opens()
    {
        // Arrange — observe 100, then observe a correction to 101.
        var harness = new Harness();
        harness.Returns(Bar(Yesterday, close: 100m));
        await harness.IngestAsync();

        var corrected = Now.AddDays(1);
        harness.Clock.UtcNow = corrected;
        harness.Returns(Bar(Yesterday, close: 101m));

        // Act
        await harness.IngestAsync(from: Yesterday, to: Yesterday.AddDays(1));

        // Assert
        Assert.Equal(2, harness.Bars.Revisions.Count);

        var first = harness.Bars.Revisions[0];
        var second = harness.Bars.Revisions[1];

        Assert.Equal(100m, first.Close.Value);
        Assert.Equal(101m, second.Close.Value);
        Assert.Equal(0, first.Revision);
        Assert.Equal(1, second.Revision);

        // One instant, used twice. A second clock read would leave a gap here
        // wide enough for an as-of query to fall into and find nothing.
        Assert.Equal(corrected, first.ObservedToUtc);
        Assert.Equal(corrected, second.ObservedFromUtc);
        Assert.Equal(first.ObservedToUtc, second.ObservedFromUtc);
        Assert.True(second.IsCurrent);
    }

    [Fact]
    public async Task Re_fetching_an_unchanged_period_writes_no_revision()
    {
        // The bar is unchanged, so nothing was observed anew. A revision per
        // fetch would bury the statements that actually differ.
        var harness = new Harness();
        harness.Returns(Bar(Yesterday, close: 100m));
        await harness.IngestAsync();

        harness.Clock.UtcNow = Now.AddDays(1);
        harness.Returns(Bar(Yesterday, close: 100m));

        // Act
        var run = await harness.IngestAsync(from: Yesterday, to: Yesterday.AddDays(1));

        // Assert
        Assert.Equal(0, run.BarsRevised);
        Assert.Single(harness.Bars.Revisions);
        Assert.True(harness.Bars.Revisions[0].IsCurrent);
    }

    [Fact]
    public async Task The_open_window_always_matches_the_current_bar()
    {
        // The projection and its history are two records of one fact, and a
        // disagreement between them would make every as-of answer suspect.
        var harness = new Harness();
        harness.Returns(Bar(Yesterday, close: 100m));
        await harness.IngestAsync();

        harness.Clock.UtcNow = Now.AddDays(1);
        harness.Returns(Bar(Yesterday, close: 101m));
        await harness.IngestAsync(from: Yesterday, to: Yesterday.AddDays(1));

        harness.Clock.UtcNow = Now.AddDays(2);
        harness.Returns(Bar(Yesterday, close: 102m));
        await harness.IngestAsync(from: Yesterday, to: Yesterday.AddDays(1));

        // Assert
        var bar = Assert.Single(harness.Bars.All);
        var open = Assert.Single(harness.Bars.Revisions, revision => revision.IsCurrent);

        Assert.Equal(bar.Close, open.Close);
        Assert.Equal(bar.Volume, open.Volume);
        Assert.Equal(bar.Source, open.Source);
        Assert.Equal(bar.Revision, open.Revision);
    }

    [Fact]
    public async Task Every_instant_is_covered_by_exactly_one_revision()
    {
        // The property the as-of read depends on. Restate the same period on a
        // sequence of days, then check every boundary and midpoint: never two
        // answers, and never none once the bar has been observed.
        var harness = new Harness();
        var observed = new List<DateTimeOffset>();

        for (var day = 0; day < 5; day++)
        {
            harness.Clock.UtcNow = Now.AddDays(day);
            observed.Add(harness.Clock.UtcNow);
            harness.Returns(Bar(Yesterday, close: 100m + day));
            await harness.IngestAsync(from: Yesterday, to: Yesterday.AddDays(1));
        }

        Assert.Equal(5, harness.Bars.Revisions.Count);

        foreach (var instant in observed
            .SelectMany(at => new[] { at.AddTicks(-1), at, at.AddHours(12) }))
        {
            var matches = harness.Bars.Revisions.Count(revision => revision.WasKnownAt(instant));

            // Before the first observation there is no answer at all, which is
            // the honest one — the system had not seen the bar yet.
            var expected = instant < observed[0] ? 0 : 1;

            Assert.Equal(expected, matches);
        }
    }

    private static ProviderBar Bar(DateTimeOffset openedAtUtc, decimal close = 105m) =>
        new(openedAtUtc, 100m, 110m, 95m, close, 1_000, null);

    /// <summary>
    /// Wires the real pipeline over in-memory ports.
    /// </summary>
    private sealed class Harness
    {
        private readonly MarketDataIngestionService _service;
        private Func<MarketDataRequest, Task<MarketDataFetchResult>> _behaviour =
            _ => Task.FromResult(MarketDataFetchResult.Empty(string.Empty, "text/csv"));

        public Harness(
            bool knownInstrument = true,
            IReadOnlySet<BarInterval>? supportedIntervals = null,
            ScriptedProvider? second = null)
        {
            Second = second;

            InstrumentId = InstrumentId.New();
            Clock = new FakeClock(Now);
            Bars = new FakeBarRepository();
            Journal = new FakeIngestionJournal();
            UnitOfWork = new FakeUnitOfWork();
            Inspector = new NoOpQualityInspector();

            Provider = new ScriptedProvider(Source, request => _behaviour(request))
            {
                Capability = TestCapability.For(
                    Source,
                    supportedIntervals ?? new HashSet<BarInterval>
                    {
                        BarInterval.OneMinute,
                        BarInterval.OneHour,
                        BarInterval.OneDay,
                    }),
            };

            var policy = new IngestionPolicy
            {
                MaxAttempts = 3,
                InitialBackoff = TimeSpan.FromMilliseconds(1),
                MaxBackoff = TimeSpan.FromMilliseconds(4),
                MinimumCallSpacing = TimeSpan.Zero,
                InitialBackfill = TimeSpan.FromDays(30),
            }.Validated();

            var delays = new NoDelayScheduler();
            var limiter = new MarketDataCallLimiter(policy, Clock, delays);

            _service = new MarketDataIngestionService(
                new SingleInstrumentRepository(
                    knownInstrument ? SingleInstrumentRepository.Known(InstrumentId) : null),
                new MarketDataProviderRegistry(
                    second is null ? [Provider] : [Provider, second]),
                new MarketDataFetcher(
                    policy, limiter, delays, NullLogger<MarketDataFetcher>.Instance),
                new MarketDataNormalizer(),
                Inspector,
                Bars,
                Journal,
                UnitOfWork,
                policy,
                Clock,
                NullLogger<MarketDataIngestionService>.Instance);
        }

        public InstrumentId InstrumentId { get; }

        public FakeClock Clock { get; }

        public FakeBarRepository Bars { get; }

        public FakeIngestionJournal Journal { get; }

        public FakeUnitOfWork UnitOfWork { get; }

        public NoOpQualityInspector Inspector { get; }

        public ScriptedProvider Provider { get; }

        public ScriptedProvider? Second { get; }

        public void Returns(params ProviderBar[] bars) =>
            _behaviour = _ => Task.FromResult(
                new MarketDataFetchResult("payload", "text/csv", bars));

        public void Intercept(Func<MarketDataRequest, IReadOnlyList<ProviderBar>> behaviour) =>
            _behaviour = request => Task.FromResult(
                new MarketDataFetchResult("payload", "text/csv", behaviour(request)));

        public void Fails(Exception exception) =>
            _behaviour = _ => Task.FromException<MarketDataFetchResult>(exception);

        public async Task<IngestionRun> IngestAsync(
            BarInterval interval = BarInterval.OneDay,
            SourceCode? source = null,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null)
        {
            Assert.True(IngestionInstruction.TryCreate(
                InstrumentId, interval, source, from, to, out var instruction, out var problem),
                problem);

            return await _service.IngestAsync(instruction, TestContext.Current.CancellationToken);
        }
    }
}
