/**
 * How a console line is read.
 *
 * The terminal convention is `<security> <function> [options]` — `FPT GP` for
 * a price graph, `FPT QLTY` for what the quality rules found. A handful of
 * lines name no security at all (`help`, `health`, `clear`), and those are read
 * as a bare verb.
 *
 * Nothing here knows any ticker. Whether the security half names a real
 * instrument is decided by the server against the instrument master, so no
 * hard-coded symbol table can creep in.
 */

/** A function mnemonic the console recognises. */
export interface FunctionSpec {
  /** The mnemonic, upper-case. */
  readonly code: string;

  /** One line, for `help` and for the completion hint. */
  readonly summary: string;

  /** Option names this function accepts, without the leading dashes. */
  readonly options: readonly string[];
}

/**
 * The functions that operate on a security.
 *
 * An allow-list rather than a shape test, and deliberately so. A rule such as
 * "one to four letters is a function" would split `Hoa Phat` into a security
 * `HOA` and a function `PHAT`, and short words are exactly what Vietnamese
 * company names are made of.
 */
export const FUNCTIONS: readonly FunctionSpec[] = [
  {
    code: "DES",
    summary: "Security description — identity, venue, listing state",
    options: [],
  },
  {
    code: "GP",
    summary: "Price graph and the stored series behind it",
    options: ["interval", "limit", "raw", "adjusted", "as-of"],
  },
  {
    code: "QLTY",
    summary: "Trust score and every finding nothing has accounted for",
    options: ["interval", "from", "to"],
  },
  {
    code: "ING",
    summary: "Ingestion history — every attempt, not only the ones that worked",
    options: ["interval"],
  },
];

/** The verbs that name no security. */
export const VERBS: readonly FunctionSpec[] = [
  { code: "HELP", summary: "This list", options: [] },
  { code: "HEALTH", summary: "Whether this deployment's dependencies answer", options: [] },
  { code: "FIND", summary: "Search the instrument master by ticker or name", options: [] },
  { code: "CLEAR", summary: "Empty the transcript", options: [] },
];

/** One console line, split into what it names and what it asks for. */
export interface ConsoleEntry {
  /** The raw line, kept verbatim so the transcript can echo what was typed. */
  readonly raw: string;

  /** The bare verb, upper-cased, when the line names one. */
  readonly verb: string | null;

  /** The security half, when the line names one. */
  readonly securityQuery: string;

  /** The recognised function mnemonic, upper-cased, or null. */
  readonly functionCode: string | null;

  /** Options given as `--name value` or `--flag`. */
  readonly options: Readonly<Record<string, string | null>>;
}

const VERB_CODES = new Set(VERBS.map((verb) => verb.code));
const FUNCTION_CODES = new Set(FUNCTIONS.map((fn) => fn.code));

/**
 * Splits a console line into a security, a function and its options.
 *
 * Options are stripped first, because a security query may contain spaces and
 * an option may not — reading them in the other order would make
 * `Hoa Phat --limit 5` ambiguous about where the name ends.
 *
 * @param input The line as typed.
 */
export function parseEntry(input: string): ConsoleEntry {
  const raw = input.trim();
  const { words, options } = splitOptions(raw);

  if (words.length === 0) {
    return { raw, verb: null, securityQuery: "", functionCode: null, options };
  }

  const first = (words[0] ?? "").toUpperCase();

  // A bare verb consumes the whole line, except FIND, which takes the rest as
  // its query.
  if (VERB_CODES.has(first)) {
    return {
      raw,
      verb: first,
      securityQuery: first === "FIND" ? words.slice(1).join(" ") : "",
      functionCode: null,
      options,
    };
  }

  const last = (words[words.length - 1] ?? "").toUpperCase();

  if (words.length >= 2 && FUNCTION_CODES.has(last)) {
    return {
      raw,
      verb: null,
      securityQuery: words.slice(0, -1).join(" "),
      functionCode: last,
      options,
    };
  }

  // A security with no function. DES is the sensible default: naming a
  // security and being told what it is.
  return { raw, verb: null, securityQuery: words.join(" "), functionCode: null, options };
}

/**
 * Pulls `--name value` and `--flag` out of a line, leaving the words.
 *
 * A value that starts with `--` is another option, so the one before it is a
 * flag. That is the same rule the operator CLI reads, and the two surfaces
 * having one grammar is worth more than either having a cleverer one.
 */
function splitOptions(input: string): {
  words: string[];
  options: Record<string, string | null>;
} {
  const tokens = input.split(/\s+/u).filter(Boolean);
  const words: string[] = [];
  const options: Record<string, string | null> = {};

  for (let index = 0; index < tokens.length; index += 1) {
    const token = tokens[index] ?? "";

    if (!token.startsWith("--")) {
      words.push(token);
      continue;
    }

    const name = token.slice(2).toLowerCase();

    if (name === "") {
      continue;
    }

    const next = tokens[index + 1];

    if (next !== undefined && !next.startsWith("--")) {
      options[name] = next;
      index += 1;
    } else {
      options[name] = null;
    }
  }

  return { words, options };
}

/**
 * Reads an option that must be a positive whole number.
 *
 * @returns The number, or null when the option is absent or unusable.
 */
export function readCount(
  options: Readonly<Record<string, string | null>>,
  name: string,
): number | null {
  const value = options[name];

  if (typeof value !== "string") {
    return null;
  }

  const parsed = Number.parseInt(value, 10);

  return Number.isSafeInteger(parsed) && parsed > 0 ? parsed : null;
}

/**
 * Suggests a completion for a partially typed line.
 *
 * Only ever completes the mnemonic, never the security: the console has no
 * symbol table, and offering one from whatever happens to be in the transcript
 * would suggest tickers this deployment may not hold.
 *
 * @returns The full line the completion would produce, or null when there is
 * exactly no unambiguous one.
 */
export function completeEntry(input: string): string | null {
  const tokens = input.split(/\s+/u).filter(Boolean);

  if (tokens.length === 0) {
    return null;
  }

  const partial = (tokens[tokens.length - 1] ?? "").toUpperCase();
  const endsWithSpace = /\s$/u.test(input);

  if (endsWithSpace) {
    return null;
  }

  const pool = tokens.length === 1 ? [...VERBS, ...FUNCTIONS] : FUNCTIONS;
  const matches = pool
    .map((spec) => spec.code)
    .filter((code) => code.startsWith(partial) && code !== partial);

  if (matches.length !== 1) {
    return null;
  }

  return [...tokens.slice(0, -1), matches[0]].join(" ");
}
