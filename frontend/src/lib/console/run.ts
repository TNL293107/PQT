import type { Instrument } from "../../types/instrument";
import { fetchSystemHealth } from "../api/health";
import { resolveInstrument, searchInstruments } from "../api/instruments";
import {
  fetchBars,
  fetchIngestionHistory,
  fetchQuality,
  fetchQualityIssues,
} from "../api/marketData";
import { parseEntry, readCount } from "./entry";
import { failure, lines, type ConsoleRecord } from "./records";

/**
 * The services a command reaches. Injected so the whole command language can
 * be exercised without a network.
 */
export interface ConsoleServices {
  readonly search: typeof searchInstruments;
  readonly resolve: typeof resolveInstrument;
  readonly bars: typeof fetchBars;
  readonly quality: typeof fetchQuality;
  readonly issues: typeof fetchQualityIssues;
  readonly ingestion: typeof fetchIngestionHistory;
  readonly health: typeof fetchSystemHealth;
}

/** The live services, reaching the API. */
export const liveServices: ConsoleServices = {
  search: searchInstruments,
  resolve: resolveInstrument,
  bars: fetchBars,
  quality: fetchQuality,
  issues: fetchQualityIssues,
  ingestion: fetchIngestionHistory,
  health: fetchSystemHealth,
};

/** What running a line produced, and whether it also clears the transcript. */
export interface ConsoleResult {
  readonly record: ConsoleRecord;
  readonly clear?: boolean;
}

/**
 * Runs one console line.
 *
 * Every refusal is a record rather than a thrown error. A console that lost its
 * transcript because a ticker was mistyped would be unusable, and the failure
 * is as much a part of the session's history as the answer.
 *
 * @param input The line as typed.
 * @param services What the command may reach.
 * @param signal Aborts a command the user interrupted.
 */
export async function runEntry(
  input: string,
  services: ConsoleServices,
  signal?: AbortSignal,
): Promise<ConsoleResult> {
  const entry = parseEntry(input);

  if (entry.raw === "") {
    return { record: lines() };
  }

  try {
    switch (entry.verb) {
      case "HELP":
        return { record: { kind: "help" } };

      case "CLEAR":
        return { record: lines(), clear: true };

      case "HEALTH":
        return { record: { kind: "health", health: await services.health() } };

      case "FIND":
        return { record: await find(entry.securityQuery, services, signal) };

      default:
        break;
    }

    if (entry.securityQuery === "") {
      return {
        record: failure(
          `'${entry.raw}' names no security.`,
          "Type a ticker, or 'help' for what this console understands.",
        ),
      };
    }

    const resolved = await resolveOne(entry.securityQuery, services, signal);

    if ("record" in resolved) {
      return { record: resolved.record };
    }

    const instrument = resolved.instrument;

    switch (entry.functionCode) {
      case "GP":
        return { record: await graph(instrument, entry.options, services, signal) };

      case "QLTY":
        return { record: await quality(instrument, entry.options, services, signal) };

      case "ING":
        return { record: await ingestion(instrument, entry.options, services, signal) };

      default:
        return { record: { kind: "description", instrument } };
    }
  } catch (error) {
    if (error instanceof DOMException && error.name === "AbortError") {
      return { record: failure("Cancelled.") };
    }

    return {
      record: failure(
        error instanceof Error ? error.message : "The command failed.",
        "The API may be unreachable — try 'health'.",
      ),
    };
  }
}

async function find(
  query: string,
  services: ConsoleServices,
  signal?: AbortSignal,
): Promise<ConsoleRecord> {
  if (query.trim() === "") {
    return failure("'find' needs something to search for.", "find FPT");
  }

  const results = await services.search(query, signal);

  return { kind: "instruments", query, results };
}

/**
 * Resolves the security half, or explains why it could not.
 *
 * Ambiguity is an outcome rather than an error: ticker uniqueness is enforced
 * per venue, so the same three letters can be live on two of them at once, and
 * the console shows both rather than picking.
 */
async function resolveOne(
  query: string,
  services: ConsoleServices,
  signal?: AbortSignal,
): Promise<{ instrument: Instrument } | { record: ConsoleRecord }> {
  const resolution = await services.resolve(query, signal);

  if (resolution.outcome === "Resolved" && resolution.instrument) {
    return { instrument: resolution.instrument };
  }

  if (resolution.outcome === "Ambiguous") {
    return {
      record: {
        kind: "instruments",
        query,
        results: resolution.candidates,
        ambiguous: true,
      },
    };
  }

  return {
    record: failure(
      `No instrument currently trades under '${query}'.`,
      `Try 'find ${query}' to search names as well as tickers.`,
    ),
  };
}

