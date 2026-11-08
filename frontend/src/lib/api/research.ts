import type { ResearchRuns } from "../../types/research";
import { apiUrl } from "./config";

/**
 * Lists recorded research runs, newest first (sprint 11.2).
 *
 * @param experiment Only this experiment's runs, or every experiment's.
 * @param limit How many, or the server's default.
 * @param signal Aborts the request.
 */
export async function fetchResearchRuns(
  experiment?: string,
  limit?: number,
  signal?: AbortSignal,
): Promise<ResearchRuns> {
  const query = new URLSearchParams();

  if (experiment) {
    query.set("experiment", experiment);
  }

  if (limit !== undefined) {
    query.set("limit", String(limit));
  }

  const suffix = query.size > 0 ? `?${query.toString()}` : "";
  const response = await fetch(apiUrl(`/research/runs${suffix}`), {
    headers: { Accept: "application/json" },
    signal,
  });

  if (!response.ok) {
    let detail = `The research runs could not be read (HTTP ${response.status}).`;

    try {
      const problem = (await response.json()) as { detail?: string };
      detail = problem.detail ?? detail;
    } catch {
      // Not a problem document; the status says enough.
    }

    throw new Error(detail);
  }

  return (await response.json()) as ResearchRuns;
}
