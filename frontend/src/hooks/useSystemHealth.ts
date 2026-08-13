import { useCallback, useEffect, useRef, useState } from "react";
import { fetchSystemHealth } from "../lib/api/health";
import type { SystemHealth } from "../types/health";

/** Loading lifecycle of the system status query. */
export type HealthQueryState = "loading" | "ready" | "error";

export interface UseSystemHealthResult {
  readonly state: HealthQueryState;
  readonly data: SystemHealth | null;
  readonly error: string | null;
  readonly refresh: () => void;
}

/** How often the status panel re-polls, in milliseconds. */
const POLL_INTERVAL_MS = 15_000;

/**
 * Polls the API health endpoints and exposes an explicit loading/ready/error
 * state.
 *
 * Deliberately hand-written rather than pulled from a server-state library:
 * Phase 0 has exactly one query, and the dependency is not yet earned. Phase 4
 * onwards, when real market data arrives, is the point to reconsider.
 */
export function useSystemHealth(): UseSystemHealthResult {
  const [state, setState] = useState<HealthQueryState>("loading");
  const [data, setData] = useState<SystemHealth | null>(null);
  const [error, setError] = useState<string | null>(null);

  // Guards against a late response from a previous poll overwriting a newer
  // one, and against setting state after unmount.
  const requestId = useRef(0);
  const mounted = useRef(true);

  const load = useCallback(async () => {
    const id = ++requestId.current;

    try {
      const result = await fetchSystemHealth();
      if (!mounted.current || id !== requestId.current) {
        return;
      }
      setData(result);
      setError(null);
      setState("ready");
    } catch (cause) {
      if (!mounted.current || id !== requestId.current) {
        return;
      }
      setError(
        cause instanceof Error
          ? cause.message
          : "The terminal could not reach the API.",
      );
      setState("error");
    }
  }, []);

  useEffect(() => {
    mounted.current = true;
    void load();

    const timer = setInterval(() => void load(), POLL_INTERVAL_MS);

    return () => {
      mounted.current = false;
      clearInterval(timer);
    };
  }, [load]);

  const refresh = useCallback(() => {
    setState("loading");
    void load();
  }, [load]);

  return { state, data, error, refresh };
}
