import { fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import type { Instrument } from "../../types/instrument";
import type { Bar, BarSeries } from "../../types/marketData";
import { PriceChart } from "./PriceChart";

const FPT: Instrument = {
  instrumentId: "11111111-1111-1111-1111-111111111111",
  ticker: "FPT",
  name: "FPT Corporation",
  assetType: "Equity",
  exchange: "HOSE",
  currency: "VND",
  status: "Listed",
};

function bar(day: string, open: number, close: number, overrides: Partial<Bar> = {}): Bar {
  return {
    openedAtUtc: `${day}T02:00:00Z`,
    open,
    high: Math.max(open, close) + 500,
    low: Math.min(open, close) - 500,
    close,
    volume: 1_000_000,
    turnover: null,
    source: "test",
    revision: 1,
    priceFactor: 1,
    shareFactor: 1,
    ...overrides,
  };
}

/** Four sessions: up, down, up, flat. */
const BARS: readonly Bar[] = [
  bar("2016-05-24", 120_000, 123_400),
  bar("2016-05-25", 123_400, 121_000),
  bar("2016-05-26", 121_000, 124_000),
  bar("2016-05-27", 124_000, 124_000, { volume: 2_500_000 }),
];

function series(bars: readonly Bar[], overrides: Partial<BarSeries> = {}): BarSeries {
  return {
    instrumentId: FPT.instrumentId,
    interval: "D1",
    adjusted: false,
    adjustedAtSource: false,
    adjustedBars: 0,
    count: bars.length,
    limit: 40,
    knownAsOf: null,
    announcementPolicy: "permissive",
    adjustmentsApplied: 0,
    adjustmentsWithheld: 0,
    bars,
    ...overrides,
  };
}

function chart() {
  return screen.getByRole("img");
}

function readout() {
  return document.querySelector<HTMLElement>(".chart__readout")!;
}

describe("PriceChart — what is drawn", () => {
  it("draws candlesticks by default, telling up from down by fill as well as colour", () => {
    const { container } = render(<PriceChart instrument={FPT} series={series(BARS)} knownAsOf={null} />);

    const candles = container.querySelectorAll("[data-direction]");
    expect(candles).toHaveLength(4);
    expect([...candles].map((candle) => candle.getAttribute("data-direction"))).toEqual([
      "up",
      "down",
      "up",
      "flat",
    ]);
    expect(container.querySelector(".chart__line")).toBeNull();
    expect(screen.getByText(/hollow body closed above its open, filled closed below/)).toBeInTheDocument();
  });

  it("draws a close line instead when asked", () => {
    const { container } = render(
      <PriceChart instrument={FPT} series={series(BARS)} knownAsOf={null} style="line" />,
    );

    const line = container.querySelector(".chart__line");
    expect(line).not.toBeNull();
    expect(line?.getAttribute("d")?.match(/[ML]/g)).toHaveLength(4);
    expect(container.querySelectorAll("[data-direction]")).toHaveLength(0);
    expect(screen.queryByText(/hollow body/)).not.toBeInTheDocument();
  });

  it("puts a volume bar under every session", () => {
    const { container } = render(<PriceChart instrument={FPT} series={series(BARS)} knownAsOf={null} />);

    expect(container.querySelectorAll(".chart__vol")).toHaveLength(4);
  });

  it("says so when the window reported no volume, rather than drawing an empty pane", () => {
    const silent = BARS.map((each) => ({ ...each, volume: 0 }));
    render(<PriceChart instrument={FPT} series={series(silent)} knownAsOf={null} />);

    expect(screen.getByText("No volume reported for this window.")).toBeInTheDocument();
  });

  it("labels the price axis in whole đồng with thousands separators", () => {
    render(<PriceChart instrument={FPT} series={series(BARS)} knownAsOf={null} />);

    expect(screen.getByText("124,000", { selector: "text.chart__tick" })).toBeInTheDocument();
    expect(screen.getByText("120,000", { selector: "text.chart__tick" })).toBeInTheDocument();
  });

  it("summarises the chart for a screen reader", () => {
    render(<PriceChart instrument={FPT} series={series(BARS)} knownAsOf={null} />);

    expect(chart()).toHaveAccessibleName(
      "FPT D1 raw candlestick chart, 4 sessions from 2016-05-24 to 2016-05-27. " +
        "First close 123,400, last close 124,000, +0.49%.",
    );
  });

  it("draws a single session", () => {
    const { container } = render(
      <PriceChart instrument={FPT} series={series([BARS[0]!])} knownAsOf={null} style="line" />,
    );

    expect(container.querySelector(".chart__dot")).not.toBeNull();
    expect(readout()).toHaveTextContent("first session in window");
  });

  it("says there is nothing to draw for an empty series", () => {
    render(<PriceChart instrument={FPT} series={series([])} knownAsOf={null} />);

    expect(screen.getByText("No sessions to draw for FPT.")).toBeInTheDocument();
    expect(screen.queryByRole("img")).not.toBeInTheDocument();
  });

  it("fits the width its container measures", () => {
    // jsdom has no ResizeObserver; this stands in for one reporting 900px.
    class StubObserver {
      private readonly callback: ResizeObserverCallback;
      constructor(callback: ResizeObserverCallback) {
        this.callback = callback;
      }
      observe(target: Element) {
        this.callback(
          [{ target, contentRect: { width: 900 } } as unknown as ResizeObserverEntry],
          this as unknown as ResizeObserver,
        );
      }
      disconnect() {}
      unobserve() {}
    }
    vi.stubGlobal("ResizeObserver", StubObserver);

    render(<PriceChart instrument={FPT} series={series(BARS)} knownAsOf={null} />);

    expect(chart().getAttribute("viewBox")).toBe("0 0 900 272");
  });

  it("falls back to a fixed width where nothing can be measured", () => {
    render(<PriceChart instrument={FPT} series={series(BARS)} knownAsOf={null} />);

    expect(chart().getAttribute("viewBox")).toBe("0 0 640 272");
  });
});

describe("PriceChart — reading a session", () => {
  it("rests on the last session", () => {
    render(<PriceChart instrument={FPT} series={series(BARS)} knownAsOf={null} />);

    expect(readout()).toHaveAttribute("aria-live", "polite");
    expect(readout()).toHaveTextContent("2016-05-27");
    expect(readout()).toHaveTextContent("C 124,000");
    expect(readout()).toHaveTextContent("Vol 2,500,000");
    expect(readout()).toHaveTextContent("±0 (0.00%)");
  });

  it("steps through sessions from the keyboard", async () => {
    const user = userEvent.setup();
    const { container } = render(<PriceChart instrument={FPT} series={series(BARS)} knownAsOf={null} />);

    await user.tab();
    expect(chart()).toHaveFocus();
    expect(container.querySelector(".chart__crosshair")).not.toBeNull();

    await user.keyboard("{ArrowLeft}");
    expect(readout()).toHaveTextContent("2016-05-26");
    expect(readout()).toHaveTextContent("O 121,000");
    expect(readout()).toHaveTextContent("+3,000 (+2.48%)");

    await user.keyboard("{Home}");
    expect(readout()).toHaveTextContent("2016-05-24");
    expect(readout()).toHaveTextContent("first session in window");

    await user.keyboard("{ArrowRight}");
    expect(readout()).toHaveTextContent("2016-05-25");
    expect(readout()).toHaveTextContent("-2,400 (-1.94%)");

    await user.keyboard("{End}");
    expect(readout()).toHaveTextContent("2016-05-27");
  });

  it("lets go of the crosshair on Escape and on blur", async () => {
    const user = userEvent.setup();
    const { container } = render(<PriceChart instrument={FPT} series={series(BARS)} knownAsOf={null} />);

    await user.tab();
    await user.keyboard("{Home}{Escape}");
    expect(container.querySelector(".chart__crosshair")).toBeNull();
    expect(readout()).toHaveTextContent("2016-05-27");

    await user.keyboard("{Home}");
    await user.tab();
    expect(container.querySelector(".chart__crosshair")).toBeNull();
  });

  it("follows the pointer", () => {
    render(<PriceChart instrument={FPT} series={series(BARS)} knownAsOf={null} />);

    // Under jsdom the element has no layout, so client and viewBox
    // coordinates coincide: the plot starts at 4px and each of the four
    // sessions gets 140px.
    fireEvent.pointerMove(chart(), { clientX: 160 });
    expect(readout()).toHaveTextContent("2016-05-25");

    fireEvent.pointerLeave(chart());
    expect(readout()).toHaveTextContent("2016-05-27");
  });

  it("does not hand the click to the console's prompt", () => {
    const outer = vi.fn();
    render(
      <div onClick={outer}>
        <PriceChart instrument={FPT} series={series(BARS)} knownAsOf={null} />
      </div>,
    );

    fireEvent.click(chart());

    expect(outer).not.toHaveBeenCalled();
  });
});

describe("PriceChart — adjustments", () => {
  // A 1.2-for-1 entitlement going ex on 26 May: the two sessions before it
  // carry its factor on an adjusted read.
  const ADJUSTED: readonly Bar[] = [
    bar("2016-05-24", 39_000, 40_000, { priceFactor: 1 / 1.2, shareFactor: 1.2 }),
    bar("2016-05-25", 40_000, 40_500, { priceFactor: 1 / 1.2, shareFactor: 1.2 }),
    bar("2016-05-26", 40_200, 41_000),
    bar("2016-05-27", 41_000, 41_500),
  ];

  it("marks the ex-date, where an adjusted and a raw read part company", () => {
    const { container } = render(
      <PriceChart
        instrument={FPT}
        series={series(ADJUSTED, { adjusted: true, adjustedBars: 2, adjustmentsApplied: 1 })}
        knownAsOf={null}
      />,
    );

    const markers = container.querySelectorAll(".chart__exdate");
    expect(markers).toHaveLength(1);
    expect(markers[0]).toHaveAttribute("data-session", "2016-05-26");
    expect(markers[0]?.querySelector("title")).toHaveTextContent(
      "Ex-date 2016-05-26: price factor ×0.8333 before, ×1 from this session",
    );
    expect(chart()).toHaveAccessibleName(/1 ex-date inside the window\.$/);
    expect(screen.getByText(/▲ marks an ex-date/)).toBeInTheDocument();
  });

  it("says in the readout when a session was rescaled, and on the ex-date that it is one", async () => {
    const user = userEvent.setup();
    render(
      <PriceChart
        instrument={FPT}
        series={series(ADJUSTED, { adjusted: true, adjustedBars: 2, adjustmentsApplied: 1 })}
        knownAsOf={null}
      />,
    );

    await user.tab();
    await user.keyboard("{ArrowLeft}");
    expect(readout()).toHaveTextContent("▲ ex-date: the price factor changes here");
    expect(readout()).not.toHaveTextContent("adjusted ×");

    await user.keyboard("{ArrowLeft}");
    expect(readout()).toHaveTextContent("adjusted ×0.8333 — printed close 48,600");
    expect(readout()).not.toHaveTextContent("ex-date");
  });

  it("marks nothing on a raw read", () => {
    const raw = ADJUSTED.map((each) => ({ ...each, priceFactor: 1, shareFactor: 1 }));
    const { container } = render(<PriceChart instrument={FPT} series={series(raw)} knownAsOf={null} />);

    expect(container.querySelectorAll(".chart__exdate")).toHaveLength(0);
  });
});

describe("PriceChart — the caption says which series this is", () => {
  function caption() {
    return document.querySelector<HTMLElement>("figcaption")!;
  }

  it("on a raw read", () => {
    render(<PriceChart instrument={FPT} series={series(BARS)} knownAsOf={null} />);

    expect(caption()).toHaveTextContent("FPT · D1 · prices in VND");
    expect(caption()).toHaveTextContent("Raw — exactly what the market printed");
    expect(caption()).not.toHaveTextContent("Point-in-time");
    expect(chart()).toHaveAttribute("aria-describedby", caption().id);
  });

  it("on a read adjusted by this system", () => {
    render(
      <PriceChart
        instrument={FPT}
        series={series(BARS, { adjusted: true, adjustedBars: 3, adjustmentsApplied: 1 })}
        knownAsOf={null}
      />,
    );

    expect(caption()).toHaveTextContent(
      "Adjusted on read — 3 bars rescaled from 1 recorded corporate action.",
    );
    expect(caption()).toHaveTextContent("Read as of now");
  });

  it("on a feed that was adjusted at source", () => {
    render(
      <PriceChart
        instrument={FPT}
        series={series(BARS, { adjusted: true, adjustedAtSource: true })}
        knownAsOf={null}
      />,
    );

    expect(caption()).toHaveTextContent("Adjusted at source. This system applied nothing");
  });

  it("on a point-in-time read, with its cut and announcement policy", () => {
    render(
      <PriceChart
        instrument={FPT}
        series={series(BARS, { adjusted: true, announcementPolicy: "strict" })}
        knownAsOf="2016-05-27"
      />,
    );

    expect(caption()).toHaveTextContent("Point-in-time as of 2016-05-27; announcement policy strict");
  });
});
