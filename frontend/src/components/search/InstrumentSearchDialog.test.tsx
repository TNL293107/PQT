import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { InstrumentSearchDialog } from "./InstrumentSearchDialog";
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

const fox: Instrument = {
  instrumentId: "id-fox",
  ticker: "FOX",
  name: "FPT Telecom Joint Stock Company",
  assetType: "Equity",
  exchange: "UPCOM",
  currency: "VND",
  status: "Listed",
  matchKind: "NamePrefix",
};

function stubSearch(results: readonly Instrument[]) {
  vi.stubGlobal(
    "fetch",
    vi.fn(() =>
      Promise.resolve(
        new Response(
          JSON.stringify({
            query: "FPT",
            count: results.length,
            limit: 20,
            results,
          }),
          { status: 200, headers: { "Content-Type": "application/json" } },
        ),
      ),
    ),
  );
}

function renderDialog(results: readonly Instrument[] = [fpt, fox]) {
  stubSearch(results);

  const onSelect = vi.fn();
  const onClose = vi.fn();

  render(<InstrumentSearchDialog onSelect={onSelect} onClose={onClose} />);

  return { onSelect, onClose, user: userEvent.setup() };
}

afterEach(() => vi.unstubAllGlobals());

describe("InstrumentSearchDialog", () => {
  it("focuses the input as soon as it opens", () => {
    // Ctrl+K has to land the user on a field they can type into. Anything
    // else makes the shortcut a two-step action.
    renderDialog();

    expect(screen.getByLabelText("Search securities")).toHaveFocus();
  });

  it("shows the facts needed to tell two listings apart", async () => {
    const { user } = renderDialog();

    await user.type(screen.getByLabelText("Search securities"), "FPT");

    expect(await screen.findByText("FPT Corporation")).toBeInTheDocument();
    expect(screen.getByText("FPT Telecom Joint Stock Company")).toBeInTheDocument();
    expect(screen.getByText("HOSE")).toBeInTheDocument();
    expect(screen.getByText("UPCOM")).toBeInTheDocument();
  });

  it("preserves the order the server ranked the results in", async () => {
    const { user } = renderDialog();

    await user.type(screen.getByLabelText("Search securities"), "FPT");
    await screen.findByText("FPT Corporation");

    const options = screen.getAllByRole("option");
    expect(options[0]).toHaveTextContent("FPT Corporation");
    expect(options[1]).toHaveTextContent("FPT Telecom Joint Stock Company");
  });

  it("highlights the strongest match first", async () => {
    // Enter without arrowing has to select the top-ranked result, which is
    // what makes typing a ticker and pressing Enter a single action.
    const { user } = renderDialog();

    await user.type(screen.getByLabelText("Search securities"), "FPT");
    await screen.findByText("FPT Corporation");

    expect(screen.getAllByRole("option")[0]).toHaveAttribute("aria-selected", "true");
  });

  it("selects the highlighted result on Enter", async () => {
    const { user, onSelect } = renderDialog();

    await user.type(screen.getByLabelText("Search securities"), "FPT");
    await screen.findByText("FPT Corporation");
    await user.keyboard("{Enter}");

    expect(onSelect).toHaveBeenCalledWith(fpt);
  });

  it("moves the highlight with the arrow keys", async () => {
    const { user, onSelect } = renderDialog();

    await user.type(screen.getByLabelText("Search securities"), "FPT");
    await screen.findByText("FPT Corporation");
    await user.keyboard("{ArrowDown}{Enter}");

    expect(onSelect).toHaveBeenCalledWith(fox);
  });

  it("wraps the highlight past the ends of the list", async () => {
    // Holding an arrow key should never dead-end on a short list.
    const { user, onSelect } = renderDialog();

    await user.type(screen.getByLabelText("Search securities"), "FPT");
    await screen.findByText("FPT Corporation");
    await user.keyboard("{ArrowUp}{Enter}");

    expect(onSelect).toHaveBeenCalledWith(fox);
  });

  it("completes the whole flow without a pointer", async () => {
    // Scenario G: Ctrl+K, type, arrow, Enter. This test fails if any step
    // starts requiring a click.
    const { user, onSelect } = renderDialog();

    await user.keyboard("FPT");
    await screen.findByText("FPT Corporation");
    await user.keyboard("{ArrowDown}{ArrowUp}{Enter}");

    expect(onSelect).toHaveBeenCalledWith(fpt);
  });

  it("closes on Escape without selecting anything", async () => {
    const { user, onSelect, onClose } = renderDialog();

    await user.keyboard("{Escape}");

    expect(onClose).toHaveBeenCalled();
    expect(onSelect).not.toHaveBeenCalled();
  });

  it("does nothing on Enter when there is no result to select", async () => {
    const { user, onSelect } = renderDialog([]);

    await user.type(screen.getByLabelText("Search securities"), "XYZABC");
    await user.keyboard("{Enter}");

    expect(onSelect).not.toHaveBeenCalled();
  });

  it("says so when nothing matches", async () => {
    // Silence would leave the user unable to tell "no such security" from
    // "the request failed".
    const { user } = renderDialog([]);

    await user.type(screen.getByLabelText("Search securities"), "XYZABC");

    expect(await screen.findByText(/No instruments found for/u)).toBeInTheDocument();
  });

  it("reports a failure rather than showing an empty list", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(() =>
        Promise.resolve(
          new Response(JSON.stringify({ detail: "The instrument service is down." }), {
            status: 503,
            headers: { "Content-Type": "application/json" },
          }),
        ),
      ),
    );

    render(<InstrumentSearchDialog onSelect={vi.fn()} onClose={vi.fn()} />);
    const user = userEvent.setup();

    await user.type(screen.getByLabelText("Search securities"), "FPT");

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "The instrument service is down.",
    );
  });

  it("states that a function mnemonic is not available yet", async () => {
    // Rather than searching the instrument master for a company called
    // "FPT GP" and reporting that none exists.
    const { user } = renderDialog();

    await user.type(screen.getByLabelText("Search securities"), "FPT GP");

    expect(await screen.findByText(/is not available/u)).toBeInTheDocument();
    expect(screen.getByText("GP")).toBeInTheDocument();
  });

  it("issues one request for a burst of typing", async () => {
    // The search runs on a per-keystroke path; debouncing is what keeps a
    // five-character ticker from being five round trips.
    const { user } = renderDialog();

    await user.type(screen.getByLabelText("Search securities"), "FPT");
    await screen.findByText("FPT Corporation");

    await waitFor(() => {
      expect(vi.mocked(fetch)).toHaveBeenCalledTimes(1);
    });
  });
});
