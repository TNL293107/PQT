import { render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import type { Quote, Watchlist, WatchlistQuotes } from "../../types/watchlist";
import { RecordView } from "./RecordView";

const AT = "2026-10-11T09:00:00Z";

const core: Watchlist = {
  name: "core",
  createdAtUtc: AT,
  updatedAtUtc: AT,
  count: 2,
  items: [
    { instrumentId: "vcb", ticker: "VCB", name: "Vietcombank", exchangeCode: "HOSE", status: "Listed", position: 0, addedAtUtc: AT },
    { instrumentId: "fpt", ticker: "FPT", name: "FPT Corporation", exchangeCode: "HOSE", status: "Listed", position: 1, addedAtUtc: AT },
  ],
};

function quote(instrumentId: string, ticker: string, fields: Partial<Quote>): Quote {
  return {
    instrumentId,
    ticker,
    venue: "HOSE",
    previousSession: "2026-09-29",
    rawPreviousClose: 60_000,
    reference: 60_000,
    close: 61_200,
    change: 0.02,
    volume: 2_000,
    move: "Advancing",
    adjustedForAction: false,
    ...fields,
  };
}

const quoted: WatchlistQuotes = {
  name: "core",
  session: "2026-09-30",
  sessionWasDefaulted: true,
  knownAsOf: null,
  announcementPolicy: "permissive",
  venues: [{ code: "HOSE", previousSession: "2026-09-29" }],
  quotes: [
    quote("fpt", "FPT", {
      rawPreviousClose: 47_500,
      reference: 40_450,
      close: 40_450,
      change: 0,
      move: "Unchanged",
      adjustedForAction: true,
    }),
    quote("vcb", "VCB", {}),
  ],
};

describe("WatchlistView", () => {
  it("shows the server's quotes in list order, with the direction written", () => {
    render(<RecordView record={{ kind: "watchlist", watchlist: core, quotes: quoted }} />);

    const rows = within(screen.getByRole("table")).getAllByRole("row").slice(1);

    expect(rows.map((row) => within(row).getAllByRole("cell")[0]?.textContent)).toEqual(["VCB", "FPT"]);
    expect(within(rows[0]!).getByText("▲ +2.00%")).toBeInTheDocument();
    expect(within(rows[1]!).getByText("= 0.00%")).toBeInTheDocument();
    expect(screen.getByText(/the latest session any of them has a bar for/u)).toBeInTheDocument();
  });

  it("says when an ex-date restated the reference", () => {
    render(<RecordView record={{ kind: "watchlist", watchlist: core, quotes: quoted }} />);

    expect(screen.getByText(/FPT went ex: its previous close 47,500 is restated to 40,450/u)).toBeInTheDocument();
  });

  it("still lists the instruments when the quotes were refused", () => {
    render(
      <RecordView
        record={{ kind: "watchlist", watchlist: core, quoteProblem: "2026-10-03 is not a trading day on HOSE." }}
      />,
    );

    expect(screen.getByText(/No quotes: 2026-10-03 is not a trading day/u)).toBeInTheDocument();
    expect(screen.getByText("Vietcombank")).toBeInTheDocument();
  });
});
