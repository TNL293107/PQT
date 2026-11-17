/**
 * Types mirroring `GET /research/runs`: recorded research runs, newest first,
 * each with its experiment's trial count (sprint 11.2).
 *
 * The runs are the worker's; the console only reads them. A metric is one of
 * `trialCount` configurations tried, so the view prints the two together.
 */

/** One metric of a run. `value` is null when the run could not measure it. */
export interface ResearchMetric {
  /** `rank_ic_mean`, `rank_ic_periods`, `spread_mean`, `turnover_mean`, ... */
  readonly name: string;
  readonly split: string;
  readonly value: number | null;
}

/** One recorded run. */
export interface ResearchRun {
  readonly runId: string;
  readonly experiment: string;
  readonly strategy: string;
  readonly status: string;
  /** Its place among the experiment's trials; null for a reproduction. */
  readonly trialIndex: number | null;
  /** How many trials the experiment has made now; null when it has none. */
  readonly trialCount: number | null;
  readonly reproducesRunId: string | null;
  readonly datasetId: string;
  readonly datasetVersion: number;
  /** What was computed, `name@version`. */
  readonly factorVersion: string;
  /** The run's parameters, as the JSON text the worker recorded. */
  readonly parameters: string;
  readonly finishedAtUtc: string;
  readonly metrics: readonly ResearchMetric[];
}

/** The runs, and the report's sentence that every result here is exploratory. */
export interface ResearchRuns {
  readonly runs: readonly ResearchRun[];
  readonly note: string;
}
