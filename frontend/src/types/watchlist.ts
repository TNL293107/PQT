/**
 * Types mirroring the JSON contract served by the API's watchlist endpoints.
 */

/** A watchlist without its entries. */
export interface WatchlistSummary {
  readonly name: string;
  readonly count: number;
  readonly createdAtUtc: string;
  readonly updatedAtUtc: string;
}

/** Every watchlist. */
export interface WatchlistList {
  readonly results: readonly WatchlistSummary[];
}

/** One instrument on a watchlist. */
export interface WatchlistItem {
  readonly instrumentId: string;
  readonly ticker: string;
  readonly name: string;
  readonly exchangeCode: string;

  /** Its listing state, so a delisted name on a list reads as one. */
  readonly status: string;
  readonly position: number;
  readonly addedAtUtc: string;
}

/** Which way an instrument moved — the categories breadth counts. */
export type PriceMove = "Advancing" | "Declining" | "Unchanged" | "NoData";

/** One instrument's quote on a session, as the server measured it. */
export interface Quote {
  readonly instrumentId: string;
  readonly ticker: string;
  readonly venue: string;
  readonly previousSession: string;
  readonly rawPreviousClose: number | null;

  /** The previous close restated for actions going ex and rounded to the tick. */
  readonly reference: number | null;
  readonly close: number | null;

  /** Fractional: 0.012 is 1.2%. Null when either bar is missing. */
  readonly change: number | null;
  readonly volume: number | null;
  readonly move: PriceMove;
  readonly adjustedForAction: boolean;
}

/** A watchlist's instruments quoted on one session, in list order. */
export interface WatchlistQuotes {
  readonly name: string;

  /** Null only for an empty list with no session named. */
  readonly session: string | null;
  readonly sessionWasDefaulted: boolean;
  readonly knownAsOf: string | null;
  readonly announcementPolicy: string;
  readonly venues: readonly { readonly code: string; readonly previousSession: string }[];
  readonly quotes: readonly Quote[];
}

/** A watchlist with its instruments, in list order. */
export interface Watchlist {
  readonly name: string;
  readonly createdAtUtc: string;
  readonly updatedAtUtc: string;
  readonly count: number;
  readonly items: readonly WatchlistItem[];
}
