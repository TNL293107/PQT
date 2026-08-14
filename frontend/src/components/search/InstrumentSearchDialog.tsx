import { useEffect, useId, useRef, useState, type KeyboardEvent } from "react";
import { useInstrumentSearch } from "../../hooks/useInstrumentSearch";
import type { Instrument } from "../../types/instrument";

interface InstrumentSearchDialogProps {
  readonly onSelect: (instrument: Instrument) => void;
  readonly onClose: () => void;
}

/**
 * The security search overlay, opened with Ctrl+K.
 *
 * Keyboard first, and keyboard complete: type, arrow through the results,
 * Enter to select, Escape to cancel. Nothing in the flow needs a pointer,
 * because on a terminal a hand leaving the keyboard is the slow part.
 *
 * The results are rendered in the order the API returned them. Ranking is
 * specified and tested on the server; re-sorting here would silently replace
 * it with a second, untested ranking that disagrees.
 */
export function InstrumentSearchDialog({ onSelect, onClose }: InstrumentSearchDialogProps) {
  const [query, setQuery] = useState("");
  const [highlighted, setHighlighted] = useState(0);

  const inputRef = useRef<HTMLInputElement>(null);
  const listboxId = useId();
  const optionId = (index: number) => `${listboxId}-option-${index}`;

  const { state, results, error, functionCode } = useInstrumentSearch(query);

  // The highlight is an index into a list that changes under it. Resetting on
  // every new result set keeps Enter pointing at the top match rather than at
  // whatever row happens to have inherited the old position.
  useEffect(() => setHighlighted(0), [results]);

  useEffect(() => {
    inputRef.current?.focus();
  }, []);

  const move = (delta: number) => {
    if (results.length === 0) {
      return;
    }

    // Wraps, so holding Down never dead-ends at the bottom of a short list.
    setHighlighted((current) => (current + delta + results.length) % results.length);
  };

  const handleKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    switch (event.key) {
      case "ArrowDown":
        event.preventDefault();
        move(1);
        break;
      case "ArrowUp":
        event.preventDefault();
        move(-1);
        break;
      case "Enter": {
        event.preventDefault();
        const chosen = results[highlighted];
        if (chosen !== undefined) {
          onSelect(chosen);
        }
        break;
      }
      case "Escape":
        event.preventDefault();
        onClose();
        break;
      default:
        break;
    }
  };

  return (
    <div className="search-overlay" role="presentation" onMouseDown={onClose}>
      {/* The panel stops the overlay's dismiss handler, so a click inside it —
          on a result, or to reposition the caret — does not close the dialog. */}
      <div
        className="search-panel"
        role="dialog"
        aria-modal="true"
        aria-label="Security search"
        onMouseDown={(event) => event.stopPropagation()}
      >
        <div className="search-panel__field">
          <span className="search-panel__prompt numeric" aria-hidden="true">
            &gt;
          </span>
          <input
            ref={inputRef}
            type="text"
            className="search-panel__input"
            placeholder="Ticker or company name"
            aria-label="Search securities"
            aria-controls={listboxId}
            aria-expanded={results.length > 0}
            aria-activedescendant={
              results.length > 0 ? optionId(highlighted) : undefined
            }
            autoComplete="off"
            spellCheck={false}
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            onKeyDown={handleKeyDown}
          />
          <kbd className="search-panel__hint numeric">ESC</kbd>
        </div>

        {functionCode !== null ? (
          <p className="search-panel__notice" role="status">
            <span className="numeric">{functionCode}</span> is not available
            yet. Select a security to set the terminal context.
          </p>
        ) : null}

        <ul className="search-results" role="listbox" id={listboxId} aria-label="Results">
          {results.map((instrument, index) => (
            <li
              key={instrument.instrumentId}
              id={optionId(index)}
              role="option"
              aria-selected={index === highlighted}
              className={
                index === highlighted
                  ? "search-result search-result--active"
                  : "search-result"
              }
              onMouseEnter={() => setHighlighted(index)}
              onClick={() => onSelect(instrument)}
            >
              <span className="search-result__ticker numeric">{instrument.ticker}</span>
              <span className="search-result__name">{instrument.name}</span>
              <span className="search-result__venue numeric">{instrument.exchange}</span>
              <span className="search-result__currency numeric">{instrument.currency}</span>
              <span className="search-result__class">{instrument.assetType}</span>
            </li>
          ))}
        </ul>

        <SearchStatus
          state={state}
          error={error}
          query={query}
          resultCount={results.length}
        />
      </div>
    </div>
  );
}

interface SearchStatusProps {
  readonly state: ReturnType<typeof useInstrumentSearch>["state"];
  readonly error: string | null;
  readonly query: string;
  readonly resultCount: number;
}

/**
 * The line under the results.
 *
 * Every state says something. A search box that goes blank when nothing
 * matches leaves the user unable to tell "no such security" from "the request
 * failed", and they are not the same problem.
 */
function SearchStatus({ state, error, query, resultCount }: SearchStatusProps) {
  if (state === "error") {
    return (
      <p className="search-panel__status search-panel__status--error" role="alert">
        {error ?? "The instrument service could not be reached."}
      </p>
    );
  }

  if (state === "idle") {
    return (
      <p className="search-panel__status">
        Type a ticker or a company name. <span className="numeric">↑</span>{" "}
        <span className="numeric">↓</span> to move,{" "}
        <span className="numeric">ENTER</span> to select.
      </p>
    );
  }

  if (state === "searching" && resultCount === 0) {
    return <p className="search-panel__status">Searching…</p>;
  }

  if (resultCount === 0) {
    return (
      <p className="search-panel__status" role="status">
        No instruments found for “{query.trim()}”.
      </p>
    );
  }

  return (
    <p className="search-panel__status numeric">
      {resultCount} {resultCount === 1 ? "match" : "matches"}
    </p>
  );
}
