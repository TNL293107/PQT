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
