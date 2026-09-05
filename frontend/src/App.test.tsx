import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it, vi } from "vitest";
import { App } from "./App";

function renderAt(path: string) {
  vi.stubGlobal("fetch", vi.fn(() => new Promise(() => {})));

  return render(
    <MemoryRouter initialEntries={[path]}>
      <App />
    </MemoryRouter>,
  );
}

describe("App shell", () => {
  it("renders the terminal identity and phase marker", () => {
    renderAt("/");

    expect(screen.getByText("Personal Quant Terminal")).toBeInTheDocument();
    expect(screen.getByText("PHASE 1")).toBeInTheDocument();
  });

  it("states plainly that trading is not enabled", () => {
    // The footer is a standing reminder that this repository cannot place an
    // order. It should not be possible to remove it without a failing test.
    renderAt("/");

    expect(screen.getByText("LIVE_TRADING_ENABLED=false")).toBeInTheDocument();
  });

  it("opens on the console", () => {
    // The terminal's primary surface is a prompt, not a dashboard. Anything
    // that needs a page of panels is reachable from the nav; the thing you
    // land on is the thing you type into.
    renderAt("/");

    expect(screen.getByRole("textbox", { name: "Terminal command" })).toBeInTheDocument();
  });

  it("still reaches the infrastructure view", () => {
    renderAt("/infrastructure");

    expect(
      screen.getByRole("heading", { level: 1, name: "Infrastructure" }),
    ).toBeInTheDocument();
  });

  it("navigates to the capability map", async () => {
    const user = userEvent.setup();
    renderAt("/");

    await user.click(screen.getByRole("link", { name: "Capability Map" }));

    expect(
      screen.getByRole("heading", { level: 1, name: "Capability Map" }),
    ).toBeInTheDocument();
  });

  it("marks unbuilt capabilities as planned", () => {
    // A badge may only say something other than PLANNED because the feature
    // genuinely shipped.
    renderAt("/capabilities");

    const badges = screen.getAllByText("PLANNED");
    expect(badges.length).toBeGreaterThan(10);
  });

  it("marks the delivered data-foundation phases as complete", () => {
    // The instrument master, ingestion and data quality all ship. A badge may
    // only claim it because the behaviour genuinely exists.
    renderAt("/capabilities");

    expect(screen.getAllByText("COMPLETE")).toHaveLength(3);
  });

  it("claims nothing is under way while nothing is", () => {
    // The in-progress badge is reserved for a phase actually being built, so
    // it is absent between phases rather than left on the last one finished.
    renderAt("/capabilities");

    expect(screen.queryAllByText("IN PROGRESS")).toHaveLength(0);
  });

  it("shows a not-found view for an unknown route", () => {
    renderAt("/orders");

    expect(
      screen.getByRole("heading", { level: 1, name: "No such view" }),
    ).toBeInTheDocument();
  });
});
