import type { FundamentalRatios } from "../../types/fundamentals";
import { apiUrl } from "./config";

/**
 * Reads an issuer's point-in-time ratios (ADR-030).
 *
 * Without a session, the server prices them on the latest session it holds a
 * close for.
 *
 * @param instrumentId The canonical identifier.
 * @param on The session, `yyyy-MM-dd`, or undefined for the latest.
 * @param signal Aborts the request.
 */
export async function fetchRatios(
  instrumentId: string,
  on?: string,
  signal?: AbortSignal,
): Promise<FundamentalRatios> {
  const query = on ? `?on=${encodeURIComponent(on)}` : "";
  const response = await fetch(
    apiUrl(`/instruments/${encodeURIComponent(instrumentId)}/ratios${query}`),
    { headers: { Accept: "application/json" }, signal },
  );

  if (!response.ok) {
    let detail = `The ratios could not be read (HTTP ${response.status}).`;

    try {
      const problem = (await response.json()) as { detail?: string };
      detail = problem.detail ?? detail;
    } catch {
      // Not a problem document; the status says enough.
    }

    throw new Error(detail);
  }

  return (await response.json()) as FundamentalRatios;
}
