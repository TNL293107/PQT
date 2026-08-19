using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.UnitTests.MarketData;

/// <summary>
/// Verifies the checkpoint and the audit record — the two pieces of state that
/// decide whether a series can be resumed and whether a gap can be explained.
/// </summary>
public sealed class IngestionStateTests
{
    private static readonly DateTimeOffset Period = new(2026, 8, 25, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 8, 26, 1, 0, 0, TimeSpan.Zero);
    private static readonly SourceCode Source = SourceCode.Create("TEST");

    [Fact]
    public void A_checkpoint_resumes_one_interval_past_the_last_stored_bar()
    {
        // Neither repeating the period it already has nor skipping the next.
        var checkpoint = StartCheckpoint();

        Assert.Equal(Period.AddDays(1), checkpoint.ResumeFromUtc);
    }

    [Fact]
    public void Advancing_a_checkpoint_forward_moves_it()
    {
        var checkpoint = StartCheckpoint();
        var later = Period.AddDays(3);

        // Act
        var moved = checkpoint.Advance(later, Now.AddDays(3));

        // Assert
        Assert.True(moved);
        Assert.Equal(later, checkpoint.LastBarOpenedAtUtc);
        Assert.Equal(Now.AddDays(3), checkpoint.LastSucceededAtUtc);
    }

    [Fact]
    public void A_checkpoint_never_moves_backwards()
    {
        // A provider serving a stale cache must not be able to make the system
        // re-ingest history it already has, or report progress it has lost.
        var checkpoint = StartCheckpoint();

        // Act
        var moved = checkpoint.Advance(Period.AddDays(-5), Now.AddDays(1));

        // Assert
        Assert.False(moved);
        Assert.Equal(Period, checkpoint.LastBarOpenedAtUtc);
        Assert.Equal(Now.AddDays(1), checkpoint.LastSucceededAtUtc);
    }

    [Fact]
    public void A_successful_run_that_returned_nothing_still_records_the_success()
    {
        // "Nothing new" and "we could not tell" look identical from outside
        // and mean opposite things.
        var checkpoint = StartCheckpoint();

        checkpoint.RecordSuccessWithoutProgress(Now.AddDays(2));

        Assert.Equal(Period, checkpoint.LastBarOpenedAtUtc);
        Assert.Equal(Now.AddDays(2), checkpoint.LastSucceededAtUtc);
    }

    [Fact]
    public void A_checkpoint_off_a_period_boundary_is_rejected()
    {
        Assert.Throws<DomainValidationException>(() => IngestionCheckpoint.Start(
            InstrumentId.New(), BarInterval.OneDay, Source, Period.AddHours(2), Now));

        var checkpoint = StartCheckpoint();

        Assert.Throws<DomainValidationException>(
            () => checkpoint.Advance(Period.AddDays(1).AddHours(2), Now));
    }

    [Fact]
    public void A_run_opens_as_running_and_closes_once()
    {
        var run = StartRun();

        Assert.Equal(IngestionOutcome.Running, run.Outcome);
        Assert.Null(run.CompletedAtUtc);

        // Act
        run.Succeed(new IngestionCounts(10, 9, 1, 8, 1), attempts: 1, RawBatchId.New(), Now);

        // Assert
        Assert.Equal(IngestionOutcome.Succeeded, run.Outcome);
        Assert.Equal(10, run.BarsFetched);
        Assert.Equal(9, run.BarsAccepted);
        Assert.Equal(1, run.BarsRejected);
        Assert.Equal(8, run.BarsStored);
        Assert.Equal(1, run.BarsRevised);
        Assert.Equal(Now, run.CompletedAtUtc);

        Assert.Throws<DomainStateException>(
            () => run.Fail("late", attempts: 1, Now));
    }

    [Fact]
    public void A_failed_run_records_a_reason_and_the_attempts_it_took()
    {
        var run = StartRun();

        // Act
        run.Fail("The provider did not answer.", attempts: 3, Now);

        // Assert
        Assert.Equal(IngestionOutcome.Failed, run.Outcome);
        Assert.Equal(3, run.Attempts);
        Assert.Equal("The provider did not answer.", run.FailureReason);
        Assert.Null(run.RawBatchId);
    }

    [Fact]
    public void A_skipped_run_is_recorded_rather_than_omitted()
    {
        // A schedule that skips every night for a month is a bug, and it is
        // only visible if the skips are written down.
        var run = StartRun();

        run.Skip("No period has finished since the last run.", Now);

        Assert.Equal(IngestionOutcome.Skipped, run.Outcome);
        Assert.Equal("No period has finished since the last run.", run.FailureReason);
    }

    [Fact]
    public void An_over_long_failure_reason_is_truncated_rather_than_refused()
    {
        // The audit row exists to explain a gap. Losing the row because the
        // explanation was verbose would be the wrong trade.
        var run = StartRun();

        run.Fail(new string('x', IngestionRun.MaxFailureReasonLength + 500), attempts: 1, Now);

        Assert.Equal(IngestionRun.MaxFailureReasonLength, run.FailureReason!.Length);
    }

    [Fact]
    public void A_blank_failure_reason_still_says_something()
    {
        var run = StartRun();

        run.Fail("   ", attempts: 1, Now);

        Assert.False(string.IsNullOrWhiteSpace(run.FailureReason));
    }

    [Fact]
    public void A_run_over_an_empty_range_is_rejected() =>
        Assert.Throws<DomainValidationException>(() => IngestionRun.Start(
            Source, InstrumentId.New(), BarInterval.OneDay, Period, Period, Now));

    private static IngestionCheckpoint StartCheckpoint() =>
        IngestionCheckpoint.Start(InstrumentId.New(), BarInterval.OneDay, Source, Period, Now);

    private static IngestionRun StartRun() =>
        IngestionRun.Start(
            Source, InstrumentId.New(), BarInterval.OneDay, Period, Period.AddDays(5), Now);
}
