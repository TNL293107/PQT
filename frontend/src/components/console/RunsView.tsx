import type { ResearchRun, ResearchRuns } from "../../types/research";

/**
 * The metrics shown per run, in this order, each beside its observation count.
 *
 * The order is fixed: a table sorted by IC or t would put the luckiest trial
 * first and read as a ranking of findings (ADR-033 §5). Runs stay newest first,
 * as the server sends them.
 */
const COLUMNS: ReadonlyArray<{ readonly label: string; readonly metric: string; readonly count: string }> = [
  { label: "RANK IC", metric: "rank_ic_mean", count: "rank_ic_periods" },
  { label: "IC", metric: "ic_mean", count: "ic_periods" },
  { label: "T", metric: "rank_ic_t_statistic", count: "rank_ic_periods" },
  { label: "SPREAD", metric: "spread_mean", count: "spread_periods" },
  { label: "TURNOVER", metric: "turnover_mean", count: "turnover_periods" },
];

/**
 * Renders recorded research runs (sprint 11.2).
 *
 * Every row carries `trial i of n` beside its metrics: each number is one of
 * the configurations its experiment tried, and reading it without that count
 * overstates it. A metric a run could not measure shows a dash, never 0.
 */
export function RunsView({ runs }: { readonly runs: ResearchRuns }) {
  if (runs.runs.length === 0) {
    return (
      <div className="out">
        <p className="out__line">No research runs are recorded.</p>
      </div>
    );
  }

  return (
    <div className="out">
      <p className="out__line out__line--dim">{runs.note}</p>

      <table className="grid">
        <thead>
          <tr>
            <th>EXPERIMENT</th>
            <th>TRIAL</th>
            <th>FACTOR</th>
            <th>DATASET</th>
            {COLUMNS.map((column) => (
              <th key={column.label}>{column.label}</th>
            ))}
            <th>FINISHED</th>
          </tr>
        </thead>
        <tbody>
          {runs.runs.map((run) => (
            <tr key={run.runId} data-status={run.status}>
              <td className="grid__key">{run.experiment}</td>
              <td className="grid__note">{trial(run)}</td>
              <td>{run.factorVersion}</td>
              <td className="grid__note">
                {run.datasetId} v{run.datasetVersion}
              </td>
              {COLUMNS.map((column) => (
                <td key={column.label} className="grid__num">
                  {measured(run, column.metric, column.count)}
                </td>
              ))}
              <td className="grid__note">{run.finishedAtUtc.slice(0, 16).replace("T", " ")}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function trial(run: ResearchRun): string {
  if (run.trialIndex === null) {
    return run.reproducesRunId ? `reproduces ${run.reproducesRunId.slice(0, 8)}` : "no trial";
  }

  return run.trialCount === null ? `trial ${run.trialIndex}` : `trial ${run.trialIndex} of ${run.trialCount}`;
}

function measured(run: ResearchRun, name: string, countName: string): string {
  const value = run.metrics.find((metric) => metric.name === name)?.value ?? null;

  if (value === null) {
    return "—";
  }

  const count = run.metrics.find((metric) => metric.name === countName)?.value ?? null;
  const shown = value.toFixed(4);

  return count === null ? shown : `${shown} (n ${count})`;
}
