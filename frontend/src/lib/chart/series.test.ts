import { describe, expect, it } from "vitest";
import type { Bar, BarSeries } from "../../types/marketData";
import {
  changeFromPrevious,
  chartSummary,
  describeSeries,
  directionOf,
  exDateIndices,
  factorsDiffer,
  maxVolume,
  priceExtent,
  readoutFor,
} from "./series";

function bar(day: string, close: number, overrides: Partial<Bar> = {}): Bar {
  return {
    openedAtUtc: `${day}T02:00:00Z`,
    open: close,
    high: close,
    low: close,
    close,
    volume: 1000,
    turnover: null,
    source: "test",
    revision: 1,
    priceFactor: 1,
    shareFactor: 1,
    ...overrides,
  };
}

function series(bars: readonly Bar[], overrides: Partial<BarSeries> = {}): BarSeries {
  return {
    instrumentId: "1",
    interval: "D1",
    adjusted: false,
    adjustedAtSource: false,
    adjustedBars: 0,
    count: bars.length,
    limit: 100,
    knownAsOf: null,
    announcementPolicy: "permissive",
    adjustmentsApplied: 0,
    adjustmentsWithheld: 0,
    bars,
    ...overrides,
  };
}

describe("directionOf", () => {
  it("reads a session against its own open", () => {
    expect(directionOf(bar("2016-05-02", 110, { open: 100 }))).toBe("up");
    expect(directionOf(bar("2016-05-02", 90, { open: 100 }))).toBe("down");
    expect(directionOf(bar("2016-05-02", 100))).toBe("flat");
  });
});

describe("exDateIndices", () => {
  it("marks the first session the earlier factor no longer covers", () => {
    const bars = [
      bar("2016-05-25", 100, { priceFactor: 0.8 }),
      bar("2016-05-26", 101, { priceFactor: 0.8 }),
      bar("2016-05-27", 102),
      bar("2016-05-30", 103),
    ];

    expect(exDateIndices(bars)).toEqual([2]);
  });

  it("marks nothing on a raw read, where every factor is one", () => {
    expect(exDateIndices([bar("2016-05-26", 100), bar("2016-05-27", 80)])).toEqual([]);
  });

  it("ignores a factor that is one up to floating-point noise", () => {
    expect(factorsDiffer(1, 0.9999999999999999)).toBe(false);
    expect(
      exDateIndices([
        bar("2016-05-26", 100, { priceFactor: 0.9999999999999999 }),
        bar("2016-05-27", 100),
      ]),
    ).toEqual([]);
  });

  it("finds nothing in an empty or one-session window", () => {
    expect(exDateIndices([])).toEqual([]);
    expect(exDateIndices([bar("2016-05-27", 1)])).toEqual([]);
  });
});

describe("changeFromPrevious", () => {
  const bars = [bar("2016-05-26", 0), bar("2016-05-27", 100), bar("2016-05-30", 90)];

  it("measures against the previous session's close", () => {
    expect(changeFromPrevious(bars, 2)).toEqual({ absolute: -10, percent: -10 });
  });

  it("has no change for the first session, whose previous close was not fetched", () => {
    expect(changeFromPrevious(bars, 0)).toBeNull();
  });

  it("has no percentage from a zero close", () => {
    expect(changeFromPrevious(bars, 1)).toEqual({ absolute: 100, percent: null });
  });

  it("refuses an index outside the window", () => {
    expect(changeFromPrevious(bars, 3)).toBeNull();
  });
});

describe("priceExtent and maxVolume", () => {
  const bars = [
    bar("2016-05-26", 100, { high: 120, low: 95 }),
    bar("2016-05-27", 105, { high: 110, low: 80, volume: 0 }),
  ];

  it("spans the wicks for candles and only the closes for a line", () => {
    expect(priceExtent(bars, "candles")).toEqual({ low: 80, high: 120 });
    expect(priceExtent(bars, "line")).toEqual({ low: 100, high: 105 });
  });

  it("answers zero for an empty window", () => {
    expect(priceExtent([], "candles")).toEqual({ low: 0, high: 0 });
    expect(maxVolume([])).toBe(0);
  });

  it("finds the largest volume", () => {
    expect(maxVolume(bars)).toBe(1000);
  });
});

