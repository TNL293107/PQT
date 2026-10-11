import { afterEach, describe, expect, it, vi } from "vitest";
import {
  WatchlistApiError,
  addToWatchlist,
  createWatchlist,
  deleteWatchlist,
  fetchWatchlist,
} from "./watchlists";

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

const KEY = "k".repeat(40);

const core = {
  name: "core",
  createdAtUtc: "2026-10-11T09:00:00Z",
  updatedAtUtc: "2026-10-11T09:00:00Z",
  count: 0,
  items: [],
};

afterEach(() => vi.unstubAllGlobals());

describe("watchlist client", () => {
  it("sends the key as a bearer token on a write and never on a read", async () => {
    const fetchMock = vi.fn(() => Promise.resolve(jsonResponse(core)));
    vi.stubGlobal("fetch", fetchMock);

    await createWatchlist("core", KEY);
    await fetchWatchlist("core");

    const calls = fetchMock.mock.calls as unknown as [string, RequestInit][];
    const write = calls[0]![1].headers as Record<string, string>;
    const read = calls[1]![1].headers as Record<string, string>;

    expect(calls[0]![1].method).toBe("POST");
    expect(write["Authorization"]).toBe(`Bearer ${KEY}`);
    expect(write["Content-Type"]).toBe("application/json");
    expect(read["Authorization"]).toBeUndefined();
  });

  it("encodes the list name and the instrument into the path", async () => {
    const fetchMock = vi.fn(() => Promise.resolve(jsonResponse(core)));
    vi.stubGlobal("fetch", fetchMock);

    await addToWatchlist("core", "0195a9e4-0000-7000-8000-000000000001", KEY);

    const [url, init] = (fetchMock.mock.calls as unknown as [string, RequestInit][])[0]!;
    expect(url).toMatch(/\/watchlists\/core\/instruments\/0195a9e4-0000-7000-8000-000000000001$/);
    expect(init.method).toBe("PUT");
  });

  it("carries the status and the problem detail of a refusal", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(() =>
        Promise.resolve(jsonResponse({ title: "Writes are disabled.", detail: "Set PQT_OPERATOR_KEY." }, 403)),
      ),
    );

    const error = await createWatchlist("core", null).catch((caught: unknown) => caught);

    expect(error).toBeInstanceOf(WatchlistApiError);
    expect((error as WatchlistApiError).status).toBe(403);
    expect((error as WatchlistApiError).message).toBe("Set PQT_OPERATOR_KEY.");
  });

  it("treats a delete's empty answer as done", async () => {
    vi.stubGlobal("fetch", vi.fn(() => Promise.resolve(new Response(null, { status: 204 }))));

    await expect(deleteWatchlist("core", KEY)).resolves.toBeUndefined();
  });
});
