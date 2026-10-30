using PersonalQuant.Application.Datasets;
using PersonalQuant.Application.Research;
using PersonalQuant.Domain.Research;
using PersonalQuant.UnitTests.MarketData.Fakes;

namespace PersonalQuant.UnitTests.Research;

/// <summary>
/// Pins the transform versions the backend queues to exactly the worker's:
/// <c>raw</c> and <c>wz-1</c> (ADR-035). A version the worker does not know
/// would be queued and then fail at claim time; it is refused here instead.
/// </summary>
public sealed class FactorJobTransformTests
{
    private const string DatasetId = "fedcba9876543210";

    private static readonly DateTimeOffset Now = new(2026, 10, 30, 3, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("raw")]
    [InlineData("wz-1")]
    public async Task Each_transform_the_worker_knows_is_queued_for_both_kinds(string transform)
    {
        var (service, jobs) = Service();

        var evaluation = await service.SubmitFactorEvaluationAsync(
            new FactorEvaluationRequest(DatasetId, Feature: "size", TransformVersion: transform, DecayHorizons: [21]),
            TestContext.Current.CancellationToken);
        var correlation = await service.SubmitFactorCorrelationAsync(
            new FactorCorrelationRequest(DatasetId, TransformVersion: transform),
            TestContext.Current.CancellationToken);

        Assert.True(evaluation.Queued, evaluation.Refusal);
        Assert.True(correlation.Queued, correlation.Refusal);
        Assert.All(jobs.Added, job => Assert.Contains($"\"transform_version\":\"{transform}\"", job.Payload, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("wz-2")]
    [InlineData("wz-0")]
    [InlineData("WZ-1")]
    [InlineData("Raw")]
    [InlineData("")]
    public async Task A_transform_the_worker_does_not_know_is_refused_for_both_kinds(string transform)
    {
        var (service, jobs) = Service();

        var evaluation = await service.SubmitFactorEvaluationAsync(
            new FactorEvaluationRequest(DatasetId, Feature: "size", TransformVersion: transform, DecayHorizons: [21]),
            TestContext.Current.CancellationToken);
        var correlation = await service.SubmitFactorCorrelationAsync(
            new FactorCorrelationRequest(DatasetId, TransformVersion: transform),
            TestContext.Current.CancellationToken);

        Assert.False(evaluation.Queued);
        Assert.False(correlation.Queued);
        Assert.Contains("transform", evaluation.Refusal, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(jobs.Added);
    }

    [Fact]
    public void The_known_versions_are_exactly_the_worker_s()
    {
        // personal_quant.factors.transforms.TRANSFORMS has these two keys and no other.
        Assert.Equal(["raw", "wz-1"], ResearchJobService.TransformVersions);
    }

    private static (ResearchJobService Service, Jobs Jobs) Service()
    {
        var jobs = new Jobs();

        return (
            new ResearchJobService(jobs, new Experiments(), new NoRuns(), new OneBuild(), new FakeUnitOfWork(), new FakeClock(Now)),
            jobs);
    }

    private sealed class Jobs : IResearchJobRepository
    {
        public List<ResearchJob> Added { get; } = [];

        public void Add(ResearchJob job) => Added.Add(job);

        public Task<ResearchJob?> FindAsync(ResearchJobId id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Added.FirstOrDefault(job => job.Id == id));

        public Task<IReadOnlyList<ResearchJob>> ListRecentAsync(int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ResearchJob>>(Added);
    }

    private sealed class Experiments : IExperimentRepository
    {
        private readonly List<Experiment> _added = [];

        public Task<Experiment?> FindByNameAsync(string name, CancellationToken cancellationToken = default) =>
            Task.FromResult(_added.FirstOrDefault(experiment => experiment.Name == name));

        public void Add(Experiment experiment) => _added.Add(experiment);
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

    /// <summary>A store holding build v1 of every dataset and nothing else.</summary>
    private sealed class OneBuild : IDatasetStore
    {
        public Task<IReadOnlyList<int>> ListVersionsAsync(string datasetId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<int>>([1]);

        public Task<DatasetFile> WriteBarsAsync(
            string datasetId,
            int version,
            IReadOnlyList<DatasetBar> bars,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DatasetFile> WriteFundamentalsAsync(
            string datasetId,
            int version,
            IReadOnlyList<DatasetFundamentalsRow> rows,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task WriteManifestAsync(DatasetManifest manifest, CancellationToken cancellationToken = default) =>
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
