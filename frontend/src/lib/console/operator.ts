import type { Instrument } from "../../types/instrument";
import {
  WatchlistApiError,
  addToWatchlist,
  createWatchlist,
  deleteWatchlist,
  fetchWatchlist,
  fetchWatchlistQuotes,
  fetchWatchlists,
  removeFromWatchlist,
} from "../api/watchlists";
import { deleteWorkspace, fetchWorkspace, fetchWorkspaces, saveWorkspace } from "../api/workspaces";
import type { resolveInstrument } from "../api/instruments";
import { MINIMUM_KEY_LENGTH, readOperatorKey, writeOperatorKey } from "./operatorKey";
import { failure, lines, type ConsoleRecord } from "./records";

/**
 * The console's operator state: the key, watchlists and workspaces
 * (ADR-026 slices 4 and 5, on ADR-027's write path).
 *
 * Kept apart from the read functions in `run.ts` because these are the only
 * lines that change anything, and the only ones that need the key.
 */

/** What the operator-state verbs reach. Injected, like every console service. */
export interface OperatorServices {
  readonly key: { readonly read: () => string | null; readonly write: (key: string | null) => boolean };
  readonly watchlists: {
    readonly list: typeof fetchWatchlists;
    readonly get: typeof fetchWatchlist;
    readonly quotes: typeof fetchWatchlistQuotes;
    readonly create: typeof createWatchlist;
    readonly add: typeof addToWatchlist;
    readonly remove: typeof removeFromWatchlist;
    readonly delete: typeof deleteWatchlist;
  };
  readonly workspaces: {
    readonly list: typeof fetchWorkspaces;
    readonly get: typeof fetchWorkspace;
    readonly save: typeof saveWorkspace;
    readonly delete: typeof deleteWorkspace;
  };
}

/** The live operator services. */
export const liveOperatorServices: OperatorServices = {
  key: { read: () => readOperatorKey(), write: (key) => writeOperatorKey(key) },
  watchlists: {
    list: fetchWatchlists,
    get: fetchWatchlist,
    quotes: fetchWatchlistQuotes,
    create: createWatchlist,
    add: addToWatchlist,
    remove: removeFromWatchlist,
    delete: deleteWatchlist,
  },
  workspaces: {
    list: fetchWorkspaces,
    get: fetchWorkspace,
    save: saveWorkspace,
    delete: deleteWorkspace,
  },
};

/** The verbs whose lines are not saved into a workspace or recalled with ↑. */
const UNSAVED_VERBS = new Set(["KEY", "WS", "CLEAR", "HELP"]);

/**
 * Whether a line belongs in a saved workspace.
 *
 * A workspace replays views. Saving the key into one would publish it to every
 * reader of `GET /workspaces`, saving `ws` lines would make a replay recurse,
 * and saving a watchlist write would repeat it on every load.
 */
export function isReplayable(line: string): boolean {
  const [first = "", second = ""] = line.trim().toUpperCase().split(/\s+/u);

  if (first === "" || UNSAVED_VERBS.has(first)) {
    return false;
  }

  // A workspace replays views, never writes: loading one in a tab that holds
  // the key must not create, fill or empty a watchlist as a side effect.
  return first !== "WL" || WATCHLIST_READS.has(second);
}

/** The watchlist sub-commands that only read. */
const WATCHLIST_READS = new Set(["", "LIST", "SHOW"]);

/**
 * How a line is echoed into the transcript and the history.
 *
 * `key <value>` is shown masked: the transcript is on screen and in the ↑
 * history, and neither is a place for the deployment's write credential.
 */
export function maskEntry(line: string): string {
  const words = line.trim().split(/\s+/u);

  if ((words[0] ?? "").toUpperCase() !== "KEY" || words.length < 2) {
    return line.trim();
  }

  return (words[1] ?? "").toLowerCase() === "clear" ? "key clear" : "key ••••••••";
}

