/**
 * What the price chart says about a series, decided before anything is drawn.
 *
 * Pure functions over the bars the API returned. Nothing here recomputes an
 * adjustment: the server applied the factors and states them on every bar,
 * and the chart only reads them back (ADR-026 — presentation only).
 */

import type { Bar, BarSeries } from "../../types/marketData";
import { formatChange, formatPercent, formatPrice, formatVolume } from "./format";

/** How the price pane is drawn. */
export type ChartStyle = "candles" | "line";

/** Which way a session moved from its own open. */
export type Direction = "up" | "down" | "flat";

/** The calendar day a bar belongs to, "YYYY-MM-DD". */
export function sessionDay(bar: Bar): string {
  return bar.openedAtUtc.slice(0, 10);
}

/** Whether a session closed above, below or at its open. */
export function directionOf(bar: Bar): Direction {
  if (bar.close > bar.open) return "up";
  if (bar.close < bar.open) return "down";
  return "flat";
}

/**
 * Whether two factors differ.
 *
 * With a tolerance, because the server multiplies stored ratios together and a
 * factor that is mathematically one can arrive as 0.9999999999999999. Marking
 * that as an ex-date would put a marker where nothing happened.
 */
export function factorsDiffer(a: number, b: number): boolean {
  return Math.abs(a - b) > 1e-9 * Math.max(1, Math.abs(a), Math.abs(b));
}

/** Whether this bar's prices were rescaled from what the market printed. */
export function isAdjustedBar(bar: Bar): boolean {
  return factorsDiffer(bar.priceFactor, 1);
}

/**
 * The sessions on which the price factor changes from the session before.
 *
 * That is an ex-date inside the window: every bar before it carries the
 * action's factor and every bar from it does not. It is exactly where an
 * adjusted and a raw read of the same series part company, so it is where the
 * reader most needs to know which one they are looking at.
 *
 * A raw read carries a factor of one throughout and so marks nothing — the
 * gap the action caused is visible on its own.
 */
export function exDateIndices(bars: readonly Bar[]): number[] {
  const indices: number[] = [];

  for (let index = 1; index < bars.length; index += 1) {
    if (factorsDiffer(bars[index - 1]!.priceFactor, bars[index]!.priceFactor)) {
      indices.push(index);
    }
  }

  return indices;
}

/** A move from the previous session's close. */
export interface Change {
  readonly absolute: number;

  /** Null when the previous close was zero and no percentage exists. */
  readonly percent: number | null;
}

/**
 * The move from the previous session's close.
 *
 * @returns Null for the first session in the window. Its previous close is
 * outside what was fetched, and inventing one from the open would report the
 * intraday move under the name of the daily change.
 */
export function changeFromPrevious(bars: readonly Bar[], index: number): Change | null {
  if (index <= 0 || index >= bars.length) {
    return null;
  }

  const previous = bars[index - 1]!.close;
  const absolute = bars[index]!.close - previous;

  return { absolute, percent: previous === 0 ? null : (absolute / previous) * 100 };
}

/**
 * The price extent the pane must show.
 *
 * Candles need the whole high–low range; a line draws only closes, and scaling
 * it to wicks it does not draw would squash the one thing it shows.
 */
export function priceExtent(bars: readonly Bar[], style: ChartStyle): { low: number; high: number } {
  if (bars.length === 0) {
    return { low: 0, high: 0 };
  }

  let low = Number.POSITIVE_INFINITY;
  let high = Number.NEGATIVE_INFINITY;

  for (const bar of bars) {
    const lo = style === "candles" ? Math.min(bar.low, bar.open, bar.close) : bar.close;
    const hi = style === "candles" ? Math.max(bar.high, bar.open, bar.close) : bar.close;
    low = Math.min(low, lo);
    high = Math.max(high, hi);
  }

  return { low, high };
}

/** The largest volume in the window, or zero. */
export function maxVolume(bars: readonly Bar[]): number {
  return bars.reduce((max, bar) => (bar.volume > max ? bar.volume : max), 0);
}

/**
 * One sentence for a screen reader, standing in for the picture.
 *
 * What a sighted reader takes from a glance: what this is, over when, where it
 * started and ended, and how many ex-dates the window crosses.
 */
