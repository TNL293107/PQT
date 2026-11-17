using PersonalQuant.Application.Research;

namespace PersonalQuant.Api.Contracts;

/// <summary>Recorded research runs, newest first, each beside how many trials its experiment has made.</summary>
/// <param name="Runs">The runs.</param>
/// <param name="Note">The sentence every Phase 7 report carries: results on this sample are exploratory.</param>
public sealed record ResearchRunsResponse(IReadOnlyList<ResearchRunResponse> Runs, string Note)
{
    /// <summary>The report's own words (ADR-033 §5).</summary>
    public const string Exploratory =
        "Each result is one of the trials its experiment counts, on at most about 26 monthly "
            + "cross-sections of at most 30 names: exploratory, not a finding.";

    /// <summary>Projects the runs with their experiments' trial counts.</summary>
    /// <param name="runs">The runs.</param>
    /// <param name="trialCounts">Each experiment's trial count.</param>
    /// <returns>The response.</returns>
    public static ResearchRunsResponse From(
        IReadOnlyList<ResearchRunView> runs,
        IReadOnlyDictionary<string, int> trialCounts) =>
        new(
            [.. runs.Select(view => ResearchRunResponse.From(view, trialCounts.GetValueOrDefault(view.ExperimentName)))],
            Exploratory);
}

/// <summary>One recorded run: the CLI's columns, every metric, and the experiment's trial count.</summary>
/// <param name="RunId">The run.</param>
/// <param name="Experiment">The experiment it is counted under.</param>
/// <param name="Strategy">The job kind that made it.</param>
/// <param name="Status"><c>succeeded</c> or <c>failed</c>.</param>
/// <param name="TrialIndex">Its place among the experiment's trials, or null for a reproduction.</param>
/// <param name="TrialCount">How many trials the experiment has made now, or null when it has none.</param>
/// <param name="ReproducesRunId">The run it re-executed, for a reproduction.</param>
/// <param name="DatasetId">The dataset read.</param>
/// <param name="DatasetVersion">The build read.</param>
/// <param name="FactorVersion">What was computed, as <c>name@version</c>.</param>
/// <param name="Parameters">The run's parameters, as the JSON the worker recorded.</param>
/// <param name="FinishedAtUtc">When it finished.</param>
/// <param name="Metrics">Every metric, in the order recorded.</param>
public sealed record ResearchRunResponse(
    Guid RunId,
    string Experiment,
    string Strategy,
    string Status,
    int? TrialIndex,
    int? TrialCount,
    Guid? ReproducesRunId,
    string DatasetId,
    int DatasetVersion,
    string FactorVersion,
    string Parameters,
    DateTimeOffset FinishedAtUtc,
    IReadOnlyList<ResearchMetricResponse> Metrics)
{
    /// <summary>Projects a run.</summary>
    /// <param name="view">The run and its experiment's name.</param>
    /// <param name="trialCount">The experiment's trial count, or 0 when it has none.</param>
    /// <returns>The response.</returns>
    public static ResearchRunResponse From(ResearchRunView view, int trialCount)
    {
        ArgumentNullException.ThrowIfNull(view);
        var run = view.Run;

        return new(
            run.Id,
            view.ExperimentName,
            run.Strategy,
            run.Status,
            run.TrialIndex,
            trialCount == 0 ? null : trialCount,
            run.ReproducesRunId,
            run.DatasetId,
            run.DatasetVersion,
            run.FactorVersion,
            run.Parameters,
            run.FinishedAtUtc,
            [.. run.Metrics.Select(metric => new ResearchMetricResponse(metric.Name, metric.Split, metric.Value))]);
    }
}

/// <summary>One metric of a run. A <c>_periods</c> metric beside it carries its observation count.</summary>
/// <param name="Name">The metric's name, such as <c>rank_ic_mean</c> or <c>rank_ic_periods</c>.</param>
/// <param name="Split">The sample it was measured on, <c>all</c> today.</param>
/// <param name="Value">The value, or null when the run could not measure it.</param>
public sealed record ResearchMetricResponse(string Name, string Split, double? Value);
