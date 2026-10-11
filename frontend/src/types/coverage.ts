/**
 * Types mirroring the JSON contract of `GET /instruments/{id}/coverage` — the
 * coverage registry of ADR-026. They are the only place that wire format is
 * described on the client.
 *
 * Every state is decided by the server. The console renders them; it never
 * infers one from a count, because the difference between "not held" and "not
 * measured" is not in the numbers.
 */

/**
 * What PQT can say about one dataset for one instrument.
 *
 * `NotMeasured` must never render as `NotHeld`: the first says PQT cannot
 * tell, the second says PQT knows it holds none.
 */
export type CoverageState = "Held" | "NotHeld" | "NotMeasured" | "NotCollected";

/** The most recent ingestion attempt against a resolution, whatever its outcome. */
export interface IngestionAttempt {
  readonly source: string;
  readonly startedAtUtc: string;
  readonly completedAtUtc: string | null;
  readonly outcome: string;
}

/** One resolution of the price series. */
export interface PriceCoverage {
  /** The resolution's name, as the other endpoints write it — `OneDay`. */
  readonly interval: string;

  /** The short form a reader types — `1d`. */
  readonly intervalCode: string;

  readonly state: CoverageState;
  readonly firstSession: string | null;
  readonly lastSession: string | null;
  readonly bars: number;
  readonly sources: readonly string[];
  readonly openFindings: number;
  readonly lastAttempt: IngestionAttempt | null;

  /** Trading days between the first and last session, or null when not measured. */
  readonly sessionsExpected: number | null;

  /**
   * Of those, the sessions with no bar — or null when not measured, which is
   * never the same as zero.
   */
  readonly missingSessions: number | null;

  readonly note: string | null;
}

/** The corporate actions recorded against the instrument. */
export interface CorporateActionCoverage {
  readonly state: CoverageState;
  readonly count: number;
  readonly cancelled: number;
  readonly withoutAnnouncementDate: number;
  readonly firstExDate: string | null;
  readonly lastExDate: string | null;
  readonly note: string | null;
}

/** One spell of membership, half-open: `to` is the first day it was not a member. */
export interface UniverseSpell {
  readonly from: string;
  readonly to: string | null;
  readonly announcedOn: string | null;
  readonly removalAnnouncedOn: string | null;
}

/**
 * The instrument's membership of one universe.
 *
 * `declaredFrom`/`declaredUntil` are the window the universe claims to know;
 * outside it an absence of spells says nothing, which `note` states.
 */
export interface UniverseCoverage {
  readonly code: string;
  readonly name: string;
  readonly state: CoverageState;
  readonly declaredFrom: string | null;
  readonly declaredUntil: string | null;
  readonly spells: readonly UniverseSpell[];
  readonly note: string;
}

/** A dataset reported by state alone. */
export interface DatasetCoverage {
  readonly state: CoverageState;
  readonly note: string;
}

/** What PQT holds for one instrument, dataset by dataset. */
export interface InstrumentCoverage {
  readonly instrumentId: string;
  readonly ticker: string;
  readonly name: string;
  readonly exchange: string;
  readonly prices: readonly PriceCoverage[];
  readonly corporateActions: CorporateActionCoverage;
  readonly universes: readonly UniverseCoverage[];
  readonly fundamentals: DatasetCoverage;
}
