/**
 * Types mirroring `GET /instruments/{id}/ratios`: an issuer's point-in-time
 * ratios for a session (ADR-030).
 *
 * Every value is decided by the server, from the statements known before the
 * session opened, its raw close and the actions announced by then. A ratio
 * the server could not compute honestly arrives as `value: null` with the
 * reason in `absent`; the console prints the reason, never a zero.
 */

/** One ratio: a value, or why there is none. */
export interface Ratio {
  /** `close`, `shares`, `netProfitTtm`, `epsTtm`, `bookValuePerShare`,
   * `priceToEarnings`, `priceToBook`, `returnOnEquity` or `dividendYield`. */
  readonly name: string;
  readonly value: number | null;
  readonly absent: string | null;
}

/** An issuer's ratios at a session, and what they were built from. */
export interface FundamentalRatios {
  readonly instrumentId: string;
  readonly session: string;
  readonly knownAsOfUtc: string;
  /** `Consolidated`, `Separate`, or null when no statement was known. */
  readonly scope: string | null;
  /** The quarter of the balance sheet used, such as `2024Q2`. */
  readonly balanceSheet: string | null;
  /** The four quarters summed for the trailing profit, oldest first. */
  readonly profitQuarters: readonly string[];
  readonly actionsApplied: number;
  readonly actionsWithheld: number;
  readonly ratios: readonly Ratio[];
}
