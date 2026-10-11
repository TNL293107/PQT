import { describe, expect, it } from "vitest";
import type { Bar } from "../../types/marketData";
import { CHART_HEIGHT, chartLayout, MIN_WIDTH } from "./layout";

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

describe("chartLayout", () => {
  it("keeps a fixed height whatever the width, so nothing reflows", () => {
    expect(chartLayout([bar("2016-05-27", 1)], "candles", 300).height).toBe(CHART_HEIGHT);
    expect(chartLayout([bar("2016-05-27", 1)], "candles", 1400).height).toBe(CHART_HEIGHT);
  });

  it("puts the volume pane under the price pane, on the same sessions", () => {
    const layout = chartLayout([bar("2016-05-26", 1), bar("2016-05-27", 2)], "candles", 640);

    expect(layout.plot.volumeTop).toBeGreaterThan(layout.plot.priceBottom);
    expect(layout.plot.volumeBottom).toBeLessThan(CHART_HEIGHT);
    expect(layout.band.count).toBe(2);
  });

  it("clamps a width too narrow to draw in", () => {
    expect(chartLayout([], "candles", 50).width).toBe(MIN_WIDTH);
  });

  it("lays out an empty series without failing", () => {
    const layout = chartLayout([], "candles", 640);

    expect(layout.band.count).toBe(0);
    expect(layout.dates.ticks).toEqual([]);
    expect(layout.exDates).toEqual([]);
  });

  it("draws a one-session series well inside the plot, on a padded axis", () => {
    const layout = chartLayout([bar("2016-05-27", 123_400)], "candles", 640);
    const quarter = (layout.plot.priceBottom - layout.plot.top) / 4;
    const y = layout.priceY(123_400);

    // Not necessarily the exact middle: the axis widens to round ticks.
    expect(y).toBeGreaterThan(layout.plot.top + quarter);
    expect(y).toBeLessThan(layout.plot.priceBottom - quarter);
    expect(layout.bodyWidth).toBeGreaterThan(1);
  });

  it("draws a flat series as a level line, not a cliff", () => {
    const layout = chartLayout(
      [bar("2016-05-26", 50_000), bar("2016-05-27", 50_000), bar("2016-05-30", 50_000)],
      "line",
      640,
    );
    const y = layout.priceY(50_000);

    expect(y).toBeGreaterThan(layout.plot.top);
    expect(y).toBeLessThan(layout.plot.priceBottom);
  });

  it("keeps a volume axis when nothing traded", () => {
    const layout = chartLayout(
      [bar("2016-05-26", 1, { volume: 0 }), bar("2016-05-27", 2, { volume: 0 })],
      "candles",
      640,
    );

    expect(layout.maxVolume).toBe(0);
    expect(layout.volume.ticks).toEqual([0]);
    expect(layout.volumeY(0)).toBe(layout.plot.volumeBottom);
  });

  it("narrows candle bodies as sessions crowd together, never below a pixel", () => {
    const few = chartLayout([bar("2016-05-26", 1), bar("2016-05-27", 2)], "candles", 640);
    const many = chartLayout(
      Array.from({ length: 2000 }, (_, index) => bar(`2016-01-${String((index % 28) + 1).padStart(2, "0")}`, 1)),
      "candles",
      640,
    );

    expect(few.bodyWidth).toBeLessThanOrEqual(14);
    expect(many.bodyWidth).toBe(1);
  });
});
