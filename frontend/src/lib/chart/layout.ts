/**
 * The price chart's geometry, computed once per series and width.
 *
 * Kept out of the component so the arithmetic is tested as arithmetic, and so
 * moving the crosshair — which happens on every pointer event — never redoes
 * it.
 */

import type { Bar } from "../../types/marketData";
import { stepDecimals } from "./format";
import { linearScale, slotWidth, type Band, type Scale } from "./scale";
import { exDateIndices, maxVolume, priceExtent, sessionDay, type ChartStyle } from "./series";
import { dateAxis, priceAxis, volumeAxis, type DateAxis, type ValueAxis } from "./ticks";

/** Total height, fixed so the transcript never reflows as the chart measures itself. */
export const CHART_HEIGHT = 272;

/** The width assumed until one is measured — and always, under jsdom. */
export const FALLBACK_WIDTH = 640;

/** Narrower than this and the axis labels would overprint the candles. */
export const MIN_WIDTH = 240;

const MARGIN_TOP = 10;
const MARGIN_LEFT = 4;

/** Room for the price labels: "1,234,500" in the console's monospace face. */
const AXIS_COLUMN = 76;

const PRICE_HEIGHT = 180;
const PANE_GAP = 12;
const VOLUME_HEIGHT = 48;

// What is left of CHART_HEIGHT below the volume pane — 22px — holds the date
// labels.

/** The least horizontal room a date label is given. */
const MIN_DATE_SPACING = 72;

/** About this many price gridlines; the nice step decides the exact count. */
const PRICE_TICK_TARGET = 5;

/**
 * A candle body takes this share of its slot. The rest is the gap that keeps
 * neighbouring sessions apart; without it a dense window reads as one block.
 */
const BODY_SHARE = 0.68;
const MAX_BODY_WIDTH = 14;

/** The plot's edges, in pixels. */
export interface PlotBox {
  readonly left: number;
  readonly right: number;
  readonly top: number;
  readonly priceBottom: number;
  readonly volumeTop: number;
  readonly volumeBottom: number;
}

export interface ChartLayout {
  readonly width: number;
  readonly height: number;
  readonly plot: PlotBox;
  readonly band: Band;
  readonly bodyWidth: number;

  readonly price: ValueAxis;
  /** The fraction digits every price label uses, from the tick step. */
  readonly priceDecimals: number;
  readonly priceY: Scale;

  readonly volume: ValueAxis;
  readonly volumeY: Scale;
  readonly maxVolume: number;

  readonly dates: DateAxis;

  /** Sessions on which the price factor changes — the ex-dates in the window. */
  readonly exDates: readonly number[];
}

/**
 * Lays out a chart of `bars` at `width` pixels.
 *
 * @param width The container's width; clamped to {@link MIN_WIDTH}.
 */
export function chartLayout(bars: readonly Bar[], style: ChartStyle, width: number): ChartLayout {
  const chartWidth = Math.max(MIN_WIDTH, Math.round(width));

  const plot: PlotBox = {
    left: MARGIN_LEFT,
    right: chartWidth - AXIS_COLUMN,
    top: MARGIN_TOP,
    priceBottom: MARGIN_TOP + PRICE_HEIGHT,
    volumeTop: MARGIN_TOP + PRICE_HEIGHT + PANE_GAP,
    volumeBottom: MARGIN_TOP + PRICE_HEIGHT + PANE_GAP + VOLUME_HEIGHT,
  };

  const band: Band = { left: plot.left, width: plot.right - plot.left, count: bars.length };

  const extent = priceExtent(bars, style);
  const price = priceAxis(extent.low, extent.high, PRICE_TICK_TARGET);
  const priceY = linearScale(price.domain, [plot.priceBottom, plot.top]);

  const peak = maxVolume(bars);
  const volume = volumeAxis(peak);
  const volumeY = linearScale(volume.domain, [plot.volumeBottom, plot.volumeTop]);

  const dates = dateAxis(
    bars.map(sessionDay),
    Math.max(1, Math.floor(band.width / MIN_DATE_SPACING)),
  );

  return {
    width: chartWidth,
    height: CHART_HEIGHT,
    plot,
    band,
    bodyWidth: Math.max(1, Math.min(MAX_BODY_WIDTH, slotWidth(band) * BODY_SHARE)),
    price,
    priceDecimals: stepDecimals(price.step),
    priceY,
    volume,
    volumeY,
    maxVolume: peak,
    dates,
    exDates: exDateIndices(bars),
  };
}
