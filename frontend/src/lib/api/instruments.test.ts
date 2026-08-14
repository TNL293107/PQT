import { afterEach, describe, expect, it, vi } from "vitest";
import {
  InstrumentApiError,
  fetchInstrument,
  resolveInstrument,
  searchInstruments,
} from "./instruments";
import type { Instrument } from "../../types/instrument";

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

const fpt: Instrument = {
  instrumentId: "0195a9e4-0000-7000-8000-000000000001",
  ticker: "FPT",
  name: "FPT Corporation",
  assetType: "Equity",
  exchange: "HOSE",
  currency: "VND",
  status: "Listed",
  matchKind: "ExactTicker",
};

afterEach(() => vi.unstubAllGlobals());

describe("searchInstruments", () => {
  it("returns the results in the order the server ranked them", async () => {
    // The client must not re-sort: ranking is specified and tested on the
    // server, and a second ordering here would silently disagree with it.
    const second = { ...fpt, instrumentId: "second", ticker: "FPT2" };
    vi.stubGlobal(
      "fetch",
      vi.fn(() =>
        Promise.resolve(
          jsonResponse({ query: "FPT", count: 2, limit: 20, results: [fpt, second] }),
        ),
      ),
    );

    const results = await searchInstruments("FPT");

    expect(results.map((result) => result.ticker)).toEqual(["FPT", "FPT2"]);
  });

  it("encodes the query so punctuation cannot alter the request", async () => {
    let requested = "";
    vi.stubGlobal(
      "fetch",
      vi.fn((input: RequestInfo | URL) => {
        requested = String(input);
        return Promise.resolve(
          jsonResponse({ query: "", count: 0, limit: 20, results: [] }),
        );
      }),
    );

    await searchInstruments("A&B 100%");

    expect(requested).toContain("q=A%26B%20100%25");
  });

  it("surfaces the problem detail from a rejected request", async () => {
    // The API writes RFC 9457 detail to be read by a user, so it is what the
    // search box shows rather than a generic failure.
    vi.stubGlobal(
      "fetch",
      vi.fn(() =>
        Promise.resolve(jsonResponse({ detail: "A search query is required." }, 400)),
      ),
    );

    await expect(searchInstruments("")).rejects.toThrow("A search query is required.");
  });

  it("reports the status when the body is not a problem document", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(() => Promise.resolve(new Response("<html>502</html>", { status: 502 }))),
    );

    await expect(searchInstruments("FPT")).rejects.toBeInstanceOf(InstrumentApiError);
  });
});

describe("resolveInstrument", () => {
  it("reads the body of a resolved symbol", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(() =>
        Promise.resolve(
          jsonResponse({
            query: "FPT",
            outcome: "Resolved",
            instrument: fpt,
            candidates: [],
          }),
        ),
      ),
    );

    const resolution = await resolveInstrument("FPT");

    expect(resolution.outcome).toBe("Resolved");
    expect(resolution.instrument?.instrumentId).toBe(fpt.instrumentId);
  });

  it("treats not-found as a result rather than a failure", async () => {
    // The API answers 404 to distinguish it from a resolved symbol. It is
    // still an answer, and the caller has to be able to read it.
    vi.stubGlobal(
      "fetch",
      vi.fn(() =>
        Promise.resolve(
          jsonResponse(
            { query: "ZZZ", outcome: "NotFound", instrument: null, candidates: [] },
            404,
          ),
        ),
      ),
    );

    const resolution = await resolveInstrument("ZZZ");

    expect(resolution.outcome).toBe("NotFound");
  });

  it("treats an ambiguous symbol as a result carrying its candidates", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(() =>
        Promise.resolve(
          jsonResponse(
            {
              query: "AAA",
              outcome: "Ambiguous",
              instrument: null,
              candidates: [fpt, { ...fpt, exchange: "UPCOM" }],
            },
            409,
          ),
        ),
      ),
    );

    const resolution = await resolveInstrument("AAA");

    expect(resolution.outcome).toBe("Ambiguous");
    expect(resolution.candidates).toHaveLength(2);
  });

  it("throws when the service genuinely fails", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(() => Promise.resolve(jsonResponse({ detail: "Server error." }, 500))),
    );

    await expect(resolveInstrument("FPT")).rejects.toBeInstanceOf(InstrumentApiError);
  });
});

describe("fetchInstrument", () => {
  it("returns null for an unknown identifier", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(() => Promise.resolve(jsonResponse({ detail: "Not found." }, 404))),
    );

    await expect(fetchInstrument("missing")).resolves.toBeNull();
  });

  it("re-reads the instrument behind an identifier", async () => {
    vi.stubGlobal("fetch", vi.fn(() => Promise.resolve(jsonResponse(fpt))));

    const instrument = await fetchInstrument(fpt.instrumentId);

    expect(instrument?.ticker).toBe("FPT");
  });
});
