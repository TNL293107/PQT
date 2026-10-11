using PersonalQuant.Application.Datasets;
using PersonalQuant.Application.Research;
using PersonalQuant.Domain.Research;
using PersonalQuant.UnitTests.MarketData.Fakes;

namespace PersonalQuant.UnitTests.Research;

/// <summary>
/// Verifies what is queued for the quant worker, and what is refused before it gets there.
/// </summary>
public sealed class ResearchJobServiceTests
{
    private const string DatasetId = "3856e4ce77841c1a";

    private static readonly DateTimeOffset Now = new(2026, 10, 11, 4, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_run_over_an_exported_dataset_is_queued_with_a_snake_case_payload()
    {
        // The payload is read by Python under these names; the latest build is
        // left for the worker to resolve when it runs, so no version is written.
        var harness = new Harness(versions: [1, 2, 3]);

        var submission = await harness.Service.SubmitMomentumSliceAsync(
            new MomentumSliceRequest(DatasetId), TestContext.Current.CancellationToken);

        var job = Assert.Single(harness.Jobs.Added);
        Assert.True(submission.Queued);
        Assert.Equal("momentum_slice", job.Kind);
        Assert.Equal(
            "{\"dataset_id\":\"3856e4ce77841c1a\",\"quantiles\":10,\"rebalance_every\":21,"
                + "\"experiment\":\"momentum_slice\"}",
            job.Payload);
        Assert.Equal(Now, job.CreatedAtUtc);
        Assert.Equal(1, harness.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task A_named_build_is_carried_in_the_payload()
    {
        var harness = new Harness(versions: [1, 2, 3]);

        await harness.Service.SubmitMomentumSliceAsync(
            new MomentumSliceRequest(DatasetId, Version: 2, Quantiles: 5, RebalanceEvery: 10),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "{\"dataset_id\":\"3856e4ce77841c1a\",\"version\":2,\"quantiles\":5,\"rebalance_every\":10,"
                + "\"experiment\":\"momentum_slice\"}",
            Assert.Single(harness.Jobs.Added).Payload);
    }

    [Fact]
    public async Task A_dataset_that_was_never_exported_is_refused_now_rather_than_failed_later()
    {
        var harness = new Harness(versions: []);

        var submission = await harness.Service.SubmitMomentumSliceAsync(
            new MomentumSliceRequest(DatasetId), TestContext.Current.CancellationToken);

        Assert.False(submission.Queued);
        Assert.Contains("Export it first", submission.Refusal, StringComparison.Ordinal);
        Assert.Empty(harness.Jobs.Added);
        Assert.Equal(0, harness.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task A_build_that_does_not_exist_is_refused_naming_the_ones_that_do()
    {
        var harness = new Harness(versions: [1, 3]);

        var submission = await harness.Service.SubmitMomentumSliceAsync(
            new MomentumSliceRequest(DatasetId, Version: 2), TestContext.Current.CancellationToken);

        Assert.False(submission.Queued);
        Assert.Contains("v1, v3", submission.Refusal, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../escape", 10, 21)]
    [InlineData("3856E4CE77841C1A", 10, 21)]
    [InlineData(DatasetId, 0, 21)]
    [InlineData(DatasetId, 101, 21)]
    [InlineData(DatasetId, 10, 0)]
    [InlineData(DatasetId, 10, 253)]
    public async Task Parameters_the_worker_would_refuse_are_refused_here(
        string datasetId,
        int quantiles,
        int rebalanceEvery)
    {
        var harness = new Harness(versions: [1]);

        var submission = await harness.Service.SubmitMomentumSliceAsync(
            new MomentumSliceRequest(datasetId, null, quantiles, rebalanceEvery),
            TestContext.Current.CancellationToken);

        Assert.False(submission.Queued);
        Assert.Empty(harness.Jobs.Added);
    }

    [Fact]
    public async Task A_named_experiment_is_created_with_its_hypothesis_and_carried_in_the_payload()
    {
        var harness = new Harness(versions: [1]);

        await harness.Service.SubmitMomentumSliceAsync(
            new MomentumSliceRequest(DatasetId, Experiment: "momentum-vn30", Hypothesis: "12-1 ranks VN30"),
            TestContext.Current.CancellationToken);

        var experiment = Assert.Single(harness.Experiments.Added);
        Assert.Equal("momentum-vn30", experiment.Name);
        Assert.Equal("12-1 ranks VN30", experiment.Hypothesis);
        Assert.Contains("\"experiment\":\"momentum-vn30\"", Assert.Single(harness.Jobs.Added).Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_existing_experiment_is_reused_rather_than_duplicated()
    {
        var harness = new Harness(versions: [1]);
        harness.Experiments.Hold(Experiment.Define("momentum-vn30", "12-1 ranks VN30", Now));

        var submission = await harness.Service.SubmitMomentumSliceAsync(
            new MomentumSliceRequest(DatasetId, Experiment: "momentum-vn30"), TestContext.Current.CancellationToken);

        Assert.True(submission.Queued);
        Assert.Empty(harness.Experiments.Added);
    }

    [Fact]
    public async Task A_different_hypothesis_for_an_existing_experiment_is_refused()
    {
        // The trial count would otherwise answer a question nobody asked.
        var harness = new Harness(versions: [1]);
        harness.Experiments.Hold(Experiment.Define("momentum-vn30", "12-1 ranks VN30", Now));

        var submission = await harness.Service.SubmitMomentumSliceAsync(
            new MomentumSliceRequest(DatasetId, Experiment: "momentum-vn30", Hypothesis: "something else"),
            TestContext.Current.CancellationToken);

        Assert.False(submission.Queued);
        Assert.Contains("new experiment", submission.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unusable_experiment_name_is_refused()
    {
        var harness = new Harness(versions: [1]);

        var submission = await harness.Service.SubmitMomentumSliceAsync(
            new MomentumSliceRequest(DatasetId, Experiment: "Momentum VN30"), TestContext.Current.CancellationToken);

        Assert.False(submission.Queued);
        Assert.Empty(harness.Jobs.Added);
    }

    [Fact]
    public async Task A_run_that_was_never_recorded_cannot_be_reproduced()
    {
        var harness = new Harness(versions: [1]);

        var submission = await harness.Service.SubmitReproductionAsync(
            Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.False(submission.Queued);
        Assert.Contains("No research run", submission.Refusal, StringComparison.Ordinal);
    }

    private sealed class Harness
    {
        public Harness(IReadOnlyList<int> versions)
        {
            Service = new ResearchJobService(
                Jobs,
                Experiments,
                new NoRuns(),
                new VersionsOnlyStore(versions),
                UnitOfWork,
                new FakeClock(Now));
        }

        public FakeExperimentRepository Experiments { get; } = new();

        public FakeJobRepository Jobs { get; } = new();

        public FakeUnitOfWork UnitOfWork { get; } = new();

        public ResearchJobService Service { get; }
    }

    private sealed class FakeJobRepository : IResearchJobRepository
    {
        public List<ResearchJob> Added { get; } = [];

        public void Add(ResearchJob job) => Added.Add(job);

        public Task<ResearchJob?> FindAsync(ResearchJobId id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Added.FirstOrDefault(job => job.Id == id));

        public Task<IReadOnlyList<ResearchJob>> ListRecentAsync(
            int limit,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ResearchJob>>(Added);
    }

    private sealed class FakeExperimentRepository : IExperimentRepository
    {
        private readonly List<Experiment> _held = [];

        public List<Experiment> Added { get; } = [];

        public void Hold(Experiment experiment) => _held.Add(experiment);

        public Task<Experiment?> FindByNameAsync(string name, CancellationToken cancellationToken = default) =>
            Task.FromResult(_held.Concat(Added).FirstOrDefault(experiment => experiment.Name == name));

        public void Add(Experiment experiment) => Added.Add(experiment);
    }

    private sealed class NoRuns : IResearchRunRepository
    {
        public Task<ResearchRunView?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<ResearchRunView?>(null);

        public Task<IReadOnlyList<ResearchRunView>> ListRecentAsync(
            string? experimentName,
            int limit,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ResearchRunView>>([]);
    }

    /// <summary>A store that knows which builds exist and nothing else.</summary>
    private sealed class VersionsOnlyStore(IReadOnlyList<int> versions) : IDatasetStore
    {
        public Task<IReadOnlyList<int>> ListVersionsAsync(
            string datasetId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(versions);

        public Task<DatasetFile> WriteBarsAsync(
            string datasetId,
            int version,
            IReadOnlyList<DatasetBar> bars,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task WriteManifestAsync(
            DatasetManifest manifest,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DatasetManifest?> ReadManifestAsync(
            string datasetId,
            int version,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string?> ComputeHashAsync(
            string datasetId,
            int version,
            string fileName,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
