import type { Instrument } from "../../types/instrument";
import type {
  BarSeries,
  DataQualityIssue,
  DataQualityReport,
  IngestionRun,
} from "../../types/marketData";
import type { SystemHealth } from "../../types/health";

/**
 * What a command produced.
 *
 * Structured, never pre-rendered text. The console draws a price series as a
 * chart and a finding as a row, and a command that returned formatted strings
 * would force every future surface — a wider panel, an export, a screen
 * reader — to parse its own output back out again.
 */
export type ConsoleRecord =
  | { readonly kind: "lines"; readonly lines: readonly string[] }
  | { readonly kind: "error"; readonly message: string; readonly hint?: string }
  | { readonly kind: "help" }
  | { readonly kind: "health"; readonly health: SystemHealth }
  | {
      readonly kind: "instruments";
      readonly query: string;
      readonly results: readonly Instrument[];
      /** Set when the query was ambiguous, so the console can say why. */
      readonly ambiguous?: boolean;
    }
  | { readonly kind: "description"; readonly instrument: Instrument }
  | {
      readonly kind: "series";
      readonly instrument: Instrument;
      readonly series: BarSeries;
      /** The as-of the caller asked for, echoed so the answer states its cut. */
      readonly knownAsOf: string | null;
    }
  | {
      readonly kind: "quality";
      readonly instrument: Instrument;
      readonly report: DataQualityReport;
      readonly issues: readonly DataQualityIssue[];
    }
  | {
      readonly kind: "ingestion";
      readonly instrument: Instrument;
      readonly runs: readonly IngestionRun[];
    };

/** One exchange in the transcript: what was typed, and what came back. */
export interface TranscriptItem {
  readonly id: number;

  /** The line as typed, or null for output the console produced unprompted. */
  readonly entry: string | null;

  readonly record: ConsoleRecord;
}

/** Builds a plain-text record. */
export function lines(...text: string[]): ConsoleRecord {
  return { kind: "lines", lines: text };
}

/** Builds a refusal, optionally with the thing to try instead. */
export function failure(message: string, hint?: string): ConsoleRecord {
  return hint === undefined ? { kind: "error", message } : { kind: "error", message, hint };
}
