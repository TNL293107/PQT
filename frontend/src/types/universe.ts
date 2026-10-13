/**
 * Types mirroring the JSON contract served by the API's universe endpoints.
 * They are the only place that wire format is described on the client.
 */

/** One universe, as `GET /universes` lists it. */
export interface UniverseSummary {
  readonly code: string;
  readonly name: string;
  readonly kind: string;

  /** First date the membership is claimed to be known, or null for no claim. */
  readonly coverageFrom: string | null;

  /** First date no longer claimed (exclusive), or null while the claim runs on. */
  readonly coverageUntil: string | null;

  /**
   * Members today, or null when the claim does not reach today. Null is not
   * zero, and must never be drawn as one.
   */
  readonly memberCount: number | null;
}

/** Every universe a deployment defines. */
export interface UniverseList {
  /** The date member counts were read as of. */
  readonly asOf: string;
  readonly count: number;
  readonly universes: readonly UniverseSummary[];
}

/** How a member counts. Never inferred on the client. */
export type BreadthCategory = "Advancing" | "Declining" | "Unchanged" | "NoData";

/** One member's move on the session. */
export interface BreadthMember {
  readonly instrumentId: string;
  readonly ticker: string;
  readonly venue: string;
  readonly previousSession: string;

  /** The previous close as printed, when there is a bar. */
  readonly rawPreviousClose: number | null;

  /** The previous close restated for actions going ex on the session. */
  readonly previousClose: number | null;
  readonly close: number | null;

  /** Fractional change: 0.012 is 1.2%. Null when either bar is missing. */
  readonly change: number | null;
  readonly volume: number | null;
  readonly category: BreadthCategory;

  /** Whether an action going ex on the session restated the previous close. */
  readonly adjustedForAction: boolean;
}

/** A venue the members trade on, and its session before this one. */
export interface BreadthVenue {
  readonly code: string;
  readonly previousSession: string;
}

/** What a venue-wide count leaves out of its own member set. */
export interface VenueListing {
  /** Members whose listing date is not recorded, counted as listed. */
  readonly withoutListingDate: number;
  readonly neverIngestedCount: number;

  /** Members with no daily bar on any session. */
  readonly neverIngested: readonly string[];
}

/**
 * Market breadth for one universe or venue on one session, counted by the server.
 *
 * The counts arrive computed. The console draws them and does not recount
 * them from the rows: ADR-026 keeps one definition of "advancing", and it is
 * the server's.
 */
export interface Breadth {
  /** What was counted: a universe's members or a venue's listed equities. */
  readonly scope: "Universe" | "Venue";

  /** The universe's or the venue's code. */
  readonly code: string;
  readonly session: string;

  /** Whether no session was asked for and the server chose the latest. */
  readonly sessionWasDefaulted: boolean;
  readonly knownAsOf: string | null;
  readonly announcementPolicy: string;
  readonly memberCount: number;
  readonly advancing: number;
  readonly declining: number;
  readonly unchanged: number;
  readonly noData: number;
  readonly advancingVolume: number;
  readonly decliningVolume: number;
  readonly unchangedVolume: number;
  readonly venues: readonly BreadthVenue[];
  readonly members: readonly BreadthMember[];

  /** Set for a venue only. */
  readonly listing: VenueListing | null;
}
