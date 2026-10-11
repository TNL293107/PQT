import { render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { BREADTH, member } from "../../lib/console/breadth.fixture";
import { RecordView } from "./RecordView";

describe("BreadthView", () => {
  it("states the counts in words and glyphs, not colour alone", () => {
    render(<RecordView record={{ kind: "breadth", breadth: BREADTH }} />);

    const bar = screen.getByRole("img");
    expect(bar).toHaveAccessibleName(
      "1 advancing, 1 unchanged, 1 declining, 1 without data, of 4 members",
    );
    // Each segment carries its glyph as text.
    expect(bar).toHaveTextContent("▲=▼·");

    expect(screen.getByText("ADVANCING")).toBeInTheDocument();
    expect(screen.getByText("NO DATA")).toBeInTheDocument();
  });

  it("puts the riser among the top movers and the faller among the bottom", () => {
    render(<RecordView record={{ kind: "breadth", breadth: BREADTH }} />);

    const top = screen.getByRole("region", { name: "Top movers" });
    const bottom = screen.getByRole("region", { name: "Bottom movers" });

    expect(within(top).getByText("RISE")).toBeInTheDocument();
    expect(within(top).getByText("▲ +5.00%")).toBeInTheDocument();
    expect(within(top).queryByText("FALL")).not.toBeInTheDocument();
    expect(within(bottom).getByText("FALL")).toBeInTheDocument();
    expect(within(bottom).getByText("▼ -3.00%")).toBeInTheDocument();
  });

  it("explains an ex-date restatement and names the members without data", () => {
    render(<RecordView record={{ kind: "breadth", breadth: BREADTH }} />);

    expect(
      screen.getByText(/DIV went ex: its previous close 25,000 is restated to 24,000/u),
    ).toBeInTheDocument();
    expect(screen.getByText(/No data for NIL/u)).toBeInTheDocument();
  });

  it("says when the server chose the session", () => {
    render(
      <RecordView record={{ kind: "breadth", breadth: { ...BREADTH, sessionWasDefaulted: true } }} />,
    );

    expect(screen.getByText(/the latest session any member has a bar for/u)).toBeInTheDocument();
  });

  it("says nothing advanced rather than drawing an empty table", () => {
    const allDown = {
      ...BREADTH,
      advancing: 0,
      declining: 1,
      unchanged: 0,
      noData: 0,
      memberCount: 1,
      members: [member({ ticker: "FALL", change: -0.01, category: "Declining" as const })],
    };

    render(<RecordView record={{ kind: "breadth", breadth: allDown }} />);

    expect(screen.getByText("Nothing advanced.")).toBeInTheDocument();
  });

  it("reports an empty universe as a fact rather than a gap", () => {
    render(
      <RecordView
        record={{
          kind: "breadth",
          breadth: { ...BREADTH, memberCount: 0, members: [], venues: [], noData: 0 },
        }}
      />,
    );

    expect(screen.getByText(/had no members on 2026-09-30/u)).toBeInTheDocument();
  });
});

describe("UniversesView", () => {
  it("shows unknown, never zero, for a count the claim does not reach", () => {
    render(
      <RecordView
        record={{
          kind: "universes",
          listing: {
            asOf: "2026-10-11",
            count: 2,
            universes: [
              {
                code: "OLD",
                name: "Lapsed",
                kind: "Custom",
                coverageFrom: "2024-01-01",
                coverageUntil: "2025-01-01",
                memberCount: null,
              },
              {
                code: "VN30",
                name: "VN30 Index",
                kind: "Index",
                coverageFrom: "2026-07-01",
                coverageUntil: null,
                memberCount: 30,
              },
            ],
          },
        }}
      />,
    );

    const rows = screen.getAllByRole("row");
    expect(within(rows[1]!).getByText("unknown")).toBeInTheDocument();
    expect(within(rows[1]!).getByText("2024-01-01 → before 2025-01-01")).toBeInTheDocument();
    expect(within(rows[2]!).getByText("30")).toBeInTheDocument();
    expect(within(rows[2]!).getByText("2026-07-01 →")).toBeInTheDocument();
  });
});
