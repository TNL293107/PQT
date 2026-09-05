import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import type { ConsoleServices } from "../../lib/console/run";
import type { Instrument } from "../../types/instrument";
import { Console } from "./Console";

const FPT: Instrument = {
  instrumentId: "11111111-1111-1111-1111-111111111111",
  ticker: "FPT",
  name: "FPT Corporation",
  assetType: "Equity",
  exchange: "HOSE",
  currency: "VND",
  status: "Listed",
};

/** Services that answer instantly and reach no network. */
function stubServices(): ConsoleServices {
  return {
    search: vi.fn(async () => ({ query: "", count: 1, limit: 20, results: [FPT] })),
    resolve: vi.fn(async () => ({
      query: "FPT",
      outcome: "Resolved" as const,
      instrument: FPT,
      candidates: [],
    })),
    bars: vi.fn(),
    quality: vi.fn(),
    issues: vi.fn(),
    ingestion: vi.fn(),
    health: vi.fn(),
  } as unknown as ConsoleServices;
}

function terminal() {
  return screen.getByRole("textbox", { name: "Terminal command" });
}

describe("Console", () => {
  it("opens with a banner saying what the console does and does not do", () => {
    render(<Console services={stubServices()} />);

    expect(screen.getByText(/Nothing here writes/)).toBeInTheDocument();
  });

  it("echoes the line and shows what came back", async () => {
    const user = userEvent.setup();
    render(<Console services={stubServices()} />);

    await user.type(terminal(), "FPT{Enter}");

    await waitFor(() => expect(screen.getByText("FPT Corporation")).toBeInTheDocument());
    // The transcript keeps the entry as typed, not as parsed.
    expect(screen.getByText("FPT", { selector: "span" })).toBeInTheDocument();
  });

  it("empties the input once the line is submitted", async () => {
    const user = userEvent.setup();
    render(<Console services={stubServices()} />);

    await user.type(terminal(), "help{Enter}");

    await waitFor(() => expect(terminal()).toHaveValue(""));
  });

  it("recalls the previous line with the up arrow", async () => {
    const user = userEvent.setup();
    render(<Console services={stubServices()} />);

    await user.type(terminal(), "help{Enter}");
    await waitFor(() => expect(terminal()).toHaveValue(""));
    await user.keyboard("{ArrowUp}");

    expect(terminal()).toHaveValue("help");
  });

  it("returns to an empty prompt when the down arrow walks past the newest line", async () => {
    const user = userEvent.setup();
    render(<Console services={stubServices()} />);

    await user.type(terminal(), "help{Enter}");
    await waitFor(() => expect(terminal()).toHaveValue(""));
    await user.keyboard("{ArrowUp}{ArrowDown}");

    expect(terminal()).toHaveValue("");
  });

  it("completes a mnemonic on tab", async () => {
    const user = userEvent.setup();
    render(<Console services={stubServices()} />);

    await user.type(terminal(), "FPT QL");
    await user.keyboard("{Tab}");

    expect(terminal()).toHaveValue("FPT QLTY");
  });

  it("drops the transcript on clear and keeps taking commands", async () => {
    const user = userEvent.setup();
    render(<Console services={stubServices()} />);

    await user.type(terminal(), "clear{Enter}");

    await waitFor(() => expect(screen.queryByText(/Nothing here writes/)).not.toBeInTheDocument());
    expect(terminal()).toBeEnabled();
  });

  it("keeps the transcript when a command is refused", async () => {
    // A console that lost its history because a ticker was mistyped would be
    // unusable, and the refusal belongs in the history as much as an answer.
    const user = userEvent.setup();
    const services = {
      ...stubServices(),
      resolve: vi.fn(async () => ({
        query: "ZZZ",
        outcome: "NotFound" as const,
        instrument: null,
        candidates: [],
      })),
    } as unknown as ConsoleServices;

    render(<Console services={services} />);

    await user.type(terminal(), "ZZZ{Enter}");

    await waitFor(() =>
      expect(screen.getByText(/No instrument currently trades/)).toBeInTheDocument(),
    );
    expect(screen.getByText(/Nothing here writes/)).toBeInTheDocument();
  });

  it("echoes the line while the command is still running", async () => {
    // A terminal that swallowed what you typed until the answer arrived would
    // leave you unsure it took the command at all.
    let release: (() => void) | undefined;
    const held = new Promise<void>((resolve) => {
      release = resolve;
    });

    const user = userEvent.setup();
    const services = {
      ...stubServices(),
      resolve: vi.fn(async () => {
        await held;
        return {
          query: "FPT",
          outcome: "Resolved" as const,
          instrument: FPT,
          candidates: [],
        };
      }),
    } as unknown as ConsoleServices;

    render(<Console services={services} />);

    await user.type(terminal(), "FPT{Enter}");

    expect(await screen.findByRole("status")).toHaveTextContent("working");
    expect(screen.getByText("FPT", { selector: "span" })).toBeInTheDocument();

    release?.();

    await waitFor(() => expect(screen.getByText("FPT Corporation")).toBeInTheDocument());
    expect(screen.queryByRole("status")).not.toBeInTheDocument();
  });

  it("submits nothing on an empty line", async () => {
    const user = userEvent.setup();
    const services = stubServices();
    render(<Console services={services} />);

    await user.type(terminal(), "   {Enter}");

    expect(services.resolve).not.toHaveBeenCalled();
  });
});
