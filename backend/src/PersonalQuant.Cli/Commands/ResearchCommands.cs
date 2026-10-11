using System.Globalization;
using PersonalQuant.Application.Research;
using PersonalQuant.Cli.CommandLine;
using PersonalQuant.Domain.Research;

namespace PersonalQuant.Cli.Commands;

/// <summary>
/// Queues research work for the quant worker and reports on it.
/// </summary>
/// <remarks>
/// The backend's half of ADR-016: it writes the request, the worker in its
/// own container claims and runs it, and the outcome is read back here. This
/// group never runs research itself — a failing experiment belongs in the
/// worker's process, not in the operator's terminal or the API's.
/// </remarks>
/// <param name="jobs">The job service, constructed once the arguments hold.</param>
/// <param name="output">Where results and refusals go.</param>
internal sealed class ResearchCommands(Lazy<IResearchJobService> jobs, Output output)
{
    private const int LabelWidth = 14;

    private const int DefaultListLimit = 20;

    /// <summary>Dispatches a <c>research</c> verb.</summary>
    /// <param name="command">The parsed command line.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The process exit code.</returns>
    public Task<int> RunAsync(CommandArguments command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return command.Verb switch
        {
            "submit" => SubmitAsync(command, cancellationToken),
            "list" => ListAsync(command, cancellationToken),
            "show" => ShowAsync(command, cancellationToken),
            "runs" => RunsAsync(command, cancellationToken),
            "run" => ShowRunAsync(command, cancellationToken),
            "reproduce" => ReproduceAsync(command, cancellationToken),
            _ => Task.FromResult(Unknown(command.Verb)),
        };
    }

    private async Task<int> SubmitAsync(CommandArguments command, CancellationToken cancellationToken)
    {
        if (!command.Validate(
                ["dataset", "version", "quantiles", "rebalance-every", "experiment", "hypothesis"],
                out var problem))
        {
            output.Problem(problem);
            return ExitCode.Usage;
        }

        if (command.Operands is not ["momentum-slice"])
        {
            output.Problem("pqt research submit momentum-slice --dataset <ID> [--version N] "
                + "[--quantiles 10] [--rebalance-every 21] [--experiment NAME] [--hypothesis TEXT]");
            return ExitCode.Usage;
        }

        if (!command.TryRequired("dataset", out var datasetId, out var missing))
        {
            output.Problem(missing);
            return ExitCode.Usage;
        }

        if (!command.TryCount("quantiles", 10, out var quantiles, out var badQuantiles)
            || !command.TryCount("rebalance-every", 21, out var every, out badQuantiles))
        {
            output.Problem(badQuantiles);
            return ExitCode.Usage;
        }

        int? version = null;

        if (command.Value("version") is not null)
        {
            if (!command.TryCount("version", 1, out var named, out var badVersion))
            {
                output.Problem(badVersion);
                return ExitCode.Usage;
            }

            version = named;
        }

        var submission = await jobs.Value
            .SubmitMomentumSliceAsync(
                new MomentumSliceRequest(
                    datasetId,
                    version,
                    quantiles,
                    every,
                    command.Value("experiment"),
                    command.Value("hypothesis")),
                cancellationToken)
            .ConfigureAwait(false);

        if (submission.Job is not { } job)
        {
            output.Problem(submission.Refusal ?? "The job was refused.");
            return ExitCode.Refused;
        }

        output.Line($"{job.Id} queued: {job.Kind} over {datasetId}"
            + (version is { } v ? $" v{v.ToString(CultureInfo.InvariantCulture)}" : " (latest build)")
            + ". The quant worker will claim it; follow it with 'pqt research show "
            + $"{job.Id}'.");

        return ExitCode.Ok;
    }

