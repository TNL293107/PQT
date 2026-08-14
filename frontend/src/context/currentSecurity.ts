import { createContext, useContext } from "react";
import type { CurrentSecurity } from "../types/instrument";

export interface CurrentSecurityValue {
  /** The security the terminal is pointed at, or null before one is chosen. */
  readonly security: CurrentSecurity | null;

  /** Points the terminal at a security. */
  readonly select: (security: CurrentSecurity) => void;

  /** Points the terminal at nothing. */
  readonly clear: () => void;
}

/**
 * The terminal's current security.
 *
 * One place, above the router, because the selection outlives the view that
 * made it. Every module that arrives later — quote, chart, news, and
 * eventually orders — reads this same value, so a security chosen on one
 * screen is the security every other screen is describing. State owned by the
 * search component instead would make "what is the terminal looking at?" a
 * question with as many answers as there are components asking it.
 *
 * Context rather than a store because the value changes when a human chooses a
 * different security — a few times a minute at most. Streaming quotes for that
 * security are a different problem and do not belong here.
 *
 * The value held is the whole instrument, not a ticker. Consumers need the
 * canonical identifier: a ticker changes on an exchange transfer and can be
 * reassigned to an unrelated issuer after a delisting, so a module holding one
 * would eventually be describing the wrong company.
 *
 * The context and the hook live apart from the provider component so that the
 * provider's module exports a component and nothing else, which is what keeps
 * fast refresh working on it.
 */
export const CurrentSecurityContext = createContext<CurrentSecurityValue | null>(null);

/**
 * Reads the terminal's current security.
 *
 * Throws outside the provider rather than returning a null-object default: a
 * module that silently read "no security" when mounted in the wrong place
 * would render an empty panel instead of failing, and that is a bug nobody
 * finds.
 */
export function useCurrentSecurity(): CurrentSecurityValue {
  const value = useContext(CurrentSecurityContext);

  if (value === null) {
    throw new Error(
      "useCurrentSecurity must be used inside a CurrentSecurityProvider.",
    );
  }

  return value;
}
