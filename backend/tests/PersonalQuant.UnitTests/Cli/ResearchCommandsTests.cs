using PersonalQuant.Application.Research;
using PersonalQuant.Cli.CommandLine;
using PersonalQuant.Cli.Commands;
using PersonalQuant.Domain.Research;
using PersonalQuant.UnitTests.Cli.Fakes;

namespace PersonalQuant.UnitTests.Cli;

/// <summary>
/// Verifies the operator's way of queuing research for the quant worker.
/// </summary>
public sealed class ResearchCommandsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 4, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_submission_reaches_the_service_with_what_was_typed()
    {
        var harness = new Harness();

        var code = await harness.RunAsync(
            "research", "submit", "momentum-slice",
            "--dataset", "3856e4ce77841c1a",
            "--version", "3",
            "--quantiles", "5",
            "--rebalance-every", "10");

        Assert.Equal(ExitCode.Ok, code);
        Assert.Equal(new MomentumSliceRequest("3856e4ce77841c1a", 3, 5, 10), harness.Jobs.LastRequest);
        Assert.Contains("queued", harness.Result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_version_the_latest_build_is_left_to_the_worker()
    {
        var harness = new Harness();

        await harness.RunAsync("research", "submit", "momentum-slice", "--dataset", "3856e4ce77841c1a");

        Assert.Equal(new MomentumSliceRequest("3856e4ce77841c1a"), harness.Jobs.LastRequest);
        Assert.Contains("latest build", harness.Result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refused_submission_says_why_and_exits_non_zero()
    {
        var harness = new Harness();
        harness.Jobs.RefuseWith("Dataset 3856e4ce77841c1a has no build. Export it first.");

        var code = await harness.RunAsync(
            "research", "submit", "momentum-slice", "--dataset", "3856e4ce77841c1a");

        Assert.Equal(ExitCode.Refused, code);
        Assert.Contains("Export it first", harness.Problems, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failed_job_is_shown_with_its_error_and_exits_non_zero()
    {
        // So a script can wait on a job and tell success from failure by the
        // exit code alone.
        var harness = new Harness();
        var job = ResearchJob.Submit("momentum_slice", "{}", Now);
        harness.Jobs.Hold(job, ResearchJobStatus.Failed, "dataset not found");

        var code = await harness.RunAsync("research", "show", job.Id.ToString());

        Assert.Equal(ExitCode.Refused, code);
        Assert.Contains("dataset not found", harness.Result, StringComparison.Ordinal);
        Assert.Contains("failed", harness.Result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_empty_queue_says_so()
    {
        var harness = new Harness();

        var code = await harness.RunAsync("research", "list");

        Assert.Equal(ExitCode.Ok, code);
        Assert.Contains("No research jobs", harness.Result, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("research", "submit", "--dataset", "3856e4ce77841c1a")]
    [InlineData("research", "submit", "momentum-slice")]
    [InlineData("research", "submit", "momentum-slice", "--dataset", "x", "--quantiles", "ten")]
    [InlineData("research", "submit", "other-thing", "--dataset", "x")]
    [InlineData("research", "show", "not-a-guid")]
    [InlineData("research", "run")]
    public async Task A_command_line_the_operator_got_wrong_never_reaches_the_deployment(
        params string[] args)
    {
        var harness = Harness.WithNothingConstructible();

        var code = await harness.RunAsync(args);

        Assert.Equal(ExitCode.Usage, code);
        Assert.NotEmpty(harness.Problems);
    }

    private sealed class Harness
    {
        private readonly ResearchCommands _commands;
        private readonly RecordedOutput _output = new();

        public Harness()
            : this(constructible: true)
        {
        }

        private Harness(bool constructible)
        {
            _commands = constructible
                ? new ResearchCommands(new Lazy<IResearchJobService>(() => Jobs), _output.Output)
                : new ResearchCommands(Unreachable.Service<IResearchJobService>(), _output.Output);
        }

        public FakeResearchJobService Jobs { get; } = new();

        public string Result => _output.Result;

        public string Problems => _output.Problems;

        public static Harness WithNothingConstructible() => new(constructible: false);

        public async Task<int> RunAsync(params string[] args)
        {
            Assert.True(CommandArguments.TryParse(args, out var command, out var problem), problem);

            return await _commands.RunAsync(command, TestContext.Current.CancellationToken);
        }
    }

    private sealed class FakeResearchJobService : IResearchJobService
    {
        private readonly List<ResearchJob> _held = [];
        private string? _refusal;

        public MomentumSliceRequest? LastRequest { get; private set; }

        public void RefuseWith(string reason) => _refusal = reason;

        public void Hold(ResearchJob job, ResearchJobStatus status, string? error)
        {
            // The worker's transitions are SQL, not domain methods; a test that
            // needs a finished job sets the fields the way a row read back would.
            typeof(ResearchJob).GetProperty(nameof(ResearchJob.Status))!.SetValue(job, status);
            typeof(ResearchJob).GetProperty(nameof(ResearchJob.Error))!.SetValue(job, error);
            typeof(ResearchJob).GetProperty(nameof(ResearchJob.FinishedAtUtc))!.SetValue(job, Now);
            _held.Add(job);
        }

        public Task<ResearchJobSubmission> SubmitMomentumSliceAsync(
            MomentumSliceRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;

            return Task.FromResult(
                _refusal is { } reason
                    ? new ResearchJobSubmission(null, reason)
                    : new ResearchJobSubmission(ResearchJob.Submit("momentum_slice", "{}", Now), null));
        }

        public Task<ResearchJob?> FindAsync(ResearchJobId id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_held.FirstOrDefault(job => job.Id == id));

        public Task<IReadOnlyList<ResearchJob>> ListRecentAsync(
            int limit,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ResearchJob>>(_held);
    }
}
