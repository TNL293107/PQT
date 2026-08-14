import type {
  Instrument,
  InstrumentResolutionResponse,
  InstrumentSearchResponse,
} from "../../types/instrument";
import { apiUrl } from "./config";

/** Milliseconds before an instrument request is abandoned. */
const REQUEST_TIMEOUT_MS = 5_000;

/**
 * Raised when the API answers, but not with a result.
 *
 * Carries the status so a caller can tell a rejected request from an outage,
 * and a message already fit to show a user.
 */
export class InstrumentApiError extends Error {
  /** The HTTP status the API answered with. */
  readonly status: number;

  constructor(message: string, status: number) {
    super(message);
    this.name = "InstrumentApiError";
    this.status = status;
  }
}

/**
 * Searches the instrument master.
 *
 * The server ranks the results; this function preserves that order exactly.
 * Re-sorting on the client would quietly replace a ranking that is specified
 * and tested with one that is neither.
 *
 * @param query Free text — a ticker, a prefix, or part of a company name.
 * @param signal Aborts the request when the query is superseded.
 */
export async function searchInstruments(
  query: string,
  signal?: AbortSignal,
): Promise<readonly Instrument[]> {
  const url = apiUrl(`/instruments/search?q=${encodeURIComponent(query)}`);
  const payload = await getJson<InstrumentSearchResponse>(url, signal);

  return payload.results;
}

/**
 * Resolves a symbol to the one instrument trading under it.
 *
 * All three outcomes are results rather than failures, so the 404 and 409 the
 * API uses to distinguish them are read for their body rather than thrown.
 *
 * @param symbol The ticker to resolve.
 * @param signal Aborts the request.
 */
export async function resolveInstrument(
  symbol: string,
  signal?: AbortSignal,
): Promise<InstrumentResolutionResponse> {
  const response = await request(
    apiUrl(`/instruments/resolve?symbol=${encodeURIComponent(symbol)}`),
    signal,
  );

  // 200 resolved, 404 nothing answers to it, 409 several do. Anything else is
  // a genuine failure.
  if (![200, 404, 409].includes(response.status)) {
    throw await toError(response);
  }

  return (await response.json()) as InstrumentResolutionResponse;
}

/**
 * Re-reads an instrument by its canonical identifier.
 *
 * The trusted path behind a stored selection: the client holds an identifier
 * and asks the server what it currently points at, rather than trusting the
 * ticker and name it happens to have kept.
 *
 * @param instrumentId The canonical identifier.
 * @param signal Aborts the request.
 * @returns The instrument, or null when the identifier is unknown.
 */
export async function fetchInstrument(
  instrumentId: string,
  signal?: AbortSignal,
): Promise<Instrument | null> {
  const response = await request(
    apiUrl(`/instruments/${encodeURIComponent(instrumentId)}`),
    signal,
  );

  if (response.status === 404) {
    return null;
  }

  if (!response.ok) {
    throw await toError(response);
  }

  return (await response.json()) as Instrument;
}

async function getJson<T>(url: string, signal?: AbortSignal): Promise<T> {
  const response = await request(url, signal);

  if (!response.ok) {
    throw await toError(response);
  }

  return (await response.json()) as T;
}

function request(url: string, signal?: AbortSignal): Promise<Response> {
  // Both signals matter: the caller's aborts a superseded keystroke, the
  // timeout bounds a request the server never answers.
  return fetch(url, {
    headers: { Accept: "application/json" },
    signal: signal
      ? AbortSignal.any([signal, AbortSignal.timeout(REQUEST_TIMEOUT_MS)])
      : AbortSignal.timeout(REQUEST_TIMEOUT_MS),
  });
}

/**
 * Turns a failed response into an error a user can read.
 *
 * The API answers with RFC 9457 problem details, whose `detail` is written to
 * be shown. When the body is not that, the status is all there is to say.
 */
async function toError(response: Response): Promise<InstrumentApiError> {
  try {
    const problem: unknown = await response.json();

    if (
      typeof problem === "object" &&
      problem !== null &&
      "detail" in problem &&
      typeof problem.detail === "string"
    ) {
      return new InstrumentApiError(problem.detail, response.status);
    }
  } catch {
    // A non-JSON body is not itself the problem worth reporting; the status is.
  }

  return new InstrumentApiError(
    `The instrument service answered with ${response.status}.`,
    response.status,
  );
}
