/**
 * Axis ticks for the price chart.
 *
 * Price ticks land on round numbers — 1, 2 or 5 times a power of ten — because
 * a gridline at 123,457 tells the reader nothing a gridline at 123,000 would
 * not, and makes them do arithmetic to read every other level.
 *
 * Date ticks land on the first session of a calendar period, never on a fixed
 * count of sessions, because "the first session of March" is a place a reader
 * can find again and "the 37th session" is not.
 */

import { stepDecimals } from "./format";

/** An axis over a continuous value: its extent and where the gridlines go. */
export interface ValueAxis {
  /** The extent drawn: the data, with headroom. */
  readonly domain: readonly [number, number];
  readonly ticks: readonly number[];
  readonly step: number;
}

/**
 * The round step that divides a span into about `targetCount` parts.
 *
 * @returns 1, 2 or 5 times a power of ten; 1 for a span that is not positive.
 */
export function niceStep(span: number, targetCount: number): number {
  if (!(span > 0) || !Number.isFinite(span)) {
    return 1;
  }

  const raw = span / Math.max(1, targetCount);
  const magnitude = 10 ** Math.floor(Math.log10(raw));
  const residual = raw / magnitude;

  let nice: number;
  if (residual <= 1) nice = 1;
  else if (residual <= 2) nice = 2;
  else if (residual <= 5) nice = 5;
  else nice = 10;

  return nice * magnitude;
}

/** How far a flat series is padded on each side, as a fraction of its level. */
const FLAT_PADDING = 0.01;

/** Headroom above and below the data, as a fraction of its span. */
const EDGE_PADDING = 0.05;

/**
 * Ticks for a price axis spanning `low` to `high`.
 *
 * The domain is the data plus a little headroom, and the ticks are the round
 * levels that fall inside it. Widening the domain out to the next round level
 * instead would leave a quarter of the pane empty whenever the low sat just
 * under one — 79,800 on a 20,000 step draws down to 60,000 — and squash the
 * series into what is left.
 *
 * A flat series is padded by one per cent either side. Drawing it across the
 * full height would turn a price that never moved into a chart that looks like
 * it did.
 */
export function priceAxis(low: number, high: number, targetCount = 5): ValueAxis {
  if (!Number.isFinite(low) || !Number.isFinite(high)) {
    return { domain: [0, 1], ticks: [0, 1], step: 1 };
  }

  let lo = Math.min(low, high);
  let hi = Math.max(low, high);

  if (hi === lo) {
    const pad = lo === 0 ? 1 : Math.abs(lo) * FLAT_PADDING;
    lo -= pad;
    hi += pad;
  } else {
    const pad = (hi - lo) * EDGE_PADDING;
    lo -= pad;
    hi += pad;
  }

  // Stepped on the padded span, which differs from the data's by a tenth: the
  // headroom is there to keep candles off the edge, not to change the levels.
  const step = niceStep((hi - lo) / (1 + 2 * EDGE_PADDING), targetCount);
  const decimals = stepDecimals(step);
  const first = Math.ceil(lo / step);
  const last = Math.floor(hi / step);

  // Counted, not accumulated: adding 0.1 ten times does not reach 1.0, and the
  // last gridline would silently go missing.
  const ticks: number[] = [];
  for (let multiple = first; multiple <= last; multiple += 1) {
    ticks.push(round(multiple * step, decimals));
  }

  return { domain: [lo, hi], ticks, step };
}

/**
 * Ticks for a volume axis, which always starts at zero and tops out at the
 * next round level above the largest session.
 *
 * A window with no volume at all — an index, or a feed that never reported it —
 * still gets an axis, with a single zero tick, so the pane reads as "nothing
 * traded" rather than as a pane that failed to draw.
 */
export function volumeAxis(max: number, targetCount = 2): ValueAxis {
  if (!(max > 0) || !Number.isFinite(max)) {
    return { domain: [0, 1], ticks: [0], step: 1 };
  }

  const step = niceStep(max, targetCount);
  const decimals = stepDecimals(step);
  const count = Math.ceil(max / step);
  const ticks: number[] = [];
  for (let multiple = 0; multiple <= count; multiple += 1) {
    ticks.push(round(multiple * step, decimals));
  }

  return { domain: [0, ticks[ticks.length - 1]!], ticks, step };
}

function round(value: number, decimals: number): number {
  const rounded = Number(value.toFixed(decimals));
  // -0 prints as "0" but compares and serialises oddly; there is no negative
  // zero price.
  return rounded === 0 ? 0 : rounded;
}

