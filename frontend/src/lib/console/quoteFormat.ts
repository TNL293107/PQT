/**
 * How the console writes a price and a change. Shared by breadth and
 * watchlists, so the same quote reads the same in both.
 */

/** A fractional change as a signed percentage; "no data" when there is none. */
export function change(value: number | null): string {
  if (value === null) return "no data";
  const percent = value * 100;
  return `${percent > 0 ? "+" : ""}${percent.toFixed(2)}%`;
}

/** A price with grouping and up to four decimals; a dash when there is none. */
export function price(value: number | null): string {
  return value === null
    ? "—"
    : value.toLocaleString("en-US", { minimumFractionDigits: 0, maximumFractionDigits: 4 });
}
