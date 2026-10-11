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
            _ => Task.FromResult(Unknown(command.Verb)),
        };
    }

    private async Task<int> SubmitAsync(CommandArguments command, CancellationToken cancellationToken)
    {
        if (!command.Validate(["dataset", "version", "quantiles", "rebalance-every"], out var problem))
        {
            output.Problem(problem);
            return ExitCode.Usage;
        }

        if (command.Operands is not ["momentum-slice"])
        {
            output.Problem("pqt research submit momentum-slice --dataset <ID> [--version N] "
                + "[--quantiles 10] [--rebalance-every 21]");
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
                new MomentumSliceRequest(datasetId, version, quantiles, every),
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

    private int Unknown(string verb)
    {
        output.Problem($"'research {verb}' is not a command. Try submit, list or show.");
        return ExitCode.Usage;
    }

    private static string Describe(ResearchJobStatus status) =>
        status.ToString().ToLowerInvariant();

    private static string Instant(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
