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

/** A watchlist with its instruments, in list order. */
export interface Watchlist {
  readonly name: string;
  readonly createdAtUtc: string;
  readonly updatedAtUtc: string;
  readonly count: number;
  readonly items: readonly WatchlistItem[];
}
