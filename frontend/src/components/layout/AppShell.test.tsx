import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import { App } from "../../App";
import type { Instrument } from "../../types/instrument";

const fpt: Instrument = {
  instrumentId: "id-fpt",
  ticker: "FPT",
  name: "FPT Corporation",
  assetType: "Equity",
  exchange: "HOSE",
  currency: "VND",
  status: "Listed",
  matchKind: "ExactTicker",
};

const nvda: Instrument = {
  instrumentId: "id-vnm",
  ticker: "VNM",
  name: "Vietnam Dairy Products Joint Stock Company",
  assetType: "Equity",
  exchange: "HOSE",
  currency: "VND",
  status: "Listed",
  matchKind: "ExactTicker",
};

/**
 * Answers instrument search with a fixed result and leaves every other
 * request — the health poll the status page runs — pending.
 */
function stubApi(results: readonly Instrument[]) {
  vi.stubGlobal(
    "fetch",
    vi.fn((input: RequestInfo | URL) => {
      const url = String(input);

      if (!url.includes("/instruments/search")) {
        return new Promise<Response>(() => {});
      }

      return Promise.resolve(
        new Response(
          JSON.stringify({
            query: "Q",
            count: results.length,
            limit: 20,
            results,
          }),
          { status: 200, headers: { "Content-Type": "application/json" } },
        ),
      );
    }),
  );
}

function renderTerminal(results: readonly Instrument[] = [fpt]) {
  stubApi(results);

  render(
    <MemoryRouter initialEntries={["/"]}>
      <App />
    </MemoryRouter>,
  );

  return userEvent.setup();
}

afterEach(() => vi.unstubAllGlobals());

describe("Terminal security selection", () => {
  it("reports no security before one is chosen", () => {
    renderTerminal();

    expect(screen.getByText("No security selected")).toBeInTheDocument();
  });

  it("opens the search overlay on Ctrl+K", async () => {
    // The shortcut has to work wherever focus is, which is the whole point of
    // registering it on the document.
    const user = renderTerminal();

    await user.keyboard("{Control>}k{/Control}");

    expect(screen.getByRole("dialog", { name: "Security search" })).toBeInTheDocument();
  });

  it("opens the search overlay on Cmd+K", async () => {
    const user = renderTerminal();

    await user.keyboard("{Meta>}k{/Meta}");

    expect(screen.getByRole("dialog", { name: "Security search" })).toBeInTheDocument();
  });

  it("sets the current security from a keyboard-only flow", async () => {
    // Scenario A and G together: Ctrl+K, type, Enter, and the terminal's
    // context is FPT — with no pointer involved at any step.
    const user = renderTerminal();

    await user.keyboard("{Control>}k{/Control}");
    await user.keyboard("FPT");
    await screen.findByText("FPT Corporation");
    await user.keyboard("{Enter}");

    expect(screen.getByText("Security")).toBeInTheDocument();
    expect(screen.getByText("FPT")).toBeInTheDocument();
    expect(screen.getByText("FPT Corporation")).toBeInTheDocument();
  });

  it("closes the overlay once a security is selected", async () => {
    const user = renderTerminal();

    await user.keyboard("{Control>}k{/Control}");
    await user.keyboard("FPT");
    await screen.findByText("FPT Corporation");
    await user.keyboard("{Enter}");

    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("replaces the security when a different one is selected", async () => {
    // Scenario F: after switching, no trace of the previous security is on
    // screen.
    const user = renderTerminal([fpt]);

    await user.keyboard("{Control>}k{/Control}");
    await user.keyboard("FPT");
    await screen.findByText("FPT Corporation");
    await user.keyboard("{Enter}");

    stubApi([nvda]);

    await user.keyboard("{Control>}k{/Control}");
    await user.keyboard("VNM");
    await screen.findByText("Vietnam Dairy Products Joint Stock Company");
    await user.keyboard("{Enter}");

    expect(screen.getByText("VNM")).toBeInTheDocument();
    expect(screen.queryByText("FPT")).not.toBeInTheDocument();
    expect(screen.queryByText("FPT Corporation")).not.toBeInTheDocument();
  });

  it("cancels on Escape and leaves the context untouched", async () => {
    const user = renderTerminal();

    await user.keyboard("{Control>}k{/Control}");
    await user.keyboard("FPT");
    await screen.findByText("FPT Corporation");
    await user.keyboard("{Escape}");

    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(screen.getByText("No security selected")).toBeInTheDocument();
  });

  it("keeps the security across a change of view", async () => {
    // The selection is terminal state, not page state. A user who picks a
    // security and navigates must not have to pick it again.
    const user = renderTerminal();

    await user.keyboard("{Control>}k{/Control}");
    await user.keyboard("FPT");
    await screen.findByText("FPT Corporation");
    await user.keyboard("{Enter}");

    await user.click(screen.getByRole("link", { name: "Capability Map" }));

    expect(
      screen.getByRole("heading", { level: 1, name: "Capability Map" }),
    ).toBeInTheDocument();
    expect(screen.getByText("FPT Corporation")).toBeInTheDocument();
  });

  it("clears the security back to none", async () => {
    const user = renderTerminal();

    await user.keyboard("{Control>}k{/Control}");
    await user.keyboard("FPT");
    await screen.findByText("FPT Corporation");
    await user.keyboard("{Enter}");

    await user.click(screen.getByRole("button", { name: "Clear" }));

    expect(screen.getByText("No security selected")).toBeInTheDocument();
  });
});
