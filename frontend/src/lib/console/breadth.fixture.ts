import type { Breadth, BreadthMember } from "../../types/universe";

/** A member row with sensible defaults, for tests. */
export function member(overrides: Partial<BreadthMember> & Pick<BreadthMember, "ticker">): BreadthMember {
  return {
    instrumentId: `id-${overrides.ticker}`,
    venue: "HOSE",
    previousSession: "2026-09-29",
    rawPreviousClose: 100,
    previousClose: 100,
    close: 100,
    change: 0,
    volume: 1_000,
    category: "Unchanged",
    adjustedForAction: false,
    ...overrides,
  };
}

/**
 * A count as the server would send it: one riser, one faller, a payer that
 * went ex and did not move, and a name with no bar.
 */
export const BREADTH: Breadth = {
  universe: "VN30",
  session: "2026-09-30",
  sessionWasDefaulted: false,
  knownAsOf: null,
  announcementPolicy: "permissive",
  memberCount: 4,
  advancing: 1,
  declining: 1,
  unchanged: 1,
  noData: 1,
  advancingVolume: 5_000,
  decliningVolume: 3_000,
  unchangedVolume: 7_000,
  venues: [{ code: "HOSE", previousSession: "2026-09-29" }],
  members: [
    member({ ticker: "RISE", close: 105, change: 0.05, volume: 5_000, category: "Advancing" }),
    member({ ticker: "FALL", close: 97, change: -0.03, volume: 3_000, category: "Declining" }),
    member({
      ticker: "DIV",
      rawPreviousClose: 25_000,
      previousClose: 24_000,
      close: 24_000,
      change: 0,
      volume: 7_000,
      adjustedForAction: true,
    }),
    member({
      ticker: "NIL",
      previousClose: null,
      close: null,
      change: null,
      volume: null,
      category: "NoData",
    }),
  ],
};
