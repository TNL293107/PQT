import { describe, expect, it, vi } from "vitest";
import type { Instrument } from "../../types/instrument";
import type { BarSeries, DataQualityReport } from "../../types/marketData";
import { runEntry, type ConsoleServices } from "./run";

const FPT: Instrument = {
  instrumentId: "11111111-1111-1111-1111-111111111111",
  ticker: "FPT",
  name: "FPT Corporation",
  assetType: "Equity",
  exchange: "HOSE",
  currency: "VND",
  status: "Listed",
};

const SERIES: BarSeries = {
  instrumentId: FPT.instrumentId,
  interval: "D1",
  adjusted: false,
  adjustedAtSource: false,
  adjustedBars: 0,
  count: 0,
  limit: 40,
  bars: [],
};

const REPORT: DataQualityReport = {
  instrumentId: FPT.instrumentId,
  ticker: "FPT",
  interval: "D1",
  fromDate: "2016-01-01",
  toDate: "2016-12-31",
  sessionsExpected: 250,
  barsStored: 250,
  unvalidatedBars: 0,
  openIssues: {},
  ingestion: {
    runs: 1,
    succeeded: 1,
    failed: 0,
    barsFetched: 250,
    barsAccepted: 250,
    barsRejected: 0,
    barsStored: 250,
  },
  score: {
    completeness: 1,
    consistency: 1,
    validity: 1,
    sourceReliability: 1,
    overall: 1,
  },
  calendarIsComplete: true,
};

/**
 * Services that answer without a network.
 *
 * Every one is a spy, so a test can assert not only what came back but what
 * the console decided not to ask for — which is the whole point of a refusal.
 */
function stubServices(overrides: Partial<ConsoleServices> = {}): ConsoleServices {
  return {
    search: vi.fn(async () => ({ query: "", count: 1, limit: 20, results: [FPT] })),
    resolve: vi.fn(async () => ({
      query: "FPT",
      outcome: "Resolved" as const,
      instrument: FPT,
      candidates: [],
    })),
    bars: vi.fn(async () => SERIES),
    quality: vi.fn(async () => REPORT),
    issues: vi.fn(async () => ({
      instrumentId: FPT.instrumentId,
      interval: "D1",
      count: 0,
      results: [],
    })),
    ingestion: vi.fn(async () => ({
      instrumentId: FPT.instrumentId,
      interval: "D1",
      count: 0,
      runs: [],
    })),
    health: vi.fn(async () => ({
      services: [],
      checkedAt: new Date("2026-09-05T00:00:00Z"),
      apiReachable: true,
    })),
    ...overrides,
  } as ConsoleServices;
}

describe("runEntry — the verbs", () => {
  it("answers an empty line with nothing and reaches no service", async () => {
    const services = stubServices();

    const result = await runEntry("   ", services);

    expect(result.record).toEqual({ kind: "lines", lines: [] });
    expect(services.resolve).not.toHaveBeenCalled();
  });

  it("answers help without a network", async () => {
    const services = stubServices();

    expect((await runEntry("help", services)).record.kind).toBe("help");
    expect(services.health).not.toHaveBeenCalled();
  });

  it("asks the caller to clear the transcript", async () => {
    expect(await runEntry("clear", stubServices())).toMatchObject({ clear: true });
  });

  it("reads health from the API", async () => {
    const record = (await runEntry("health", stubServices())).record;

    expect(record.kind).toBe("health");
  });

  it("refuses a find with nothing to search for", async () => {
    const services = stubServices();

    const record = (await runEntry("find", services)).record;

    expect(record).toMatchObject({ kind: "error" });
    expect(services.search).not.toHaveBeenCalled();
  });

  it("searches names as well as tickers", async () => {
    const services = stubServices();

    const record = (await runEntry("find hoa phat", services)).record;

    expect(record).toMatchObject({ kind: "instruments", query: "hoa phat" });
    expect(services.search).toHaveBeenCalledWith("hoa phat", undefined);
  });
});

describe("runEntry — resolving the security", () => {
  it("describes a security typed on its own", async () => {
    const record = (await runEntry("FPT", stubServices())).record;

    expect(record).toMatchObject({ kind: "description", instrument: FPT });
  });

  it("points an unknown ticker at find rather than failing blankly", async () => {
    const services = stubServices({
      resolve: vi.fn(async () => ({
        query: "ZZZ",
        outcome: "NotFound" as const,
        instrument: null,
        candidates: [],
      })),
    });

    const record = (await runEntry("ZZZ GP", services)).record;

    expect(record.kind).toBe("error");
    expect(record).toMatchObject({ hint: expect.stringContaining("find ZZZ") });
    expect(services.bars).not.toHaveBeenCalled();
  });

  it("shows both candidates rather than picking one", async () => {
    // The same ticker can be live on two venues at once, because uniqueness is
    // enforced per venue. Choosing between them is not the console's call.
    const onHnx = { ...FPT, instrumentId: "2", exchange: "HNX" };
    const services = stubServices({
      resolve: vi.fn(async () => ({
        query: "FPT",
        outcome: "Ambiguous" as const,
        instrument: null,
        candidates: [FPT, onHnx],
      })),
    });

    const record = (await runEntry("FPT GP", services)).record;

    expect(record).toMatchObject({ kind: "instruments", ambiguous: true });
    expect(services.bars).not.toHaveBeenCalled();
  });

  it("refuses a line that names no security", async () => {
    const record = (await runEntry("--limit 5", stubServices())).record;

    expect(record.kind).toBe("error");
  });
});

