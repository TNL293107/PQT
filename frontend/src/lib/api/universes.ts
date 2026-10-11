import type { Breadth, UniverseList } from "../../types/universe";
import { apiUrl } from "./config";

/** Milliseconds before a universe request is abandoned. */
const REQUEST_TIMEOUT_MS = 8_000;

/**
 * Raised when the API answers, but not with a result.
 *
 * Carries the status and the server's `outcome`, so the console can tell "no
 * such universe" from "not a trading day" without parsing the sentence, and a
 * message already fit to show a user.
 */
export class UniverseApiError extends Error {
  /** The HTTP status the API answered with. */
  readonly status: number;

  /** The refusal's reason, as the server named it, when it named one. */
  readonly outcome: string | null;

  constructor(message: string, status: number, outcome: string | null = null) {
    super(message);
    this.name = "UniverseApiError";
    this.status = status;
    this.outcome = outcome;
  }
}

/**
 * Lists every universe, with its declared coverage and today's member count.
 *
 * @param signal Aborts the request.
 */
export async function fetchUniverses(signal?: AbortSignal): Promise<UniverseList> {
  return getJson<UniverseList>(apiUrl("/universes"), signal);
}

/**
 * Reads a universe's breadth on a session.
 *
 * @param code The universe's code, such as `VN30`.
 * @param on The session as `yyyy-MM-dd`, or undefined for the latest one any
 * member has a bar for.
 * @param signal Aborts the request.
 */
export async function fetchBreadth(
  code: string,
  on?: string,
  signal?: AbortSignal,
): Promise<Breadth> {
  const base = `/universes/${encodeURIComponent(code)}/breadth`;
  const path = on ? `${base}?on=${encodeURIComponent(on)}` : base;

  return getJson<Breadth>(apiUrl(path), signal);
}

async function getJson<T>(url: string, signal?: AbortSignal): Promise<T> {
  // Both signals matter: the caller's aborts a superseded command, the timeout
  // bounds a request the server never answers.
  const response = await fetch(url, {
    headers: { Accept: "application/json" },
    signal: signal
      ? AbortSignal.any([signal, AbortSignal.timeout(REQUEST_TIMEOUT_MS)])
      : AbortSignal.timeout(REQUEST_TIMEOUT_MS),
  });

  if (!response.ok) {
    throw await toError(response);
  }

  return (await response.json()) as T;
}

/**
 * Turns a failed response into an error carrying the server's own words.
 *
 * A breadth refusal's `detail` says exactly why the session cannot be counted,
 * and its `outcome` says which kind of refusal it is. Replacing either with a
 * status code would discard the part of the answer worth reading.
 */
async function toError(response: Response): Promise<UniverseApiError> {
  let detail: string | null = null;
  let outcome: string | null = null;

  try {
    const problem = (await response.json()) as {
      detail?: unknown;
      title?: unknown;
      outcome?: unknown;
    };

    if (typeof problem.detail === "string" && problem.detail.trim() !== "") {
      detail = problem.detail;
    } else if (typeof problem.title === "string" && problem.title.trim() !== "") {
      detail = problem.title;
    }

    if (typeof problem.outcome === "string") {
      outcome = problem.outcome;
    }
  } catch {
    // A body that is not problem details tells us nothing beyond the status.
  }

  return new UniverseApiError(
    detail ?? `The request failed with status ${response.status}.`,
    response.status,
    outcome,
  );
}
