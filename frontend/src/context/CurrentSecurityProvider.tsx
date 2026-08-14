import { useCallback, useMemo, useState, type ReactNode } from "react";
import { CurrentSecurityContext, type CurrentSecurityValue } from "./currentSecurity";
import type { CurrentSecurity } from "../types/instrument";

/**
 * Holds the terminal's current security for everything below it.
 *
 * Mounted above the router, because the selection is terminal state rather
 * than page state and must survive navigation. The reasoning behind the shape
 * of the value is recorded on {@link CurrentSecurityContext}.
 */
export function CurrentSecurityProvider({ children }: { readonly children: ReactNode }) {
  const [security, setSecurity] = useState<CurrentSecurity | null>(null);

  const select = useCallback((next: CurrentSecurity) => {
    // Replaced outright rather than merged. Selecting VNM after FPT must
    // leave nothing of FPT behind for a consumer to read by mistake.
    setSecurity(next);
  }, []);

  const clear = useCallback(() => setSecurity(null), []);

  const value = useMemo<CurrentSecurityValue>(
    () => ({ security, select, clear }),
    [security, select, clear],
  );

  return (
    <CurrentSecurityContext.Provider value={value}>
      {children}
    </CurrentSecurityContext.Provider>
  );
}