describe("chartSummary", () => {
  it("states what, when, where it started and ended, and the ex-dates crossed", () => {
    const bars = [
      bar("2016-05-26", 100_000, { priceFactor: 0.8 }),
      bar("2016-05-27", 110_000),
    ];

    expect(chartSummary("FPT", series(bars, { adjusted: true }), "candles")).toBe(
      "FPT D1 adjusted candlestick chart, 2 sessions from 2016-05-26 to 2016-05-27. " +
        "First close 100,000, last close 110,000, +10.00%. 1 ex-date inside the window.",
    );
  });

  it("describes a single session without inventing a change", () => {
    expect(chartSummary("FPT", series([bar("2016-05-27", 123_400)]), "line")).toBe(
      "FPT D1 raw line chart, 1 session on 2016-05-27. Close 123,400.",
    );
  });

  it("says when there is nothing to draw", () => {
    expect(chartSummary("FPT", series([]), "candles")).toBe(
      "FPT D1 raw candlestick chart, no sessions.",
    );
  });
});

describe("describeSeries — the caption", () => {
  it("says a raw read is what the market printed", () => {
    const [first, ...rest] = describeSeries(series([]), null);

    expect(first).toMatch(/^Raw — exactly what the market printed/);
    expect(rest).toEqual([]);
  });

  it("says who adjusted, and that this system applied nothing to a source-adjusted feed", () => {
    expect(describeSeries(series([], { adjusted: true, adjustedAtSource: true }), null)[0]).toMatch(
      /^Adjusted at source\. This system applied nothing/,
    );
  });

  it("counts what an adjustment on read rescaled, and says it reads as of now", () => {
    const sentences = describeSeries(
      series([], { adjusted: true, adjustedBars: 12, adjustmentsApplied: 2 }),
      null,
    );

    expect(sentences).toEqual([
      "Adjusted on read — 12 bars rescaled from 2 recorded corporate actions.",
      "Read as of now — every action this system knows of is applied.",
    ]);
  });

  it("states the cut and the announcement policy of a point-in-time read", () => {
    const strict = describeSeries(
      series([], { adjusted: true, announcementPolicy: "strict" }),
      "2016-05-27",
    );
    const permissive = describeSeries(series([], { adjusted: true }), "2016-05-27");

    expect(strict[1]).toBe(
      "Point-in-time as of 2016-05-27; announcement policy strict: an action whose announcement date is unknown is left out.",
    );
    expect(permissive[1]).toMatch(/policy permissive: .* is applied\.$/);
  });

  it("falls back to the instant the server answered as of", () => {
    const sentences = describeSeries(series([], { knownAsOf: "2016-05-27T00:00:00Z" }), null);

    expect(sentences[1]).toMatch(/^Point-in-time as of 2016-05-27;/);
  });
});

describe("readoutFor", () => {
  const bars = [
    bar("2016-05-26", 40_000, { open: 39_000, high: 41_000, low: 38_500, priceFactor: 1 / 1.2 }),
    bar("2016-05-27", 49_000, { open: 48_000, high: 49_500, low: 47_800, volume: 1_234_500 }),
  ];

  it("states the session, its prices, volume and change", () => {
    const readout = readoutFor(bars, 1, new Set([1]));

    expect(readout).toMatchObject({
      day: "2016-05-27",
      open: "48,000",
      high: "49,500",
      low: "47,800",
      close: "49,000",
      volume: "1,234,500",
      change: "+9,000 (+22.50%)",
      direction: "up",
      adjustment: null,
      exDate: true,
    });
  });

  it("says when a bar was rescaled, and what the market printed", () => {
    const readout = readoutFor(bars, 0, new Set());

    expect(readout?.adjustment).toEqual({ factor: 1 / 1.2, printedClose: "48,000" });
    expect(readout?.change).toBeNull();
    expect(readout?.exDate).toBe(false);
  });

  it("has nothing to say about a session outside the window", () => {
    expect(readoutFor(bars, 5, new Set())).toBeNull();
    expect(readoutFor([], 0, new Set())).toBeNull();
  });
});
