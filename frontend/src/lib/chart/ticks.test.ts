import { describe, expect, it } from "vitest";
import { dateAxis, niceStep, priceAxis, volumeAxis } from "./ticks";

describe("niceStep", () => {
  it("rounds to one, two or five times a power of ten", () => {
    expect(niceStep(10_000, 5)).toBe(2000);
    expect(niceStep(2_468, 5)).toBe(500);
    expect(niceStep(7, 5)).toBe(2);
    expect(niceStep(0.3, 5)).toBeCloseTo(0.1);
  });

  it("falls back to one for a span that is not positive", () => {
    expect(niceStep(0, 5)).toBe(1);
    expect(niceStep(-3, 5)).toBe(1);
    expect(niceStep(Number.NaN, 5)).toBe(1);
  });
});

describe("priceAxis", () => {
  it("lands on round đồng inside a domain with a little headroom", () => {
    const axis = priceAxis(118_300, 127_900);

    expect(axis.step).toBe(2000);
    expect(axis.domain).toEqual([117_820, 128_380]);
    expect(axis.ticks).toEqual([118_000, 120_000, 122_000, 124_000, 126_000, 128_000]);
  });

  it("does not stretch the domain to the next round level below the data", () => {
    // 79,800 on a 20,000 step would otherwise draw from 60,000, leaving a
    // quarter of the pane empty.
    const axis = priceAxis(79_800, 132_300);

    expect(axis.domain[0]).toBeGreaterThan(75_000);
    expect(axis.ticks[0]).toBe(80_000);
  });

  it("pads a flat series instead of stretching it across the full height", () => {
    const axis = priceAxis(123_400, 123_400);

    expect(axis.domain[0]).toBeLessThan(123_400);
    expect(axis.domain[1]).toBeGreaterThan(123_400);
    // About a per cent either side — a level line, not a cliff.
    expect(axis.domain[1] - axis.domain[0]).toBeLessThan(123_400 * 0.05);
  });

  it("pads a flat series at zero", () => {
    expect(priceAxis(0, 0).domain).toEqual([-1, 1]);
  });

  it("does not drop the last tick to floating-point drift", () => {
    const axis = priceAxis(0, 1);

    expect(axis.ticks[axis.ticks.length - 1]).toBe(1);
    expect(axis.ticks).toEqual([0, 0.2, 0.4, 0.6, 0.8, 1]);
  });

  it("answers something drawable for a domain that is not a number", () => {
    expect(priceAxis(Number.NaN, 5)).toEqual({ domain: [0, 1], ticks: [0, 1], step: 1 });
  });
});

describe("volumeAxis", () => {
  it("starts at zero and tops out at the next round level", () => {
    const axis = volumeAxis(3_450_000);

    expect(axis.domain).toEqual([0, 4_000_000]);
    expect(axis.ticks).toEqual([0, 2_000_000, 4_000_000]);
  });

  it("still has an axis when nothing traded", () => {
    expect(volumeAxis(0)).toEqual({ domain: [0, 1], ticks: [0], step: 1 });
  });
});

/** Weekday sessions from `start`, as "YYYY-MM-DD". */
function sessions(start: string, count: number): string[] {
  const days: string[] = [];
  const cursor = new Date(`${start}T00:00:00Z`);

  while (days.length < count) {
    const weekday = cursor.getUTCDay();
    if (weekday !== 0 && weekday !== 6) {
      days.push(cursor.toISOString().slice(0, 10));
    }
    cursor.setUTCDate(cursor.getUTCDate() + 1);
  }

  return days;
}

describe("dateAxis", () => {
  it("labels a short window by session", () => {
    const axis = dateAxis(sessions("2016-05-02", 20), 5);

    expect(axis.unit).toBe("day");
    expect(axis.ticks.length).toBeLessThanOrEqual(5);
    expect(axis.ticks[0]).toEqual({ index: 0, label: "2 May" });
  });

  it("labels a year by month, with the year on January", () => {
    const days = sessions("2016-07-01", 260);
    const axis = dateAxis(days, 14);

    expect(axis.unit).toBe("month");
    const labels = axis.ticks.map((tick) => tick.label);
    expect(labels).toContain("Aug");
    expect(labels).toContain("2017");
    expect(labels).not.toContain("Jan");
    // Each tick is the first session of its month.
    for (const tick of axis.ticks) {
      expect(days[tick.index]!.slice(5, 7)).not.toBe(days[tick.index - 1]!.slice(5, 7));
    }
  });

  it("coarsens to quarters when months do not fit", () => {
    const axis = dateAxis(sessions("2016-01-04", 260), 4);

    expect(axis.unit).toBe("month");
    expect(axis.ticks.map((tick) => tick.label)).toEqual(["Apr", "Jul", "Oct"]);
  });

  it("labels a decade by year", () => {
    const axis = dateAxis(sessions("2010-03-01", 2600), 8);

    expect(axis.unit).toBe("year");
    expect(axis.ticks.every((tick) => /^\d{4}$/.test(tick.label))).toBe(true);
    expect(axis.ticks.length).toBeLessThanOrEqual(8);
  });

  it("finds a period's tick on the next session when a data hole spans its start", () => {
    const days = ["2016-03-30", "2016-03-31", "2016-04-15", "2016-05-02", "2016-05-03"];

    expect(dateAxis(days, 10).ticks).toEqual([
      { index: 2, label: "Apr" },
      { index: 3, label: "May" },
    ]);
  });

  it("has nothing to label in an empty series", () => {
    expect(dateAxis([], 5)).toEqual({ unit: "day", ticks: [] });
  });

  it("labels a single session", () => {
    expect(dateAxis(["2016-05-27"], 5).ticks).toEqual([{ index: 0, label: "27 May" }]);
  });

  it("thins rather than overprints when even decades do not fit", () => {
    const axis = dateAxis(sessions("1960-01-04", 16_000), 1);

    expect(axis.ticks.length).toBeLessThanOrEqual(1);
  });
});
