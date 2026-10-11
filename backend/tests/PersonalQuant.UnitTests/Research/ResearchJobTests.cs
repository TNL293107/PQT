using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.Research;

namespace PersonalQuant.UnitTests.Research;

/// <summary>
/// Verifies the one transition the backend makes on a research job: queuing it.
/// </summary>
public sealed class ResearchJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 4, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_submitted_job_is_queued_and_unclaimed()
    {
        var job = ResearchJob.Submit("momentum_slice", "{\"dataset_id\":\"x\"}", Now);

        Assert.False(job.Id.IsEmpty);
        Assert.Equal(ResearchJobStatus.Queued, job.Status);
        Assert.Equal(0, job.Attempts);
        Assert.Null(job.ClaimedBy);
        Assert.Null(job.LeaseUntilUtc);
        Assert.Null(job.FinishedAtUtc);
        Assert.Equal(Now, job.CreatedAtUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Momentum")]
    [InlineData("momentum-slice")]
    [InlineData("1momentum")]
    [InlineData("momentum slice")]
    public void A_kind_the_worker_could_not_name_a_handler_after_is_refused(string kind)
    {
        // Kinds are handler names in Python; anything else would be queued to
        // fail as "no handler" in another process, minutes later.
        Assert.Throws<DomainValidationException>(() => ResearchJob.Submit(kind, "{}", Now));
    }

    [Fact]
    public void A_kind_longer_than_the_column_is_refused() =>
        Assert.Throws<DomainValidationException>(
            () => ResearchJob.Submit(new string('a', ResearchJob.MaxKindLength + 1), "{}", Now));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_job_without_a_payload_is_refused(string payload) =>
        Assert.Throws<DomainValidationException>(() => ResearchJob.Submit("momentum_slice", payload, Now));

    [Fact]
    public void An_oversized_payload_is_refused() =>
        Assert.Throws<DomainValidationException>(
            () => ResearchJob.Submit("momentum_slice", new string('x', ResearchJob.MaxPayloadLength + 1), Now));

    [Fact]
    public void A_submission_time_must_be_utc() =>
        Assert.Throws<DomainValidationException>(
            () => ResearchJob.Submit("momentum_slice", "{}", new DateTimeOffset(2026, 10, 11, 11, 0, 0, TimeSpan.FromHours(7))));
}
