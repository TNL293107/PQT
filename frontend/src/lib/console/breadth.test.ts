import { describe, expect, it, vi } from "vitest";
import { UniverseApiError } from "../api/universes";
import { BREADTH } from "./breadth.fixture";
import { completeEntry, parseEntry } from "./entry";
import { runEntry, type ConsoleServices } from "./run";

/** Services that answer the universe verbs and fail anything else loudly. */
function stubServices(overrides: Partial<ConsoleServices> = {}): ConsoleServices {
  return {
    resolve: vi.fn(async () => {
      throw new Error("BREADTH must not resolve a security.");
    }),
    universes: vi.fn(async () => ({ asOf: "2026-10-11", count: 0, universes: [] })),
    breadth: vi.fn(async () => BREADTH),
    ...overrides,
  } as unknown as ConsoleServices;
}

describe("parseEntry — the universe verbs", () => {
  it("reads BREADTH as a verb that takes the universe as its argument", () => {
    const entry = parseEntry("breadth vn30 --on 2026-09-30");

    expect(entry.verb).toBe("BREADTH");
    expect(entry.securityQuery).toBe("vn30");
    expect(entry.functionCode).toBeNull();
    expect(entry.options).toEqual({ on: "2026-09-30" });
  });

  it("reads UNIVERSES as a bare verb", () => {
    const entry = parseEntry("universes");

    expect(entry.verb).toBe("UNIVERSES");
    expect(entry.securityQuery).toBe("");
  });

  it("completes the verbs from their first letters", () => {
    expect(completeEntry("brea")).toBe("BREADTH");
    expect(completeEntry("univ")).toBe("UNIVERSES");
  });
});

describe("runEntry — BREADTH", () => {
  it("asks the server for the named universe and session", async () => {
    const services = stubServices();

    const record = (await runEntry("breadth vn30 --on 2026-09-30", services)).record;

    expect(record).toEqual({ kind: "breadth", breadth: BREADTH });
    expect(services.breadth).toHaveBeenCalledWith("VN30", "2026-09-30", undefined, "Universe");
    expect(services.resolve).not.toHaveBeenCalled();
  });

  it("asks for a venue's breadth with --venue", async () => {
    const services = stubServices();

    await runEntry("breadth --venue hose --on 2026-09-30", services);

    expect(services.breadth).toHaveBeenCalledWith("HOSE", "2026-09-30", undefined, "Venue");
  });

  it("refuses a universe and a venue on one line", async () => {
    const services = stubServices();

    const record = (await runEntry("breadth VN30 --venue HOSE", services)).record;

    expect(record.kind).toBe("error");
    expect(services.breadth).not.toHaveBeenCalled();
  });

  it("leaves the session to the server when none is named", async () => {
    const services = stubServices();

    await runEntry("BREADTH VN30", services);

    expect(services.breadth).toHaveBeenCalledWith("VN30", undefined, undefined, "Universe");
  });

  it("refuses without a universe, and without asking", async () => {
    const services = stubServices();

    const record = (await runEntry("breadth", services)).record;

    expect(record).toMatchObject({ kind: "error", hint: expect.stringContaining("universes") });
    expect(services.breadth).not.toHaveBeenCalled();
  });

  it("refuses a session that is not a date, and without asking", async () => {
    const services = stubServices();

    const record = (await runEntry("breadth VN30 --on 2026-02-31", services)).record;

    expect(record).toMatchObject({ kind: "error" });
    expect(services.breadth).not.toHaveBeenCalled();
  });

  it("refuses something that cannot be a universe code", async () => {
    const services = stubServices();

    const record = (await runEntry("breadth vn 30", services)).record;

    expect(record).toMatchObject({ kind: "error" });
    expect(services.breadth).not.toHaveBeenCalled();
  });

  it("shows a refusal in the server's words, with what to try next", async () => {
    const services = stubServices({
      breadth: vi.fn(async () => {
        throw new UniverseApiError(
          "2026-10-03 is not a trading day on HOSE.",
          422,
          "NotATradingDay",
        );
      }),
    });

    const record = (await runEntry("breadth VN30 --on 2026-10-03", services)).record;

    expect(record).toEqual({
      kind: "error",
      message: "2026-10-03 is not a trading day on HOSE.",
      hint: "Name a trading session with --on yyyy-MM-dd.",
    });
  });

  it("points an unknown universe at the listing", async () => {
    const services = stubServices({
      breadth: vi.fn(async () => {
        throw new UniverseApiError("No universe is defined under the code NOPE.", 404, "UnknownUniverse");
      }),
    });

    const record = (await runEntry("breadth NOPE", services)).record;

    expect(record).toMatchObject({ kind: "error", hint: expect.stringContaining("'universes'") });
  });
});

describe("runEntry — UNIVERSES", () => {
  it("lists the universes from the server", async () => {
    const services = stubServices();

    const record = (await runEntry("universes", services)).record;

    expect(record).toEqual({
      kind: "universes",
      listing: { asOf: "2026-10-11", count: 0, universes: [] },
    });
  });
});
