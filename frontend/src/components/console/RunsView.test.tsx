import { render, screen, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { ResearchRun, ResearchRuns } from "../../types/research";
import { parseEntry } from "../../lib/console/entry";
import { runEntry, type ConsoleServices } from "../../lib/console/run";
import { RunsView } from "./RunsView";

function run(overrides: Partial<ResearchRun>): ResearchRun {
  return {
    runId: "ae6813d6-1629-4e41-acc2-1f52ccff66c7",
    experiment: "phase-7-vn30",
    strategy: "factor_evaluation",
    status: "succeeded",
    trialIndex: 1,
    trialCount: 14,
    reproducesRunId: null,
    datasetId: "0e7d28336af399e1",
    datasetVersion: 1,
    factorVersion: "momentum_12_1@1",
    parameters: "{}",
    finishedAtUtc: "2026-10-28T07:10:00+00:00",
    metrics: [
      { name: "rank_ic_mean", split: "all", value: 0.0784 },
      { name: "rank_ic_periods", split: "all", value: 24 },
      { name: "rank_ic_t_statistic", split: "all", value: 1.2977 },
      { name: "spread_mean", split: "all", value: null },
    ],
    ...overrides,
  };
}

// Newest first, as the server sends them: size has the larger IC but comes
// second, because it finished earlier.
const RUNS: ResearchRuns = {
  note: "Each result is one of the trials its experiment counts: exploratory, not a finding.",
  runs: [
    run({ runId: "r-newer", factorVersion: "momentum_12_1@1", trialIndex: 2 }),
    run({
      runId: "r-older",
      factorVersion: "size@1",
      trialIndex: 1,
      metrics: [{ name: "rank_ic_mean", split: "all", value: 0.1083 }],
    }),
    run({ runId: "r-repro", factorVersion: "momentum_12_1@1", trialIndex: null, trialCount: 14, reproducesRunId: "ae6813d6-1629-4e41-acc2-1f52ccff66c7" }),
  ],
};

function rows(): HTMLElement[] {
  return screen.getAllByRole("row").slice(1);
}

describe("RunsView", () => {
  it("prints each run's trial and its experiment's trial count beside the metrics", () => {
    render(<RunsView runs={RUNS} />);

    expect(within(rows()[0] as HTMLElement).getByText("trial 2 of 14")).toBeInTheDocument();
    expect(within(rows()[1] as HTMLElement).getByText("trial 1 of 14")).toBeInTheDocument();
    expect(within(rows()[0] as HTMLElement).getByText("0.0784 (n 24)")).toBeInTheDocument();
  });

  it("keeps the server's order and never ranks by a metric", () => {
    render(<RunsView runs={RUNS} />);

    // size@1 has the higher rank IC (0.1083) and still comes after momentum.
    expect(rows().map((row) => within(row).getAllByRole("cell")[2]?.textContent)).toEqual([
      "momentum_12_1@1",
      "size@1",
      "momentum_12_1@1",
    ]);
  });

  it("prints an unmeasured metric as a dash, never zero, and a reproduction as one", () => {
    render(<RunsView runs={RUNS} />);

    const newest = rows()[0] as HTMLElement;
    expect(within(newest).getAllByText("—").length).toBeGreaterThan(0);
    expect(newest).not.toHaveTextContent("0.0000");
    expect(within(rows()[2] as HTMLElement).getByText("reproduces ae6813d6")).toBeInTheDocument();
  });

  it("states that every result is exploratory", () => {
    render(<RunsView runs={RUNS} />);

    expect(screen.getByText(/exploratory/)).toBeInTheDocument();
  });

  it("says so when no run is recorded", () => {
    render(<RunsView runs={{ runs: [], note: "x" }} />);

    expect(screen.getByText("No research runs are recorded.")).toBeInTheDocument();
  });
});

describe("runEntry — RUNS", () => {
  const services = (runs = vi.fn(async () => RUNS)) => ({ runs }) as unknown as ConsoleServices;

  it("reads RUNS as a bare verb", () => {
    expect(parseEntry("runs --experiment phase-7-vn30").verb).toBe("RUNS");
  });

  it("passes the experiment and the limit to the API", async () => {
    const runs = vi.fn(async () => RUNS);

    const result = await runEntry("runs --experiment phase-7-vn30 --limit 5", services(runs));

    expect(runs).toHaveBeenCalledWith("phase-7-vn30", 5, undefined);
    expect(result.record.kind).toBe("runs");
  });

  it("refuses a limit that is not a positive whole number without reaching the API", async () => {
    const runs = vi.fn(async () => RUNS);

    const result = await runEntry("runs --limit 0", services(runs));

    expect(result.record.kind).toBe("error");
    expect(runs).not.toHaveBeenCalled();
  });
});
