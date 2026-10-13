import type { Watchlist, WatchlistList, WatchlistQuotes } from "../../types/watchlist";
import { apiUrl } from "./config";

/** Milliseconds before a watchlist request is abandoned. */
const REQUEST_TIMEOUT_MS = 8_000;

/**
 * Raised when the API answers a watchlist request with a refusal.
 *
 * The status tells a missing key (401) from a deployment that accepts no
 * writes at all (403), which the console answers differently.
 */
export class WatchlistApiError extends Error {
  /** The HTTP status the API answered with. */
  readonly status: number;

  constructor(message: string, status: number) {
    super(message);
    this.name = "WatchlistApiError";
    this.status = status;
  }
}

/** Lists every watchlist. */
export function fetchWatchlists(signal?: AbortSignal): Promise<WatchlistList> {
  return send<WatchlistList>("GET", "/watchlists", null, signal);
}

/** Reads one watchlist with its instruments. */
export function fetchWatchlist(name: string, signal?: AbortSignal): Promise<Watchlist> {
  return send<Watchlist>("GET", `/watchlists/${encodeURIComponent(name)}`, null, signal);
}

/**
 * Quotes a watchlist's instruments on one session — the quotes breadth counts.
 *
 * @param on The session as yyyy-MM-dd, or undefined for the latest one any
 *   instrument on the list has a bar for.
 */
export function fetchWatchlistQuotes(
  name: string,
  on?: string,
  signal?: AbortSignal,
): Promise<WatchlistQuotes> {
  const query = on === undefined ? "" : `?on=${encodeURIComponent(on)}`;
  return send<WatchlistQuotes>("GET", `/watchlists/${encodeURIComponent(name)}/quotes${query}`, null, signal);
}

/** Creates an empty watchlist. Needs the operator key. */
export function createWatchlist(
  name: string,
  key: string | null,
  signal?: AbortSignal,
): Promise<Watchlist> {
  return send<Watchlist>("POST", "/watchlists", key, signal, { name });
}

/** Appends an instrument. Needs the operator key; adding twice is not an error. */
export function addToWatchlist(
  name: string,
  instrumentId: string,
  key: string | null,
  signal?: AbortSignal,
): Promise<Watchlist> {
  return send<Watchlist>("PUT", instrumentPath(name, instrumentId), key, signal, {});
}

/** Removes an instrument. Needs the operator key; removing what is absent is not an error. */
export function removeFromWatchlist(
  name: string,
  instrumentId: string,
  key: string | null,
  signal?: AbortSignal,
): Promise<Watchlist> {
  return send<Watchlist>("DELETE", instrumentPath(name, instrumentId), key, signal);
}

/** Deletes a watchlist. Needs the operator key. */
export async function deleteWatchlist(
  name: string,
  key: string | null,
  signal?: AbortSignal,
): Promise<void> {
  await send<null>("DELETE", `/watchlists/${encodeURIComponent(name)}`, key, signal);
}

function instrumentPath(name: string, instrumentId: string): string {
  return `/watchlists/${encodeURIComponent(name)}/instruments/${encodeURIComponent(instrumentId)}`;
}

export async function send<T>(
  method: string,
  path: string,
  key: string | null,
  signal?: AbortSignal,
  body?: unknown,
): Promise<T> {
  const headers: Record<string, string> = { Accept: "application/json" };

  if (body !== undefined) headers["Content-Type"] = "application/json";
  if (key !== null) headers["Authorization"] = `Bearer ${key}`;

  const response = await fetch(apiUrl(path), {
    method,
    headers,
    ...(body !== undefined ? { body: JSON.stringify(body) } : {}),
    signal: signal
      ? AbortSignal.any([signal, AbortSignal.timeout(REQUEST_TIMEOUT_MS)])
      : AbortSignal.timeout(REQUEST_TIMEOUT_MS),
  });

  if (!response.ok) {
    throw await toError(response);
  }

  return response.status === 204 ? (null as T) : ((await response.json()) as T);
}

async function toError(response: Response): Promise<WatchlistApiError> {
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

  return new WatchlistApiError(
    detail ?? `The request failed with status ${response.status}.`,
    response.status,
  );
}
