/**
 * Function mnemonics the command bar recognises.
 *
 * None of them do anything yet — this is the registry later phases add to, and
 * recognising a mnemonic is what lets the terminal say "GP is not available
 * yet" instead of searching the instrument master for a company called
 * "FPT GP".
 *
 * It is an allow-list rather than a shape test on purpose. A rule such as
 * "one to four letters is a function" would split `Hoa Phat` into a security
 * `HOA` and a function `PHAT`, and short words are exactly what Vietnamese
 * company names are made of.
 */
export const KNOWN_FUNCTION_CODES: readonly string[] = ["GP", "FA", "NEWS"];

/**
 * A command bar entry, split into the security it names and what it asks for.
 */
export interface ParsedCommand {
  /** The text to search the instrument master for. */
  readonly securityQuery: string;

  /**
   * The recognised function mnemonic that followed the security, upper-cased,
   * or null when the entry names a security only.
   */
  readonly functionCode: string | null;
}

/**
 * Splits a command bar entry into a security and an optional function.
 *
 * Terminal convention is `<security> <function>` — `FPT GP` for a price
 * graph, `FPT FA` for financials. Phase 1 implements the security half only,
 * but the split belongs here rather than inside the search box: it is what
 * later phases attach functions to, and rewriting the search UI to introduce
 * it later would be the expensive way to arrive at the same place.
 *
 * Nothing here knows any ticker. Whether the security half names a real
 * instrument is decided by the search and resolution services against the
 * instrument master, so no hard-coded symbol table can creep in.
 */
export function parseCommand(input: string): ParsedCommand {
  const tokens = input.trim().split(/\s+/u).filter(Boolean);

  if (tokens.length < 2) {
    return { securityQuery: tokens.join(" "), functionCode: null };
  }

  const candidate = (tokens[tokens.length - 1] ?? "").toUpperCase();

  if (!KNOWN_FUNCTION_CODES.includes(candidate)) {
    return { securityQuery: tokens.join(" "), functionCode: null };
  }

  return {
    securityQuery: tokens.slice(0, -1).join(" "),
    functionCode: candidate,
  };
}
