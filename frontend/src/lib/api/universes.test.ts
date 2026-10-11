import { afterEach, describe, expect, it, vi } from "vitest";
import { UniverseApiError, fetchBreadth, fetchUniverses } from "./universes";

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

afterEach(() => vi.unstubAllGlobals());

describe("fetchBreadth", () => {
  it("asks for the named session, and for none when none is named", async () => {
    const requested: string[] = [];
    vi.stubGlobal(
      "fetch",
      vi.fn((input: RequestInfo | URL) => {
        requested.push(String(input));
        return Promise.resolve(jsonResponse({}));
      }),
    );

    await fetchBreadth("VN30", "2026-09-30");
    await fetchBreadth("VN30");

    expect(requested[0]).toMatch(/\/universes\/VN30\/breadth\?on=2026-09-30$/u);
    expect(requested[1]).toMatch(/\/universes\/VN30\/breadth$/u);
  });

  it("carries the server's reason and outcome on a refusal", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(() =>
        Promise.resolve(
          jsonResponse(
            {
              title: "That session is not a trading day.",
              detail: "2026-10-03 is not a trading day on HOSE.",
              outcome: "NotATradingDay",
            },
            422,
          ),
        ),
      ),
    );

    const error = await fetchBreadth("VN30", "2026-10-03").catch((caught: unknown) => caught);

    expect(error).toBeInstanceOf(UniverseApiError);
    expect(error).toMatchObject({
      status: 422,
      outcome: "NotATradingDay",
      message: "2026-10-03 is not a trading day on HOSE.",
    });
  });
});

describe("fetchUniverses", () => {
  it("reads the listing", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(() => Promise.resolve(jsonResponse({ asOf: "2026-10-11", count: 0, universes: [] }))),
    );

    expect(await fetchUniverses()).toEqual({ asOf: "2026-10-11", count: 0, universes: [] });
  });
});
