import { useCallback, useEffect, useRef, useState } from "react";
import { completeEntry } from "../../lib/console/entry";
import type { TranscriptItem } from "../../lib/console/records";
import { liveServices, runEntry, type ConsoleServices } from "../../lib/console/run";
import { RecordView } from "./RecordView";

interface ConsoleProps {
  /** Injected so the console can be driven without a network in tests. */
  readonly services?: ConsoleServices;
}

const BANNER: TranscriptItem = {
  id: 0,
  entry: null,
  record: {
    kind: "lines",
    lines: [
      "Personal Quant Terminal — read-only console over the same application layer the",
      "operator CLI drives. Nothing here writes.",
      "",
      "Type 'help' for the command language, or a ticker to begin.",
    ],
  },
};

/**
 * The terminal, as a terminal.
 *
 * A transcript and a prompt rather than a page of panels, because that is what
 * this system already is underneath: one core with many interfaces, and a
 * command language the operator CLI already speaks. A console makes the two
 * surfaces the same shape, so what somebody learns typing here transfers
 * directly to the shell.
 *
 * It reads and never writes. Every mutating path — ingest, resolve a finding —
 * belongs to the operator CLI, where it runs with the deployment's own
 * environment and leaves a run record. A browser tab with no authentication is
 * the wrong place to close a data-quality finding from, and Phase 19 is where
 * that changes.
 */
export function Console({ services = liveServices }: ConsoleProps) {
  const [items, setItems] = useState<readonly TranscriptItem[]>([BANNER]);
  const [input, setInput] = useState("");
  const [busy, setBusy] = useState(false);

  // The line being run, held separately from the transcript so it can be
  // echoed the instant it is submitted. A terminal that swallowed what you
  // typed until the answer arrived would leave you unsure it took the command
  // at all, and the slowest commands here are the ones that most need it.
  const [pending, setPending] = useState<string | null>(null);
  const [history, setHistory] = useState<readonly string[]>([]);
  const [historyIndex, setHistoryIndex] = useState<number | null>(null);

  const nextId = useRef(1);
  const inputRef = useRef<HTMLInputElement>(null);
  const tailRef = useRef<HTMLDivElement>(null);
  const running = useRef<AbortController | null>(null);

  // The transcript grows downward and the prompt lives at the bottom, so a new
  // record that pushed the prompt off-screen would leave the caret invisible
  // while the user is still typing into it.
  useEffect(() => {
    // Feature-tested rather than assumed. jsdom implements no layout, so it
    // ships no scrollIntoView, and an unguarded call turns every test that
    // happens to render the console into a crash about scrolling.
    tailRef.current?.scrollIntoView?.({ block: "end" });
  }, [items, busy, pending]);

  useEffect(() => () => running.current?.abort(), []);

  const submit = useCallback(
    async (line: string) => {
      const trimmed = line.trim();

      if (trimmed === "" || busy) {
        return;
      }

      const id = nextId.current;
      nextId.current += 1;

      setHistory((previous) => [...previous, trimmed]);
      setHistoryIndex(null);
      setInput("");
      setPending(trimmed);
      setBusy(true);

      const controller = new AbortController();
      running.current = controller;

      const result = await runEntry(trimmed, services, controller.signal);

      running.current = null;
      setBusy(false);
      setPending(null);

      if (result.clear) {
        setItems([]);
        return;
      }

      setItems((previous) => [...previous, { id, entry: trimmed, record: result.record }]);
    },
    [busy, services],
  );

  const onKeyDown = useCallback(
    (event: React.KeyboardEvent<HTMLInputElement>) => {
      if (event.key === "Enter") {
        event.preventDefault();
        void submit(input);
        return;
      }

      if (event.key === "Tab") {
        event.preventDefault();

        const completed = completeEntry(input);

        if (completed !== null) {
          setInput(completed);
        }

        return;
      }

      // Ctrl+C abandons a running command without losing the transcript, which
      // is what it does in every shell this borrows from.
      if (event.key === "c" && event.ctrlKey) {
        running.current?.abort();
        return;
      }

      if (event.key === "ArrowUp") {
        event.preventDefault();

        if (history.length === 0) {
          return;
        }

        const index = historyIndex === null ? history.length - 1 : Math.max(0, historyIndex - 1);

        setHistoryIndex(index);
        setInput(history[index] ?? "");
        return;
      }

      if (event.key === "ArrowDown") {
        event.preventDefault();

        if (historyIndex === null) {
          return;
        }

        const index = historyIndex + 1;

        if (index >= history.length) {
          setHistoryIndex(null);
          setInput("");
          return;
        }

        setHistoryIndex(index);
        setInput(history[index] ?? "");
      }
    },
    [history, historyIndex, input, submit],
  );

  return (
    <div
      className="console"
      onClick={() => inputRef.current?.focus()}
      role="presentation"
    >
      <div className="console__scroll">
        {items.map((item) => (
          <article className="turn" key={item.id}>
            {item.entry === null ? null : (
              <p className="turn__entry">
                <span className="prompt" aria-hidden="true">
                  pqt&gt;
                </span>
                <span>{item.entry}</span>
              </p>
            )}
            <RecordView record={item.record} />
          </article>
        ))}

        {pending === null ? null : (
          <article className="turn">
            <p className="turn__entry">
              <span className="prompt" aria-hidden="true">
                pqt&gt;
              </span>
              <span>{pending}</span>
            </p>

            {busy ? (
              <p className="turn__busy" role="status">
                <span className="spinner" aria-hidden="true" />
                working — ctrl+c to abandon
              </p>
            ) : null}
          </article>
        )}

        <div ref={tailRef} />
      </div>

      <label className="console__prompt">
        <span className="prompt" aria-hidden="true">
          pqt&gt;
        </span>
        <input
          ref={inputRef}
          className="console__input"
          value={input}
          onChange={(event) => setInput(event.target.value)}
          onKeyDown={onKeyDown}
          disabled={busy}
          spellCheck={false}
          autoComplete="off"
          autoCapitalize="off"
          autoCorrect="off"
          aria-label="Terminal command"
          placeholder={busy ? "" : "help"}
        />
      </label>
    </div>
  );
}
