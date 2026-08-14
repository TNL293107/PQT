/**
 * Types mirroring the JSON contract served by the API's instrument endpoints.
 * They are the only place that wire format is described on the client.
 */

/** Lifecycle state of an instrument, as reported by the API. */
export type InstrumentStatus = "Pending" | "Listed" | "Suspended" | "Delisted";

/**
 * Why a search result matched.
 *
 * The server has already ordered the results by it; the client shows it so a
 * user can see why a row is where it is, and never re-sorts by it.
 */
export type InstrumentMatchKind =
  | "ExactTicker"
  | "TickerPrefix"
  | "ExactName"
  | "NamePrefix"
  | "NameContains";

/**
 * One instrument as the API returns it.
 *
 * `instrumentId` is the identity. The ticker is what the user reads, and is
 * not stable — it changes on an exchange transfer and can be reassigned after
 * a delisting — so nothing in the client keys off it.
 */
export interface Instrument {
  readonly instrumentId: string;
  readonly ticker: string;
  readonly name: string;
  readonly assetType: string;
  readonly exchange: string;
  readonly currency: string;
  readonly status: InstrumentStatus;

  /** Absent outside search results, where nothing was ranked. */
  readonly matchKind?: InstrumentMatchKind | null;
}

/** The body returned by `GET /instruments/search`. */
export interface InstrumentSearchResponse {
  readonly query: string;
  readonly count: number;
  readonly limit: number;
  readonly results: readonly Instrument[];
}

/** How the API reports the outcome of resolving a symbol. */
export type InstrumentResolutionOutcome = "Resolved" | "NotFound" | "Ambiguous";

/** The body returned by `GET /instruments/resolve`, at every status. */
export interface InstrumentResolutionResponse {
  readonly query: string;
  readonly outcome: InstrumentResolutionOutcome;
  readonly instrument: Instrument | null;
  readonly candidates: readonly Instrument[];
}

/**
 * The security the terminal is currently pointed at.
 *
 * Deliberately the whole instrument rather than a ticker string. Every module
 * that consumes the context — quotes, charts, news, and later orders — needs
 * the canonical identifier, and a module that only had `"FPT"` would have to
 * resolve it again and could resolve it to something else.
 */
export type CurrentSecurity = Instrument;