export function chartSummary(
  ticker: string,
  series: BarSeries,
  style: ChartStyle,
): string {
  const { bars } = series;
  const kind = style === "candles" ? "candlestick" : "line";
  const reading = series.adjusted ? "adjusted" : "raw";

  if (bars.length === 0) {
    return `${ticker} ${series.interval} ${reading} ${kind} chart, no sessions.`;
  }

  const first = bars[0]!;
  const last = bars[bars.length - 1]!;
  const sessions = `${bars.length} session${bars.length === 1 ? "" : "s"}`;
  const range =
    bars.length === 1
      ? `on ${sessionDay(first)}`
      : `from ${sessionDay(first)} to ${sessionDay(last)}`;

  const parts = [`${ticker} ${series.interval} ${reading} ${kind} chart, ${sessions} ${range}.`];

  if (bars.length === 1) {
    parts.push(`Close ${formatPrice(last.close)}.`);
  } else {
    const change = first.close === 0 ? null : ((last.close - first.close) / first.close) * 100;
    parts.push(
      `First close ${formatPrice(first.close)}, last close ${formatPrice(last.close)}` +
        (change === null ? "." : `, ${formatPercent(change)}.`),
    );
  }

  const exDates = exDateIndices(bars).length;
  if (exDates > 0) {
    parts.push(`${exDates} ex-date${exDates === 1 ? "" : "s"} inside the window.`);
  }

  return parts.join(" ");
}

/**
 * The caption: which series this is, stated so nobody has to guess.
 *
 * Raw against adjusted is the first fact, because the two disagree on every
 * session before an ex-date. Who adjusted is the second — "adjusted" from a
 * feed and "adjusted" by this system are different claims, and adjusting a
 * source-adjusted series again would be wrong by every factor since. The cut
 * and the announcement policy follow when the read was point-in-time, because
 * that is when they change what was applied.
 *
 * @param knownAsOf The as-of the caller asked for, as typed; falls back to the
 * instant the server answered as of.
 */
export function describeSeries(series: BarSeries, knownAsOf: string | null): string[] {
  const sentences: string[] = [];
  const asOf = knownAsOf ?? series.knownAsOf;

  if (!series.adjusted) {
    sentences.push(
      "Raw — exactly what the market printed, with no factor applied. An ex-date shows as the gap it caused, not as a marker.",
    );
  } else if (series.adjustedAtSource) {
    sentences.push(
      "Adjusted at source. This system applied nothing: the feed had already rescaled it, and adjusting again would be wrong by the product of every factor since.",
    );
  } else {
    const bars = series.adjustedBars;
    const actions = series.adjustmentsApplied;
    sentences.push(
      `Adjusted on read — ${bars} bar${bars === 1 ? "" : "s"} rescaled from ` +
        `${actions} recorded corporate action${actions === 1 ? "" : "s"}.`,
    );
  }

  if (asOf !== null) {
    const policy =
      series.announcementPolicy === "strict"
        ? "strict: an action whose announcement date is unknown is left out"
        : "permissive: an action whose announcement date is unknown is applied";
    sentences.push(`Point-in-time as of ${asOf.slice(0, 10)}; announcement policy ${policy}.`);
  } else if (series.adjusted && !series.adjustedAtSource) {
    sentences.push("Read as of now — every action this system knows of is applied.");
  }

  return sentences;
}

/** The readout's facts for one session, in display order. */
export interface Readout {
  readonly day: string;
  readonly open: string;
  readonly high: string;
  readonly low: string;
  readonly close: string;
  readonly volume: string;

  /** "+900 (+2.00%)", or null for the first session in the window. */
  readonly change: string | null;
  readonly direction: "up" | "down" | "flat";

  /** The factor applied and the close as printed, when the bar was rescaled. */
  readonly adjustment: { readonly factor: number; readonly printedClose: string } | null;

  /** Set when the price factor changes on this session. */
  readonly exDate: boolean;
}

/** Everything the readout says about one session. */
export function readoutFor(
  bars: readonly Bar[],
  index: number,
  exDates: ReadonlySet<number>,
): Readout | null {
  const bar = bars[index];

  if (bar === undefined) {
    return null;
  }

  const change = changeFromPrevious(bars, index);
  const adjusted = isAdjustedBar(bar);

  return {
    day: sessionDay(bar),
    open: formatPrice(bar.open),
    high: formatPrice(bar.high),
    low: formatPrice(bar.low),
    close: formatPrice(bar.close),
    volume: formatVolume(bar.volume),
    change: change === null ? null : formatChange(change.absolute, change.percent),
    direction:
      change === null || change.absolute === 0 ? "flat" : change.absolute > 0 ? "up" : "down",
    adjustment:
      adjusted && bar.priceFactor !== 0
        ? { factor: bar.priceFactor, printedClose: formatPrice(bar.close / bar.priceFactor) }
        : null,
    exDate: exDates.has(index),
  };
}