/** Runs `key`, `key <value>` or `key clear`. */
export function runKey(argument: string, services: OperatorServices): ConsoleRecord {
  const value = argument.trim();

  if (value === "") {
    return services.key.read() === null
      ? lines(
          "No operator key is held in this tab. Watchlists and workspaces can be read but not saved.",
          "Set one with 'key <operator key>' — the value of PQT_OPERATOR_KEY.",
        )
      : lines("This tab holds an operator key. 'key clear' forgets it.");
  }

  if (value.toLowerCase() === "clear") {
    services.key.write(null);
    return lines("Operator key forgotten.");
  }

  if (/\s/u.test(value) || value.length < MINIMUM_KEY_LENGTH) {
    return failure(
      `An operator key is at least ${MINIMUM_KEY_LENGTH} characters with no spaces.`,
      "Paste the value of PQT_OPERATOR_KEY from the deployment's .env.",
    );
  }

  return services.key.write(value)
    ? lines("Operator key held for this tab. It is forgotten when the tab closes.")
    : failure("This browser refused to hold the key.", "Session storage may be disabled for this site.");
}

/**
 * Runs a watchlist line: `wl`, `wl show|create|delete <name>`,
 * `wl add|rm <name> <ticker>...`.
 *
 * @param argument The line after `wl`.
 * @param services What it reaches.
 * @param resolve Resolves a ticker against the instrument master.
 * @param signal Aborts the request.
 */
export async function runWatchlist(
  argument: string,
  services: OperatorServices,
  resolve: typeof resolveInstrument,
  signal?: AbortSignal,
): Promise<ConsoleRecord> {
  const [verb = "", name = "", ...tickers] = argument.trim().split(/\s+/u).filter(Boolean);

  return guard(async () => {
    switch (verb.toLowerCase()) {
      case "":
      case "list":
        return { kind: "watchlists", listing: await services.watchlists.list(signal) };

      case "show":
        return name === "" ? usage() : await show(name, tickers);

      case "create":
        return name === ""
          ? usage()
          : {
              kind: "watchlist",
              watchlist: await services.watchlists.create(name, services.key.read(), signal),
            };

      case "delete":
        if (name === "") return usage();
        await services.watchlists.delete(name, services.key.read(), signal);
        return lines(`Watchlist '${name}' deleted.`);

      case "add":
      case "rm":
      case "remove": {
        if (name === "" || tickers.length === 0) return usage();

        // Every ticker is resolved before anything is written, as the CLI
        // does, so a typo leaves the list exactly as it was.
        const instruments: Instrument[] = [];

        for (const ticker of tickers) {
          const resolution = await resolve(ticker, signal);

          if (resolution.outcome !== "Resolved" || !resolution.instrument) {
            return failure(
              `'${ticker}' does not name one instrument; nothing was changed.`,
              `Try 'find ${ticker}'.`,
            );
          }

          instruments.push(resolution.instrument);
        }

        const write = verb.toLowerCase() === "add" ? services.watchlists.add : services.watchlists.remove;
        let latest = null;

        for (const instrument of instruments) {
          latest = await write(name, instrument.instrumentId, services.key.read(), signal);
        }

        return latest === null ? usage() : { kind: "watchlist", watchlist: latest };
      }

      default:
        return usage();
    }
  });

  /**
   * The list and its quotes on one session — the quotes breadth counts.
   *
   * A list whose quotes are refused (no recent bars, a holiday named) is still
   * shown, with the server's reason beside it: the list exists either way.
   */
  async function show(listName: string, rest: readonly string[]): Promise<ConsoleRecord> {
    let on: string | undefined;

    if (rest.length > 0) {
      const [flag = "", day = ""] = rest;

      if (flag.toLowerCase() !== "--on" || !/^\d{4}-\d{2}-\d{2}$/u.test(day) || rest.length > 2) {
        return failure("wl show <name> [--on yyyy-MM-dd]");
      }

      on = day;
    }

    const watchlist = await services.watchlists.get(listName, signal);

    try {
      const quotes = await services.watchlists.quotes(listName, on, signal);
      return { kind: "watchlist", watchlist, quotes };
    } catch (error) {
      if (error instanceof WatchlistApiError && error.status === 422) {
        return { kind: "watchlist", watchlist, quoteProblem: error.message };
      }

      throw error;
    }
  }

  function usage(): ConsoleRecord {
    return failure(
      "wl list · wl show <name> [--on yyyy-MM-dd] · wl create <name> · wl add <name> <ticker>… · wl rm <name> <ticker>… · wl delete <name>",
    );
  }
}

