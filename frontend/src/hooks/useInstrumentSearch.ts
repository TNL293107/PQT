import { useEffect, useState } from "react";
import { searchInstruments } from "../lib/api/instruments";
import { parseCommand } from "../lib/command/parseCommand";
import type { Instrument } from "../types/instrument";

/** Loading lifecycle of the instrument search query. */
export type InstrumentSearchState = "idle" | "searching" | "ready" | "error";

export interface UseInstrumentSearchResult {
  readonly state: InstrumentSearchState;
  readonly results: readonly Instrument[];
  readonly error: string | null;

  /**
   * The recognised function mnemonic in the entry, if any.
   *
   * Surfaced so the search box can say the function is not available yet
   * rather than quietly searching for a company of that name.
   */
  readonly functionCode: string | null;
}

/**
 * How long typing must pause before a request goes out.
 *
 * Short enough that the list feels immediate, long enough that typing a
 * five-character ticker is one request rather than five.
 */
const DEBOUNCE_MS = 140;

/**
 * Searches the instrument master as the user types.
 *
 * Hand-written rather than pulled from a server-state library, matching the
 * decision recorded on the system health hook: the terminal has two queries so
 * far, and the dependency is not yet earned.
 *
 * Two things make it safe on a per-keystroke path. Every request is aborted
 * when the query changes, so a slow answer for `FP` cannot land after the
 * answer for `FPT` and repopulate the list with results for something the user
 * has stopped typing. And an aborted request is not an error — it is the
 * expected end of a superseded query, and showing it would make the search box
 * flash a failure on every fast keystroke.
 */
export function useInstrumentSearch(query: string): UseInstrumentSearchResult {
  const [state, setState] = useState<InstrumentSearchState>("idle");
  const [results, setResults] = useState<readonly Instrument[]>([]);
  const [error, setError] = useState<string | null>(null);

  const { securityQuery, functionCode } = parseCommand(query);

  useEffect(() => {
    if (securityQuery.length === 0) {
      setState("idle");
      setResults([]);
      setError(null);
      return;
    }

    const controller = new AbortController();

    const timer = setTimeout(() => {
      setState("searching");

      searchInstruments(securityQuery, controller.signal)
        .then((found) => {
          setResults(found);
          setError(null);
          setState("ready");
        })
        .catch((cause: unknown) => {
          if (controller.signal.aborted) {
            return;
          }

          setResults([]);
          setError(
            cause instanceof Error
              ? cause.message
              : "The terminal could not reach the instrument service.",
          );
          setState("error");
        });
    }, DEBOUNCE_MS);

    return () => {
      clearTimeout(timer);
      controller.abort();
    };
  }, [securityQuery]);

  return { state, results, error, functionCode };
}
