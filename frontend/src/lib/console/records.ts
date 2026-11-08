import type { Instrument } from "../../types/instrument";
import type {
  BarSeries,
  DataQualityIssue,
  DataQualityReport,
  IngestionRun,
} from "../../types/marketData";
import type { SystemHealth } from "../../types/health";
import type { ChartStyle } from "../chart/series";
import type { Breadth, UniverseList } from "../../types/universe";
import type { InstrumentCoverage } from "../../types/coverage";
import type { FundamentalRatios } from "../../types/fundamentals";
import type { Watchlist, WatchlistList, WatchlistQuotes } from "../../types/watchlist";
import type { Workspace, WorkspaceList } from "../../types/workspace";
import type { ResearchRuns } from "../../types/research";

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
      /** How the price pane is drawn. Candlesticks when absent. */
      readonly style?: ChartStyle;
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
    }
  | { readonly kind: "universes"; readonly listing: UniverseList }
  | { readonly kind: "breadth"; readonly breadth: Breadth }
  | {
      readonly kind: "coverage";
      readonly instrument: Instrument;
      readonly coverage: InstrumentCoverage;
    }
  | {
      readonly kind: "fundamentals";
      readonly instrument: Instrument;
      readonly ratios: FundamentalRatios;
    }
  | { readonly kind: "watchlists"; readonly listing: WatchlistList }
  | {
      readonly kind: "watchlist";
      readonly watchlist: Watchlist;
      /** The list's quotes, when they were asked for and given. */
      readonly quotes?: WatchlistQuotes;
      /** Why there are no quotes, in the server's words, when they were refused. */
      readonly quoteProblem?: string;
    }
  | { readonly kind: "runs"; readonly runs: ResearchRuns }
  | { readonly kind: "workspaces"; readonly listing: WorkspaceList }
  | { readonly kind: "workspace"; readonly workspace: Workspace };

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
