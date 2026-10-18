import { render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import type { Instrument } from "../../types/instrument";
import type { CoverageState, InstrumentCoverage, PriceCoverage } from "../../types/coverage";
import { StateMark } from "./CoverageView";
import { RecordView } from "./RecordView";

const FPT: Instrument = {
  instrumentId: "11111111-1111-1111-1111-111111111111",
  ticker: "FPT",
  name: "FPT Corporation",
  assetType: "Equity",
  exchange: "HOSE",
  currency: "VND",
  status: "Listed",
};

const DAILY: PriceCoverage = {
  interval: "OneDay",
  intervalCode: "1d",
  state: "Held",
  firstSession: "2015-01-02",
  lastSession: "2026-08-28",
  bars: 2900,
  sources: ["VNX"],
  openFindings: 1,
  lastAttempt: {
    source: "VNX",
    startedAtUtc: "2026-08-29T01:00:00+00:00",
    completedAtUtc: "2026-08-29T01:01:00+00:00",
    outcome: "Succeeded",
  },
  sessionsExpected: 2901,
  missingSessions: 1,
  note: null,
};

/** One dataset in each of the four states, plus an unmeasured session count. */
const COVERAGE: InstrumentCoverage = {
  instrumentId: FPT.instrumentId,
  ticker: "FPT",
  name: "FPT Corporation",
  exchange: "HOSE",
  prices: [
    DAILY,
    {
      ...DAILY,
      interval: "OneHour",
      intervalCode: "1h",
      bars: 30,
      openFindings: 0,
      lastAttempt: null,
      sessionsExpected: null,
      missingSessions: null,
      note: "Missing sessions are measured for the daily series only.",
    },
    {
      ...DAILY,
      interval: "FiveMinutes",
      intervalCode: "5m",
      state: "NotCollected",
      firstSession: null,
      lastSession: null,
      bars: 0,
      sources: [],
      openFindings: 0,
      lastAttempt: null,
      sessionsExpected: null,
      missingSessions: null,
      note: "No registered source serves 5m bars for HOSE Equity.",
    },
  ],
  corporateActions: {
    state: "NotHeld",
    count: 0,
    cancelled: 0,
    withoutAnnouncementDate: 0,
    firstExDate: null,
    lastExDate: null,
    note: "No corporate action is recorded for this instrument.",
  },
  universes: [
    {
      code: "VN30",
      name: "VN30 Index",
      state: "Held",
      declaredFrom: "2024-01-01",
      declaredUntil: null,
      spells: [{ from: "2024-01-02", to: null, announcedOn: null, removalAnnouncedOn: null }],
      note: "VN30 claims to know its membership from 2024-01-01 onward.",
    },
    {
      code: "VNMID",
      name: "VNMID Index",
      state: "NotMeasured",
      declaredFrom: null,
      declaredUntil: null,
      spells: [],
      note: "VNMID declares no coverage, so the absence of a spell says nothing about membership.",
    },
  ],
  fundamentals: {
    state: "NotCollected",
    reports: 0,
    withoutValues: 0,
    firstPeriod: null,
    lastPeriod: null,
    lastDisclosedOn: null,
    sources: [],
    note: "IDS fails on every filing page (ADR-029, known gap)",
  },
};

function renderCoverage(coverage: InstrumentCoverage = COVERAGE) {
  return render(<RecordView record={{ kind: "coverage", instrument: FPT, coverage }} />);
}

/** The table row whose first cell names the dataset. */
function row(dataset: string): HTMLElement {
  const cell = screen.getByText(dataset, { selector: "td" });
  const tableRow = cell.closest("tr");

  if (!tableRow) {
    throw new Error(`No row for ${dataset}.`);
  }

  return tableRow;
}

describe("CoverageView", () => {
  it("draws each of the four states with its own mark and word", () => {
    renderCoverage();

    expect(within(row("Price 1d")).getByText("held")).toBeInTheDocument();
    expect(within(row("Corporate actions")).getByText("not held")).toBeInTheDocument();
    expect(within(row("Universe VNMID")).getByText("not measured")).toBeInTheDocument();
    expect(within(row("Fundamentals")).getByText("not collected")).toBeInTheDocument();

    expect(within(row("Price 1d")).getByText("✓")).toBeInTheDocument();
    expect(within(row("Corporate actions")).getByText("✗")).toBeInTheDocument();
    expect(within(row("Universe VNMID")).getByText("?")).toBeInTheDocument();
    expect(within(row("Fundamentals")).getByText("—")).toBeInTheDocument();
  });

  it("never draws a universe that declared nothing as not held", () => {
    renderCoverage();

    const unknown = row("Universe VNMID");

    expect(unknown).toHaveAttribute("data-state", "NotMeasured");
    expect(within(unknown).queryByText("not held")).not.toBeInTheDocument();
    expect(within(unknown).queryByText("✗")).not.toBeInTheDocument();
    expect(unknown).toHaveTextContent("says nothing about membership");
  });

  it("keeps the state readable without colour: the mark is decoration, the word is announced", () => {
    renderCoverage();

    const mark = within(row("Universe VNMID")).getByText("?");

    expect(mark).toHaveAttribute("aria-hidden", "true");
    expect(mark.parentElement).toHaveTextContent("?not measured");
  });

  it("states the extent and the facts of a held series", () => {
    renderCoverage();

    const daily = row("Price 1d");

    expect(daily).toHaveTextContent("2015-01-02 → 2026-08-28");
    expect(daily).toHaveTextContent("2,900 bars");
    expect(daily).toHaveTextContent("1 of 2,901 sessions missing");
    expect(daily).toHaveTextContent("1 open finding");
    expect(daily).toHaveTextContent("last attempt 2026-08-29 VNX Succeeded");
  });

  it("says a session count was not measured rather than printing zero", () => {
    renderCoverage();

    const hourly = row("Price 1h");

    expect(hourly).not.toHaveTextContent("0 of");
    expect(hourly).toHaveTextContent("daily series only");
  });

  it("draws a state this build does not know as its own name rather than failing", () => {
    // A fifth state added on the server must not take the whole view down.
    render(<StateMark state={"Estimated" as CoverageState} />);

    expect(screen.getByText("Estimated")).toBeInTheDocument();
  });

  it("explains a dataset PQT does not collect", () => {
    renderCoverage();

    expect(row("Price 5m")).toHaveTextContent("No registered source serves 5m bars");
    expect(row("Fundamentals")).toHaveTextContent("known gap");
  });

  it("counts the fundamentals reports held and says how many came without values", () => {
    renderCoverage({
      ...COVERAGE,
      fundamentals: {
        state: "Held",
        reports: 24,
        withoutValues: 2,
        firstPeriod: "2021Q4",
        lastPeriod: "2026Q2",
        lastDisclosedOn: "2026-08-21",
        sources: ["IDS"],
        note: "2 disclosed without values",
      },
    });

    expect(row("Fundamentals")).toHaveTextContent("2021Q4 → 2026Q2");
    expect(row("Fundamentals")).toHaveTextContent("24 reports · last disclosed 2026-08-21 · IDS");
    expect(row("Fundamentals")).toHaveTextContent("2 disclosed without values");
  });

  it("shows a membership spell that is still running as running to now", () => {
    renderCoverage();

    expect(row("Universe VN30")).toHaveTextContent("2024-01-02 → now");
  });

  it("says so when no universe is defined, rather than drawing nothing", () => {
    renderCoverage({ ...COVERAGE, universes: [] });

    expect(screen.getByText(/No universe is defined/)).toBeInTheDocument();
  });
});