describe("runEntry — GP", () => {
  it("passes the window the caller asked for", async () => {
    const services = stubServices();

    await runEntry("FPT GP --limit 40 --interval W1", services);

    expect(services.bars).toHaveBeenCalledWith(
      FPT.instrumentId,
      { interval: "W1", limit: 40 },
      undefined,
    );
  });

  it("reads raw when raw is asked for", async () => {
    const services = stubServices();

    await runEntry("FPT GP --raw", services);

    expect(services.bars).toHaveBeenCalledWith(FPT.instrumentId, { adjusted: false }, undefined);
  });

  it("refuses raw and adjusted together instead of preferring one", async () => {
    // They are opposite requests. Answering with either would be answering a
    // question nobody asked.
    const services = stubServices();

    const record = (await runEntry("FPT GP --raw --adjusted", services)).record;

    expect(record.kind).toBe("error");
    expect(services.bars).not.toHaveBeenCalled();
  });

  it("refuses an as-of that is not a date", async () => {
    const services = stubServices();

    const record = (await runEntry("FPT GP --as-of yesterday", services)).record;

    expect(record.kind).toBe("error");
    expect(services.bars).not.toHaveBeenCalled();
  });

  it("carries the as-of cut through to the read and echoes it", async () => {
    const services = stubServices();

    const record = (await runEntry("FPT GP --as-of 2016-05-27", services)).record;

    expect(services.bars).toHaveBeenCalledWith(
      FPT.instrumentId,
      { knownAsOf: new Date("2016-05-27").toISOString() },
      undefined,
    );
    expect(record).toMatchObject({ kind: "series", knownAsOf: "2016-05-27" });
  });
});

describe("runEntry — QLTY and ING", () => {
  it("reads the score and the findings together", async () => {
    // A score with no findings beside it invites the reader to treat the
    // number as the whole answer.
    const services = stubServices();

    const record = (await runEntry("FPT QLTY", services)).record;

    expect(record).toMatchObject({ kind: "quality", issues: [] });
    expect(services.quality).toHaveBeenCalledWith(FPT.instrumentId, {}, undefined);
    expect(services.issues).toHaveBeenCalledWith(FPT.instrumentId, undefined, undefined);
  });

  it("narrows quality to an interval when one is named", async () => {
    const services = stubServices();

    await runEntry("FPT QLTY --interval W1", services);

    expect(services.quality).toHaveBeenCalledWith(
      FPT.instrumentId,
      { interval: "W1" },
      undefined,
    );
  });

  it("scores the window the caller names rather than the server's recent default", async () => {
    // Without this, a deployment holding only a 2016 backfill scores zero
    // completeness against a window its data never reaches.
    const services = stubServices();

    await runEntry("FPT QLTY --from 2016-01-01 --to 2016-12-31", services);

    expect(services.quality).toHaveBeenCalledWith(
      FPT.instrumentId,
      { from: "2016-01-01", to: "2016-12-31" },
      undefined,
    );
  });

  it.each(["--from yesterday", "--to 2016", "--from 27/05/2016"])(
    "refuses %s instead of scoring some other window",
    async (option) => {
      const services = stubServices();

      const record = (await runEntry(`FPT QLTY ${option}`, services)).record;

      expect(record.kind).toBe("error");
      expect(services.quality).not.toHaveBeenCalled();
    },
  );

  it("refuses a date that is well-formed but not real", async () => {
    const services = stubServices();

    const record = (await runEntry("FPT QLTY --from 2016-02-31", services)).record;

    expect(record.kind).toBe("error");
    expect(services.quality).not.toHaveBeenCalled();
  });

  it("lists ingestion runs", async () => {
    const record = (await runEntry("FPT ING", stubServices())).record;

    expect(record).toMatchObject({ kind: "ingestion", runs: [] });
  });
});

describe("runEntry — failure", () => {
  it("answers an unreachable API with a record, not a throw", async () => {
    const services = stubServices({
      health: vi.fn(async () => {
        throw new Error("Failed to fetch");
      }),
    });

    const record = (await runEntry("health", services)).record;

    expect(record).toMatchObject({ kind: "error", message: "Failed to fetch" });
    expect(record).toMatchObject({ hint: expect.stringContaining("health") });
  });

  it("reports an abandoned command as cancelled rather than as a fault", async () => {
    const services = stubServices({
      bars: vi.fn(async () => {
        throw new DOMException("The operation was aborted.", "AbortError");
      }),
    });

    const record = (await runEntry("FPT GP", services)).record;

    expect(record).toMatchObject({ kind: "error", message: "Cancelled." });
  });

  it("passes the abort signal to the service it calls", async () => {
    const services = stubServices();
    const controller = new AbortController();

    await runEntry("FPT GP", services, controller.signal);

    expect(services.resolve).toHaveBeenCalledWith("FPT", controller.signal);
    expect(services.bars).toHaveBeenCalledWith(FPT.instrumentId, {}, controller.signal);
  });
});