async function graph(
  instrument: Instrument,
  options: Readonly<Record<string, string | null>>,
  services: ConsoleServices,
  signal?: AbortSignal,
): Promise<ConsoleRecord> {
  // Raw and adjusted are opposite requests and the flags are mutually
  // exclusive. Silently preferring one would answer a question nobody asked.
  const wantsRaw = "raw" in options;
  const wantsAdjusted = "adjusted" in options;

  if (wantsRaw && wantsAdjusted) {
    return failure(
      "--raw and --adjusted ask for different series.",
      "Name one. Raw is what the market printed; adjusted rescales it for corporate actions.",
    );
  }

  const asOf = readDay(options, "as-of");

  if ("record" in asOf) {
    return asOf.record;
  }

  const knownAsOf = asOf.day;

  const series = await services.bars(
    instrument.instrumentId,
    {
      ...(typeof options["interval"] === "string" ? { interval: options["interval"] } : {}),
      ...(readCount(options, "limit") !== null ? { limit: readCount(options, "limit")! } : {}),
      ...(wantsRaw ? { adjusted: false } : {}),
      ...(wantsAdjusted ? { adjusted: true } : {}),
      ...(knownAsOf !== null ? { knownAsOf: new Date(knownAsOf).toISOString() } : {}),
    },
    signal,
  );

  return { kind: "series", instrument, series, knownAsOf };
}

async function quality(
  instrument: Instrument,
  options: Readonly<Record<string, string | null>>,
  services: ConsoleServices,
  signal?: AbortSignal,
): Promise<ConsoleRecord> {
  const interval = typeof options["interval"] === "string" ? options["interval"] : undefined;

  // Without a window the server scores the last year, which measures nothing
  // on a deployment holding a historical backfill. Naming one is how a reader
  // asks about the years the data is actually in.
  const from = readDay(options, "from");
  const to = readDay(options, "to");

  if ("record" in from) return from.record;
  if ("record" in to) return to.record;

  // Both halves, always. A score with no findings beside it invites the reader
  // to treat the number as the whole answer, and the findings are what say
  // which part of it to distrust.
  const [report, issues] = await Promise.all([
    services.quality(
      instrument.instrumentId,
      {
        ...(interval !== undefined ? { interval } : {}),
        ...(from.day !== null ? { from: from.day } : {}),
        ...(to.day !== null ? { to: to.day } : {}),
      },
      signal,
    ),
    services.issues(instrument.instrumentId, interval, signal),
  ]);

  return { kind: "quality", instrument, report, issues: issues.results };
}

/**
 * Reads a calendar day, or refuses.
 *
 * A day, not an instant: the window a score covers is a range of sessions, and
 * a time of day would suggest a precision the scoring does not have.
 */
function readDay(
  options: Readonly<Record<string, string | null>>,
  name: string,
): { day: string | null } | { record: ConsoleRecord } {
  const value = options[name];

  if (typeof value !== "string") {
    return { day: null };
  }

  // Round-tripped rather than merely parsed. Date.parse accepts 2016-02-31 and
  // silently answers with 2 March, so a typo would score a window the caller
  // never asked for instead of being refused.
  const parsed = new Date(`${value}T00:00:00Z`);

  if (
    !/^\d{4}-\d{2}-\d{2}$/.test(value) ||
    Number.isNaN(parsed.getTime()) ||
    parsed.toISOString().slice(0, 10) !== value
  ) {
    return {
      record: failure(`'--${name} ${value}' is not a date.`, "Write it as 2016-05-27."),
    };
  }

  return { day: value };
}

async function ingestion(
  instrument: Instrument,
  options: Readonly<Record<string, string | null>>,
  services: ConsoleServices,
  signal?: AbortSignal,
): Promise<ConsoleRecord> {
  const interval = typeof options["interval"] === "string" ? options["interval"] : undefined;
  const history = await services.ingestion(instrument.instrumentId, interval, signal);

  return { kind: "ingestion", instrument, runs: history.runs };
}
