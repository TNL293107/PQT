/**
 * Types mirroring the JSON contract served by the API's market data and data
 * quality endpoints. They are the only place that wire format is described on
 * the client.
 */

/** One stored session, as the API returns it. */
export interface Bar {
  readonly openedAtUtc: string;
  readonly open: number;
  readonly high: number;
  readonly low: number;
  readonly close: number;
  readonly volume: number;
  readonly turnover: number | null;

  /** The source that produced it. A series may legitimately hold more than one. */
  readonly source: string;

  /** Which statement of this period the value came from. Not a time. */
  readonly revision: number;

  /** What the price was multiplied by. One when the series is read raw. */
  readonly priceFactor: number;

  /** What the volume was multiplied by. One when the series is read raw. */
  readonly shareFactor: number;
}

/**
 * A bounded window of a series.
 *
 * `adjusted` and `adjustedAtSource` are not the same claim and must never be
 * collapsed. The first says the caller asked for adjusted prices; the second
 * says the source had already adjusted them, so this system applied nothing —
 * which is why `adjustedBars` can be zero on an adjusted read.
 */
export interface BarSeries {
  readonly instrumentId: string;
  readonly interval: string;
  readonly adjusted: boolean;
  readonly adjustedAtSource: boolean;
  readonly adjustedBars: number;
  readonly count: number;
  readonly limit: number;
  readonly bars: readonly Bar[];
}

/** The four components a trust score is built from, and their combination. */
export interface DataQualityScore {
  readonly completeness: number;
  readonly consistency: number;
  readonly validity: number;
  readonly sourceReliability: number;
  readonly overall: number;
}

/** What the ingestion pipeline did over the scored window. */
export interface IngestionSummary {
  readonly runs: number;
  readonly succeeded: number;
  readonly failed: number;
  readonly barsFetched: number;
  readonly barsAccepted: number;
  readonly barsRejected: number;
  readonly barsStored: number;
}

/**
 * How far a series can be trusted over a window.
 *
 * `calendarIsComplete` is the load-bearing flag. When it is false the venue's
 * calendar was not transcribed for the whole window, so completeness is not a
 * low score — it is not a score at all, and rendering the number without the
 * flag would present an unmeasured quantity as a measured one.
 */
export interface DataQualityReport {
  readonly instrumentId: string;
  readonly ticker: string;
  readonly interval: string;
  readonly fromDate: string;
  readonly toDate: string;
  readonly sessionsExpected: number;
  readonly barsStored: number;
  readonly unvalidatedBars: number;
  readonly openIssues: Readonly<Record<string, number>>;
  readonly ingestion: IngestionSummary;
  readonly score: DataQualityScore;
  readonly calendarIsComplete: boolean;
}

/** One finding the quality rules recorded and nothing has yet accounted for. */
export interface DataQualityIssue {
  readonly issueId: string;
  readonly sessionAtUtc: string;
  readonly kind: string;
  readonly status: string;
  readonly detail: string;
  readonly detectedAtUtc: string;
}

/** A page of findings against one series. */
export interface DataQualityIssues {
  readonly instrumentId: string;
  readonly interval: string;
  readonly count: number;
  readonly results: readonly DataQualityIssue[];
}

/** One attempt to ingest, whatever its outcome. */
export interface IngestionRun {
  readonly runId: string;
  readonly source: string;
  readonly interval: string;
  readonly requestedFromUtc: string;
  readonly requestedToUtc: string;
  readonly startedAtUtc: string;
  readonly outcome: string;
  readonly barsFetched: number;
  readonly barsAccepted: number;
  readonly barsRejected: number;
  readonly barsStored: number;
  readonly barsRevised: number;
  readonly failureReason: string | null;
}

/** The recent ingestion attempts against one series. */
export interface IngestionHistory {
  readonly instrumentId: string;
  readonly interval: string;
  readonly count: number;
  readonly runs: readonly IngestionRun[];
}
