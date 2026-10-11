/**
 * Number and date formatting for the price chart.
 *
 * Vietnamese prices are quoted in whole đồng — 123,400, not 123.4 — so a price
 * is written with thousands separators and never rescaled to thousands. Some
 * feeds and boards do show "123.4" meaning thousands of đồng; reproducing that
 * here would make an adjusted price and a printed one differ by a factor of a
 * thousand whenever the two were read side by side.
 *
 * en-US separators throughout, matching every other table in the console. A
 * locale that swapped the comma and the full stop would turn 123,400 into a
 * number a reader could mistake for one hundred and twenty-three.
 */

const LOCALE = "en-US";

/** The most fractional digits a price is ever shown with. */
const MAX_PRICE_DECIMALS = 2;

const compactFormatter = new Intl.NumberFormat(LOCALE, {
  notation: "compact",
  maximumFractionDigits: 1,
});

/**
 * Writes a price.
 *
 * Adjusted prices are rarely whole đồng — a 1.2 split factor turns 45,000 into
 * 37,500 but 45,100 into 37,583.33 — so up to two decimals survive, and none
 * are padded onto a whole number.
 *
 * @param value The price in the instrument's currency.
 * @param decimals Exactly this many fraction digits, when given. An axis passes
 * the precision of its tick step so every label lines up.
 */
export function formatPrice(value: number, decimals?: number): string {
  if (!Number.isFinite(value)) {
    return "—";
  }

  return value.toLocaleString(LOCALE, {
    minimumFractionDigits: decimals ?? 0,
    maximumFractionDigits: decimals ?? MAX_PRICE_DECIMALS,
  });
}

/** Writes a share count in full, for the readout. */
export function formatVolume(value: number): string {
  if (!Number.isFinite(value)) {
    return "—";
  }

  return Math.round(value).toLocaleString(LOCALE);
}

/**
 * Writes a share count compactly, for an axis label: 12.3M, not 12,345,600.
 * The readout carries the exact figure.
 */
export function formatCompactVolume(value: number): string {
  if (!Number.isFinite(value)) {
    return "—";
  }

  return compactFormatter.format(value);
}

/**
 * Writes a signed change and its percentage: "+900 (+2.00%)".
 *
 * The sign is always written, because an unsigned 900 next to a price reads as
 * a level rather than a move.
 *
 * @param percent Null when the base was zero and no percentage exists.
 */
export function formatChange(absolute: number, percent: number | null): string {
  const sign = absolute > 0 ? "+" : absolute < 0 ? "-" : "±";
  const magnitude = formatPrice(Math.abs(absolute));

  if (percent === null) {
    return `${sign}${magnitude}`;
  }

  return `${sign}${magnitude} (${formatPercent(percent)})`;
}

/** Writes a signed percentage with two decimals: "+2.00%", "-0.45%". */
export function formatPercent(percent: number): string {
  if (!Number.isFinite(percent)) {
    return "—";
  }

  const rounded = percent.toFixed(2);

  // toFixed rounds -0.001 to "-0.00", which claims a direction the number does
  // not have.
  if (Number(rounded) === 0) {
    return "0.00%";
  }

  return `${percent > 0 ? "+" : ""}${rounded}%`;
}

/**
 * The fraction digits a tick step needs.
 *
 * A step of 0.05 needs two, a step of 500 needs none. Derived from the step
 * rather than from each tick so every label on an axis has the same precision.
 */
export function stepDecimals(step: number): number {
  if (!(step > 0) || !Number.isFinite(step) || step >= 1) {
    return 0;
  }

  return Math.min(MAX_PRICE_DECIMALS + 2, Math.max(0, Math.ceil(-Math.log10(step) - 1e-9)));
}

/**
 * Writes a price factor: "×0.8333".
 *
 * Four decimals, because factors are ratios like 1/1.2 whose meaning is lost at
 * two — 0.83 could be any of several different entitlements.
 */
export function formatFactor(factor: number): string {
  return `×${Number(factor.toFixed(4)).toString()}`;
}