/** What a workspace line asks the console to do besides showing a record. */
export interface WorkspaceOutcome {
  readonly record: ConsoleRecord;

  /** Lines the console runs next, in order, when a workspace is loaded. */
  readonly replay?: readonly string[];
}

/**
 * Runs a workspace line: `ws`, `ws show|load|delete <name>`, `ws save <name>`.
 *
 * @param argument The line after `ws`.
 * @param history The lines run so far this session, oldest first.
 * @param services What it reaches.
 * @param signal Aborts the request.
 */
export async function runWorkspace(
  argument: string,
  history: readonly string[],
  services: OperatorServices,
  signal?: AbortSignal,
): Promise<WorkspaceOutcome> {
  const [verb = "", name = ""] = argument.trim().split(/\s+/u).filter(Boolean);
  const usage = failure("ws list · ws show <name> · ws load <name> · ws save <name> · ws delete <name>");

  if (verb !== "" && verb.toLowerCase() !== "list" && name === "") {
    return { record: usage };
  }

  switch (verb.toLowerCase()) {
    case "":
    case "list":
      return { record: await guard(async () => ({ kind: "workspaces", listing: await services.workspaces.list(signal) })) };

    case "show":
      return { record: await guard(async () => ({ kind: "workspace", workspace: await services.workspaces.get(name, signal) })) };

    case "load": {
      let entries: readonly string[] = [];
      const record = await guard(async () => {
        const workspace = await services.workspaces.get(name, signal);
        entries = workspace.entries.filter(isReplayable);
        return lines(`Replaying workspace '${workspace.name}' — ${entries.length} line${entries.length === 1 ? "" : "s"}.`);
      });

      return record.kind === "error" ? { record } : { record, replay: entries };
    }

    case "save": {
      // The session's own lines, deduplicated in order: re-running FPT GP
      // three times while exploring is one view, not three.
      const lines_ = [...new Set(history.filter(isReplayable).map((line) => line.trim()))];

      if (lines_.length === 0) {
        return {
          record: failure(
            "Nothing to save: no view has been opened in this session yet.",
            "Run the lines you want to keep, then 'ws save <name>'.",
          ),
        };
      }

      return {
        record: await guard(async () => ({
          kind: "workspace",
          workspace: await services.workspaces.save(name, lines_, services.key.read(), signal),
        })),
      };
    }

    case "delete":
      return {
        record: await guard(async () => {
          await services.workspaces.delete(name, services.key.read(), signal);
          return lines(`Workspace '${name}' deleted.`);
        }),
      };

    default:
      return { record: usage };
  }
}

/**
 * Turns a refused write into the instruction that fixes it.
 *
 * 401 and 403 are not the same problem: one is this tab, the other is the
 * deployment, and the operator does different things about them.
 */
async function guard(work: () => Promise<ConsoleRecord>): Promise<ConsoleRecord> {
  try {
    return await work();
  } catch (error) {
    if (error instanceof WatchlistApiError && error.status === 401) {
      return failure("This needs the operator key.", "key <operator key> — the value of PQT_OPERATOR_KEY.");
    }

    if (error instanceof WatchlistApiError && error.status === 403) {
      return failure(error.message, "Reads still work; saving needs a key configured on the API.");
    }

    if (error instanceof WatchlistApiError) {
      return failure(error.message);
    }

    throw error;
  }
}
