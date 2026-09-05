import type {
  BarSeries,
  DataQualityIssues,
  DataQualityReport,
  IngestionHistory,
} from "../../types/marketData";
import { apiUrl } from "./config";

/** Milliseconds before a market data request is abandoned. */
const REQUEST_TIMEOUT_MS = 8_000;

/**
 * Raised when the API answers, but not with a result.
 *
 * Carries the status so a caller can tell a rejected request from an outage,
 * and a message already fit to show a user.
 */
export class MarketDataApiError extends Error {
  /** The HTTP status the API answered with. */
  readonly status: number;

  constructor(message: string, status: number) {
    super(message);
    this.name = "MarketDataApiError";
    this.status = status;
  }
}

/** What a caller may narrow a bar read by. */
export interface BarQuery {
  readonly interval?: string;
  readonly limit?: number;

  /**
   * Whether to apply corporate action factors on read.
   *
   * Absent means the server's default. Asking for adjusted does not guarantee
   * this system did the adjusting — a source that adjusts at source returns a
   * series it has already rescaled, and the response says so separately.
   */
  readonly adjusted?: boolean;

  /**
   * The observation-time cut.
   *
   * A point-in-time read: what the system believed at this instant, not what it
   * believes now. A period first observed after it is absent rather than
   * filled from the current value.
   */
  readonly knownAsOf?: string;
}

/**
 * Reads a bounded window of an instrument's series.
 *
 * @param instrumentId The canonical identifier.
 * @param query What to narrow the read by.
 * @param signal Aborts the request.
 */
export async function fetchBars(
  instrumentId: string,
  query: BarQuery = {},
  signal?: AbortSignal,
): Promise<BarSeries> {
  const params = new URLSearchParams();

  if (query.interval) params.set("interval", query.interval);
  if (query.limit !== undefined) params.set("limit", String(query.limit));
  if (query.adjusted !== undefined) params.set("adjusted", String(query.adjusted));
  if (query.knownAsOf) params.set("knownAsOf", query.knownAsOf);

  return getJson<BarSeries>(path(instrumentId, "bars", params), signal);
}

/** The window a trust score is measured over. */
export interface QualityQuery {
  readonly interval?: string;

  /** First day of the window. The server defaults to a year before `to`. */
  readonly from?: string;

  /** Last day of the window. The server defaults to today. */
  readonly to?: string;
}

/**
 * Scores how much a series can be trusted over a window.
 *
 * Every figure it returns is measured over that window and says nothing about
 * the rest of the series, which is why the window is a parameter rather than a
 * fixed recent slice: a deployment holding only a historical backfill would
 * otherwise score zero against a window its data never reaches.
 *
 * @param instrumentId The canonical identifier.
 * @param query The resolution and the window.
 * @param signal Aborts the request.
 */
export async function fetchQuality(
  instrumentId: string,
  query: QualityQuery = {},
  signal?: AbortSignal,
): Promise<DataQualityReport> {
  const params = new URLSearchParams();

  if (query.interval) params.set("interval", query.interval);
  if (query.from) params.set("from", query.from);
  if (query.to) params.set("to", query.to);

  return getJson<DataQualityReport>(path(instrumentId, "quality", params), signal);
}

/**
 * Lists the findings nothing has yet accounted for.
 *
 * @param instrumentId The canonical identifier.
 * @param interval The resolution.
 * @param signal Aborts the request.
 */
export async function fetchQualityIssues(
  instrumentId: string,
  interval?: string,
  signal?: AbortSignal,
): Promise<DataQualityIssues> {
  const params = new URLSearchParams();

  if (interval) params.set("interval", interval);

  return getJson<DataQualityIssues>(path(instrumentId, "quality/issues", params), signal);
}

/**
 * Reads the recent ingestion attempts against a series.
 *
 * Every attempt, not only the successful ones. A pipeline that recorded only
 * its successes could not explain a gap.
 *
 * @param instrumentId The canonical identifier.
 * @param interval The resolution.
 * @param signal Aborts the request.
 */
export async function fetchIngestionHistory(
  instrumentId: string,
  interval?: string,
  signal?: AbortSignal,
): Promise<IngestionHistory> {
  const params = new URLSearchParams();

  if (interval) params.set("interval", interval);

  return getJson<IngestionHistory>(path(instrumentId, "ingestion", params), signal);
}

function path(instrumentId: string, resource: string, params: URLSearchParams): string {
  const query = params.toString();
  const base = `/instruments/${encodeURIComponent(instrumentId)}/${resource}`;

  return apiUrl(query ? `${base}?${query}` : base);
}

async function getJson<T>(url: string, signal?: AbortSignal): Promise<T> {
  const response = await request(url, signal);

  if (!response.ok) {
    throw await toError(response);
  }

  return (await response.json()) as T;
}

function request(url: string, signal?: AbortSignal): Promise<Response> {
  // Both signals matter: the caller's aborts a superseded command, the timeout
  // bounds a request the server never answers.
  return fetch(url, {
    headers: { Accept: "application/json" },
    signal: signal
      ? AbortSignal.any([signal, AbortSignal.timeout(REQUEST_TIMEOUT_MS)])
      : AbortSignal.timeout(REQUEST_TIMEOUT_MS),
  });
}

/**
 * Turns a failed response into an error carrying something worth reading.
 *
 * The API answers failures as problem details, and its `detail` is written for
 * a caller. Replacing it with a status code would discard the one part of the
 * response that says what to do about it.
 */
async function toError(response: Response): Promise<MarketDataApiError> {
  let detail: string | null = null;

  try {
    const problem = (await response.json()) as { detail?: unknown; title?: unknown };

    if (typeof problem.detail === "string" && problem.detail.trim() !== "") {
      detail = problem.detail;
    } else if (typeof problem.title === "string" && problem.title.trim() !== "") {
      detail = problem.title;
    }
  } catch {
    // A body that is not problem details tells us nothing beyond the status.
  }

  return new MarketDataApiError(
    detail ?? `The request failed with status ${response.status}.`,
    response.status,
  );
}
