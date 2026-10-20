import { render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import type { FundamentalRatios } from "../../types/fundamentals";
import { FundamentalsView } from "./FundamentalsView";

const RATIOS: FundamentalRatios = {
  instrumentId: "0199a0a0-0000-7000-8000-000000000001",
  session: "2023-08-01",
  knownAsOfUtc: "2023-08-01T00:00:00Z",
  scope: "Consolidated",
  balanceSheet: "2023Q2",
  profitQuarters: ["2022Q3", "2022Q4", "2023Q1", "2023Q2"],
  actionsApplied: 1,
  actionsWithheld: 0,
  ratios: [
    { name: "close", value: 84500, absent: null, reason: null },
    { name: "epsTtm", value: 4573.37, absent: null, reason: null },
    { name: "priceToEarnings", value: 18.4765, absent: null, reason: null },
    { name: "returnOnEquity", value: 0.2658, absent: null, reason: null },
    { name: "dividendYield", value: null, absent: "no close on the session", reason: "NoClose" },
  ],
  findings: [],
};

function row(label: string): HTMLElement {
  return screen.getByText(label, { selector: "td" }).closest("tr") as HTMLElement;
}

describe("FundamentalsView", () => {
  it("prints each ratio in its own unit", () => {
    render(<FundamentalsView ratios={RATIOS} />);

    expect(within(row("Close")).getByText("84,500")).toBeInTheDocument();
    expect(within(row("EPS (TTM)")).getByText("4,573")).toBeInTheDocument();
    expect(within(row("P/E")).getByText("18.48")).toBeInTheDocument();
    expect(within(row("ROE (TTM)")).getByText("26.58%")).toBeInTheDocument();
  });

  it("prints an absent ratio as its reason and never as zero", () => {
    render(<FundamentalsView ratios={RATIOS} />);

    expect(row("Dividend yield (TTM)")).toHaveTextContent("—");
    expect(row("Dividend yield (TTM)")).toHaveTextContent("no close on the session");
    expect(row("Dividend yield (TTM)")).not.toHaveTextContent("0.00%");
  });

  it("says what the ratios were built from", () => {
    render(<FundamentalsView ratios={RATIOS} />);

    expect(screen.getByText(/Balance sheet 2023Q2/)).toHaveTextContent("2022Q3 2022Q4 2023Q1 2023Q2");
    expect(screen.getByText(/Balance sheet 2023Q2/)).toHaveTextContent("1 applied, 0 withheld");
  });

  it("prints what the share-count checks found", () => {
    const finding = "The 1.15x action of 2023-07-05 is not yet in 2023Q2's capital (x1 since 2023Q1); it is applied as registered late.";
    render(<FundamentalsView ratios={{ ...RATIOS, findings: [finding] }} />);

    expect(screen.getByText(finding)).toBeInTheDocument();
  });
});
