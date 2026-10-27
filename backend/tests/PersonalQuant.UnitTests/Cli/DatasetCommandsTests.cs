using PersonalQuant.Application.Datasets;
using PersonalQuant.Cli.CommandLine;
using PersonalQuant.Cli.Commands;
using PersonalQuant.UnitTests.Cli.Fakes;

namespace PersonalQuant.UnitTests.Cli;

/// <summary>
/// Verifies how the operator asks for history before each name's admission.
/// </summary>
public sealed class DatasetCommandsTests
{
    [Fact]
    public async Task Without_history_the_request_carries_none()
    {
        var harness = new Harness();

        var code = await harness.RunAsync("dataset", "export", "--universe", "VN30", "--from", "2026-07-01", "--to", "2026-09-17");

        Assert.Equal(ExitCode.Refused, code);
        Assert.Equal(0, harness.Datasets.LastRequest!.HistorySessions);
    }

    [Theory]
    [InlineData("253", 253)]
    [InlineData("0", 0)]
    public async Task History_reaches_the_service_as_typed(string typed, int expected)
    {
        var harness = new Harness();

        await harness.RunAsync(
            "dataset", "export", "--universe", "VN30", "--from", "2026-07-01", "--to", "2026-09-17", "--history", typed);

        Assert.Equal(expected, harness.Datasets.LastRequest!.HistorySessions);
        Assert.Equal(DatasetMembership.PointInTime, harness.Datasets.LastRequest.Membership);
    }

    [Fact]
    public async Task History_applies_to_an_as_of_export_too()
    {
        var harness = new Harness();

        await harness.RunAsync(
            "dataset", "export", "--universe", "VN30", "--from", "2026-07-01", "--to", "2026-09-17",
            "--as-of", "2026-07-01", "--history", "21");

        Assert.Equal(21, harness.Datasets.LastRequest!.HistorySessions);
        Assert.Equal(DatasetMembership.AsOf, harness.Datasets.LastRequest.Membership);
    }

    [Theory]
    [InlineData("ten")]
    [InlineData("-5")]
    [InlineData("1001")]
    public async Task A_history_the_operator_got_wrong_never_reaches_the_deployment(string typed)
    {
        var harness = Harness.WithNothingConstructible();

        var code = await harness.RunAsync(
            "dataset", "export", "--universe", "VN30", "--from", "2026-07-01", "--history", typed);

        Assert.Equal(ExitCode.Usage, code);
        Assert.Contains("history", harness.Problems, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_request_file_resolves_to_a_point_in_time_export_with_the_history_its_lookbacks_need()
    {
        var harness = new Harness();
        var file = Request("null", 21, "adjusted");

        await harness.RunAsync("dataset", "export", "--request", file);

        var request = harness.Datasets.LastRequest!;

        Assert.Equal(DatasetMembership.PointInTime, request.Membership);
        Assert.Equal(252, request.HistorySessions);
        Assert.True(request.Fundamentals);
        Assert.True(request.Adjusted);
        Assert.Equal(new DateOnly(2026, 7, 1), request.FromDate);
        Assert.Equal(["size", "momentum_12_1"], request.Labelled!.Features.Select(feature => feature.Name));
    }

    [Theory]
    [InlineData("\"2026-07-01\"", 21, "adjusted", "as_of")]
    [InlineData("null", 5, "adjusted", "horizon")]
    [InlineData("null", 21, "raw", "basis")]
    public async Task A_request_version_1_does_not_support_is_refused_before_anything_is_written(
        string asOf, int horizon, string basis, string named)
    {
        var harness = Harness.WithNothingConstructible();

        var code = await harness.RunAsync("dataset", "export", "--request", Request(asOf, horizon, basis));

        Assert.Equal(ExitCode.Usage, code);
        Assert.Contains(named, harness.Problems, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_request_with_another_option_beside_it_is_refused()
    {
        var harness = Harness.WithNothingConstructible();

        var code = await harness.RunAsync("dataset", "export", "--request", Request("null", 21, "adjusted"), "--raw");

        Assert.Equal(ExitCode.Usage, code);
        Assert.Contains("--raw", harness.Problems, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_request_names_the_same_dataset_and_its_features_name_a_different_one()
    {
        LabelledDatasetRequest labelled = new(
            "VN30",
            null,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 9, 17),
            "1d",
            true,
            21,
            [new LabelledDatasetFeature("size", 1, 1), new LabelledDatasetFeature("momentum_12_1", 1, 253)],
            new LabelledDatasetLabel(21, "adjusted"));

        Assert.True(DatasetRequest.TryResolve(labelled, out var first, out var problem), problem);
        Assert.True(DatasetRequest.TryResolve(labelled with { Features = [.. labelled.Features.Reverse()] }, out var reordered, out _));
        Assert.True(DatasetRequest.TryResolve(labelled with { Features = [labelled.Features[1]] }, out var fewer, out _));
        Assert.True(DatasetRequest.TryCreatePointInTime(
            first.UniverseCode, first.Interval, first.FromDate, first.ToDate, out var plain, out _, historySessions: 252, fundamentals: true));

        Assert.Equal(first.DatasetId, reordered.DatasetId);
        Assert.NotEqual(first.DatasetId, fewer.DatasetId);
        Assert.NotEqual(first.DatasetId, plain.DatasetId);
    }

    private static string Request(string asOf, int horizon, string basis)
    {
        var path = Path.Combine(Path.GetTempPath(), $"pqt-request-{Guid.NewGuid():N}.json");

        File.WriteAllText(
            path,
            $$"""
            {
              "universe": "VN30",
              "as_of": {{asOf}},
              "from": "2026-07-01",
              "to": "2026-09-17",
              "interval": "1d",
              "fundamentals": true,
              "rebalance_every": 21,
              "features": [
                {"name": "size", "version": 1, "lookback": 1},
                {"name": "momentum_12_1", "version": 1, "lookback": 253}
              ],
              "label": {"horizon": {{horizon}}, "basis": "{{basis}}"}
            }
            """);

        return path;
    }

    private sealed class Harness
    {
        private readonly DatasetCommands _commands;
        private readonly RecordedOutput _output = new();

        public Harness()
            : this(constructible: true)
        {
        }

        private Harness(bool constructible)
        {
            _commands = constructible
                ? new DatasetCommands(new Lazy<IDatasetExportService>(() => Datasets), _output.Output)
                : new DatasetCommands(Unreachable.Service<IDatasetExportService>(), _output.Output);
        }

        public FakeDatasetExportService Datasets { get; } = new();

        public string Problems => _output.Problems;

        public static Harness WithNothingConstructible() => new(constructible: false);

        public async Task<int> RunAsync(params string[] args)
        {
            Assert.True(CommandArguments.TryParse(args, out var command, out var problem), problem);

            return await _commands.RunAsync(command, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>Records the request and refuses it, which is all the parsing needs.</summary>
    private sealed class FakeDatasetExportService : IDatasetExportService
    {
        public DatasetRequest? LastRequest { get; private set; }

        public Task<DatasetExport> ExportAsync(
            DatasetRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;

            return Task.FromResult(DatasetExport.Refused("Recorded, not exported."));
        }

        public Task<DatasetVerification> VerifyAsync(
            string datasetId,
            int version,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(DatasetVerification.NotFound(datasetId, version));
    }
}