/** A labelled position on the date axis. */
export interface DateTick {
  /** The session the tick sits on. */
  readonly index: number;
  readonly label: string;
}

/** What a date axis counts in. */
export type DateUnit = "day" | "month" | "year";

/** The ticks a date axis draws, and the period they mark. */
export interface DateAxis {
  readonly unit: DateUnit;
  readonly ticks: readonly DateTick[];
}

const MONTHS = [
  "Jan",
  "Feb",
  "Mar",
  "Apr",
  "May",
  "Jun",
  "Jul",
  "Aug",
  "Sep",
  "Oct",
  "Nov",
  "Dec",
] as const;

interface Period {
  readonly unit: "month" | "year";
  readonly step: number;
}

/**
 * Coarsening order. Quarters and half-years before whole years, so a two-year
 * window reads in quarters rather than jumping straight to two labels.
 */
const PERIODS: readonly Period[] = [
  { unit: "month", step: 1 },
  { unit: "month", step: 2 },
  { unit: "month", step: 3 },
  { unit: "month", step: 6 },
  { unit: "year", step: 1 },
  { unit: "year", step: 2 },
  { unit: "year", step: 5 },
  { unit: "year", step: 10 },
];

/**
 * Chooses where the date labels go.
 *
 * Takes the finest calendar period whose boundaries fit in `maxTicks`. A window
 * too short to cross two month boundaries is labelled by session instead,
 * every few sessions, so a forty-session read still has dates under it.
 *
 * Each label is as short as it can be while still placing itself: a month is
 * "Mar", except January, which is labelled with its year — the reader learns
 * the year from the January either side and the month from everything else.
 *
 * @param days Each session's calendar day, "YYYY-MM-DD", in order.
 * @param maxTicks The most labels the width has room for.
 */
export function dateAxis(days: readonly string[], maxTicks: number): DateAxis {
  const limit = Math.max(1, Math.floor(maxTicks));

  if (days.length === 0) {
    return { unit: "day", ticks: [] };
  }

  const monthly = boundaries(days, PERIODS[0]!);

  if (monthly.length < 2) {
    return { unit: "day", ticks: sessionTicks(days, limit) };
  }

  for (const period of PERIODS) {
    const indices = period === PERIODS[0] ? monthly : boundaries(days, period);

    if (indices.length <= limit) {
      return { unit: period.unit, ticks: indices.map((index) => label(days, index, period.unit)) };
    }
  }

  // Wider than a decade per label still does not fit: thin the coarsest
  // period evenly rather than overprint labels on each other.
  const coarsest = PERIODS[PERIODS.length - 1]!;
  const indices = boundaries(days, coarsest);
  const every = Math.ceil(indices.length / limit);
  return {
    unit: coarsest.unit,
    ticks: indices
      .filter((_, position) => position % every === 0)
      .map((index) => label(days, index, coarsest.unit)),
  };
}

/**
 * The sessions that open a new period.
 *
 * Bucketed rather than matched month by month, so a data hole spanning the
 * first of a quarter still yields that quarter's tick on the next session
 * that exists.
 */
function boundaries(days: readonly string[], period: Period): number[] {
  const indices: number[] = [];
  let previous: number | null = null;

  days.forEach((day, index) => {
    const bucket = bucketOf(day, period);

    if (previous !== null && bucket !== previous) {
      indices.push(index);
    }

    previous = bucket;
  });

  return indices;
}

function bucketOf(day: string, period: Period): number {
  const year = Number(day.slice(0, 4));
  const month = Number(day.slice(5, 7)) - 1;

  return period.unit === "year"
    ? Math.floor(year / period.step)
    : Math.floor((year * 12 + month) / period.step);
}

function sessionTicks(days: readonly string[], limit: number): DateTick[] {
  const every = Math.max(1, Math.ceil(days.length / limit));
  const ticks: DateTick[] = [];

  for (let index = 0; index < days.length; index += every) {
    const day = days[index]!;
    ticks.push({ index, label: `${Number(day.slice(8, 10))} ${monthName(day)}` });
  }

  return ticks;
}

function label(days: readonly string[], index: number, unit: "month" | "year"): DateTick {
  const day = days[index]!;
  const year = day.slice(0, 4);

  if (unit === "year" || day.slice(5, 7) === "01") {
    return { index, label: year };
  }

  return { index, label: monthName(day) };
}

function monthName(day: string): string {
  return MONTHS[Number(day.slice(5, 7)) - 1] ?? day.slice(5, 7);
}