    private async Task<int> ListAsync(CommandArguments command, CancellationToken cancellationToken)
    {
        if (!command.Validate(["limit"], out var problem))
        {
            output.Problem(problem);
            return ExitCode.Usage;
        }

        if (!command.TryCount("limit", DefaultListLimit, out var limit, out var badLimit))
        {
            output.Problem(badLimit);
            return ExitCode.Usage;
        }

        var recent = await jobs.Value.ListRecentAsync(limit, cancellationToken).ConfigureAwait(false);

        if (recent.Count == 0)
        {
            output.Line("No research jobs have been submitted.");
            return ExitCode.Ok;
        }

        output.Table(
            ["id", "kind", "status", "attempts", "submitted", "finished"],
            [
                .. recent.Select(job => (IReadOnlyList<string>)
                [
                    job.Id.ToString(),
                    job.Kind,
                    Describe(job.Status),
                    job.Attempts.ToString(CultureInfo.InvariantCulture),
                    Instant(job.CreatedAtUtc),
                    job.FinishedAtUtc is { } finished ? Instant(finished) : Output.Unknown,
                ]),
            ]);

        return ExitCode.Ok;
    }

    private async Task<int> ShowAsync(CommandArguments command, CancellationToken cancellationToken)
    {
        if (!command.Validate([], out var problem))
        {
            output.Problem(problem);
            return ExitCode.Usage;
        }

        if (command.Operands.Count != 1 || !Guid.TryParse(command.Operands[0], out var id))
        {
            output.Problem("pqt research show <JOB-ID>");
            return ExitCode.Usage;
        }

        var job = await jobs.Value.FindAsync(new ResearchJobId(id), cancellationToken).ConfigureAwait(false);

        if (job is null)
        {
            output.Problem($"No research job {id} exists.");
            return ExitCode.Refused;
        }

        output.Field("job", job.Id.ToString(), LabelWidth);
        output.Field("kind", job.Kind, LabelWidth);
        output.Field("status", Describe(job.Status), LabelWidth);
        output.Field("payload", job.Payload, LabelWidth);
        output.Field("submitted", Instant(job.CreatedAtUtc), LabelWidth);
        output.Field("attempts", job.Attempts.ToString(CultureInfo.InvariantCulture), LabelWidth);
        output.Field("claimed by", job.ClaimedBy ?? Output.Unknown, LabelWidth);
        output.Field("lease until", job.LeaseUntilUtc is { } lease ? Instant(lease) : Output.Unknown, LabelWidth);
        output.Field("finished", job.FinishedAtUtc is { } finished ? Instant(finished) : Output.Unknown, LabelWidth);

        if (job.Error is { } error)
        {
            output.Field("error", error, LabelWidth);
        }

        if (job.Result is { } result)
        {
            output.Field("result", result, LabelWidth);
        }

        // A failed job is a refusal the operator should be able to script on.
        return job.Status == ResearchJobStatus.Failed ? ExitCode.Refused : ExitCode.Ok;
    }

    private async Task<int> RunsAsync(CommandArguments command, CancellationToken cancellationToken)
    {
        if (!command.Validate(["experiment", "limit"], out var problem))
        {
            output.Problem(problem);
            return ExitCode.Usage;
        }

        if (!command.TryCount("limit", DefaultListLimit, out var limit, out var badLimit))
        {
            output.Problem(badLimit);
            return ExitCode.Usage;
        }

        var recorded = await jobs.Value
            .ListRunsAsync(command.Value("experiment"), limit, cancellationToken)
            .ConfigureAwait(false);

        if (recorded.Count == 0)
        {
            output.Line("No research runs are recorded.");
            return ExitCode.Ok;
        }

        // The trial column is the one a reader must not skip: the IC beside it
        // is one of that many configurations tried.
        output.Table(
            ["run", "experiment", "trial", "dataset", "factor", "ic mean", "t", "finished"],
            [
                .. recorded.Select(view => (IReadOnlyList<string>)
                [
                    view.Run.Id.ToString(),
                    view.ExperimentName,
                    Trial(view.Run),
                    $"{view.Run.DatasetId} v{view.Run.DatasetVersion.ToString(CultureInfo.InvariantCulture)}",
                    view.Run.FactorVersion,
                    Metric(view.Run, "ic_mean"),
                    Metric(view.Run, "t_statistic"),
                    Instant(view.Run.FinishedAtUtc),
                ]),
            ]);

        return ExitCode.Ok;
    }

