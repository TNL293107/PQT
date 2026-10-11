import { describe, expect, it, vi } from "vitest";
import { WatchlistApiError } from "../api/watchlists";
import type { Instrument } from "../../types/instrument";
import type { Watchlist } from "../../types/watchlist";
import { isReplayable, maskEntry, runKey, runWatchlist, runWorkspace, type OperatorServices } from "./operator";

const KEY = "k".repeat(40);

const core: Watchlist = {
  name: "core",
  createdAtUtc: "2026-10-11T09:00:00Z",
  updatedAtUtc: "2026-10-11T09:00:00Z",
  count: 0,
  items: [],
};

function instrument(ticker: string): Instrument {
  return {
    instrumentId: `id-${ticker}`,
    ticker,
    name: `${ticker} Corporation`,
    assetType: "Equity",
    exchange: "HOSE",
    currency: "VND",
    status: "Listed",
    matchKind: "ExactTicker",
  };
}

function services(held: string | null = KEY) {
  let key = held;
  const fake = {
    key: { read: () => key, write: vi.fn((next: string | null) => ((key = next), true)) },
    watchlists: {
      list: vi.fn(),
      get: vi.fn(() => Promise.resolve(core)),
      create: vi.fn(() => Promise.resolve(core)),
      add: vi.fn(() => Promise.resolve(core)),
      remove: vi.fn(() => Promise.resolve(core)),
      delete: vi.fn(() => Promise.resolve()),
    },
    workspaces: {
      list: vi.fn(),
      get: vi.fn(() =>
        Promise.resolve({ name: "morning", entries: ["breadth VN30", "FPT GP"], createdAtUtc: "", updatedAtUtc: "" }),
      ),
      save: vi.fn((name: string, entries: readonly string[]) =>
        Promise.resolve({ name, entries: [...entries], createdAtUtc: "", updatedAtUtc: "" }),
      ),
      delete: vi.fn(() => Promise.resolve()),
    },
  };

  return fake as unknown as OperatorServices & typeof fake;
}

const resolveKnown = vi.fn((ticker: string) =>
  Promise.resolve(
    ticker.toUpperCase() === "NOPE"
      ? { query: ticker, outcome: "NotFound" as const, instrument: null, candidates: [] }
      : { query: ticker, outcome: "Resolved" as const, instrument: instrument(ticker.toUpperCase()), candidates: [] },
  ),
);

describe("the operator key", () => {
  it("is never echoed, and a too-short one is refused", () => {
    const fake = services(null);

    expect(maskEntry(`key ${KEY}`)).toBe("key ••••••••");
    expect(maskEntry("key clear")).toBe("key clear");
    expect(runKey("short", fake).kind).toBe("error");
    expect(fake.key.write).not.toHaveBeenCalled();

    runKey(KEY, fake);
    expect(fake.key.write).toHaveBeenCalledWith(KEY);
  });

  it("is kept out of saved workspaces, and so are workspace lines", () => {
    expect(isReplayable(`key ${KEY}`)).toBe(false);
    expect(isReplayable("ws load morning")).toBe(false);
    expect(isReplayable("FPT GP")).toBe(true);
  });
});

describe("watchlist lines", () => {
  it("resolve every ticker before adding any, and send the held key", async () => {
    const fake = services();

    const record = await runWatchlist("add core FPT VCB", fake, resolveKnown as never);

    expect(record.kind).toBe("watchlist");
    expect(fake.watchlists.add).toHaveBeenCalledTimes(2);
    expect(fake.watchlists.add).toHaveBeenCalledWith("core", "id-FPT", KEY, undefined);
  });

  it("change nothing when one ticker does not resolve", async () => {
    const fake = services();

    const record = await runWatchlist("add core FPT NOPE", fake, resolveKnown as never);

    expect(record.kind).toBe("error");
    expect(fake.watchlists.add).not.toHaveBeenCalled();
  });

  it("turn a missing key into the instruction that fixes it", async () => {
    const fake = services(null);
    fake.watchlists.create.mockRejectedValueOnce(new WatchlistApiError("Operator key required.", 401));

    const record = await runWatchlist("create core", fake, resolveKnown as never);

    expect(record).toMatchObject({ kind: "error", message: "This needs the operator key." });
  });
});

describe("workspace lines", () => {
  it("save this session's views once each, without key or workspace lines", async () => {
    const fake = services();

    await runWorkspace("save morning", ["FPT GP", `key ${KEY}`, "breadth VN30", "FPT GP", "ws list"], fake);

    expect(fake.workspaces.save).toHaveBeenCalledWith("morning", ["FPT GP", "breadth VN30"], KEY, undefined);
  });

  it("load into a replay of the saved lines", async () => {
    const outcome = await runWorkspace("load morning", [], services());

    expect(outcome.replay).toEqual(["breadth VN30", "FPT GP"]);
  });

  it("refuse to save an empty session", async () => {
    const fake = services();

    const outcome = await runWorkspace("save morning", ["help"], fake);

    expect(outcome.record.kind).toBe("error");
    expect(fake.workspaces.save).not.toHaveBeenCalled();
  });
});
