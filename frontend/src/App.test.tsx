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
    expect(screen.getByText("PHASE 0")).toBeInTheDocument();
  });

  it("states plainly that trading is not enabled", () => {
    // The footer is a standing reminder that this repository cannot place an
    // order. It should not be possible to remove it without a failing test.
    renderAt("/");

    expect(screen.getByText("LIVE_TRADING_ENABLED=false")).toBeInTheDocument();
  });

  it("opens on the infrastructure view", () => {
    renderAt("/");

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

  it("marks every listed capability as planned", () => {
    // Nothing on the capability map is implemented. If a badge ever says
    // otherwise it must be because the feature genuinely shipped.
    renderAt("/capabilities");

    const badges = screen.getAllByText("PLANNED");
    expect(badges.length).toBeGreaterThan(10);
  });

  it("shows a not-found view for an unknown route", () => {
    renderAt("/orders");

    expect(
      screen.getByRole("heading", { level: 1, name: "No such view" }),
    ).toBeInTheDocument();
  });
});