    private async Task<int> ShowRunAsync(CommandArguments command, CancellationToken cancellationToken)
    {
        if (!command.Validate([], out var problem))
        {
            output.Problem(problem);
            return ExitCode.Usage;
        }

        if (command.Operands.Count != 1 || !Guid.TryParse(command.Operands[0], out var id))
        {
            output.Problem("pqt research run <RUN-ID>");
            return ExitCode.Usage;
        }

        var view = await jobs.Value.FindRunAsync(id, cancellationToken).ConfigureAwait(false);

        if (view is null)
        {
            output.Problem($"No research run {id} is recorded.");
            return ExitCode.Refused;
        }

        var run = view.Run;
        output.Field("run", run.Id.ToString(), LabelWidth);
        output.Field("experiment", view.ExperimentName, LabelWidth);
        output.Field("trial", Trial(run), LabelWidth);
        output.Field("status", run.Status, LabelWidth);
        output.Field("strategy", run.Strategy, LabelWidth);
        output.Field("dataset", $"{run.DatasetId} v{run.DatasetVersion.ToString(CultureInfo.InvariantCulture)} "
            + $"(schema {run.SchemaVersion.ToString(CultureInfo.InvariantCulture)})", LabelWidth);
        output.Field("content hash", run.ContentHash, LabelWidth);
        output.Field("universe", $"{run.UniverseCode}, {run.Membership}", LabelWidth);
        output.Field("factor", run.FactorVersion, LabelWidth);
        output.Field("params", run.Parameters, LabelWidth);
        output.Field("code commit", run.CodeCommit ?? Output.Unknown, LabelWidth);
        output.Field("engine", $"{run.ResearchEngine} {run.ResearchEngineVersion}", LabelWidth);
        output.Field("finished", Instant(run.FinishedAtUtc), LabelWidth);

        if (run.ReproducesRunId is { } original)
        {
            output.Field("reproduces", original.ToString(), LabelWidth);
        }

        foreach (var metric in run.Metrics.OrderBy(metric => metric.Name, StringComparer.Ordinal))
        {
            output.Field(
                $"  {metric.Name}",
                metric.Value?.ToString("R", CultureInfo.InvariantCulture) ?? Output.Unknown,
                LabelWidth);
        }

        return run.Status == "succeeded" ? ExitCode.Ok : ExitCode.Refused;
    }

    private async Task<int> ReproduceAsync(CommandArguments command, CancellationToken cancellationToken)
    {
        if (!command.Validate([], out var problem))
        {
            output.Problem(problem);
            return ExitCode.Usage;
        }

        if (command.Operands.Count != 1 || !Guid.TryParse(command.Operands[0], out var id))
        {
            output.Problem("pqt research reproduce <RUN-ID>");
            return ExitCode.Usage;
        }

        var submission = await jobs.Value.SubmitReproductionAsync(id, cancellationToken).ConfigureAwait(false);

        if (submission.Job is not { } job)
        {
            output.Problem(submission.Refusal ?? "The reproduction was refused.");
            return ExitCode.Refused;
        }

        output.Line($"{job.Id} queued: reproduce run {id}. The worker re-executes it from its record "
            + $"and diffs the metrics; follow it with 'pqt research show {job.Id}'.");

        return ExitCode.Ok;
    }

    private int Unknown(string verb)
    {
        output.Problem(
            $"'research {verb}' is not a command. Try submit, list, show, runs, run or reproduce.");
        return ExitCode.Usage;
    }

    private static string Trial(ResearchRun run) =>
        run.TrialIndex is { } index && run.TrialCount is { } count
            ? $"{index.ToString(CultureInfo.InvariantCulture)} of {count.ToString(CultureInfo.InvariantCulture)}"
            : "reproduction";

    private static string Metric(ResearchRun run, string name) =>
        run.Metrics.FirstOrDefault(metric => metric.Name == name && metric.Split == "all")?.Value is { } value
            ? value.ToString("+0.000;-0.000", CultureInfo.InvariantCulture)
            : Output.Unknown;

    private static string Describe(ResearchJobStatus status) =>
        status.ToString().ToLowerInvariant();

    private static string Instant(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
