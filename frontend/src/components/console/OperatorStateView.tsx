import type { PriceMove, Watchlist, WatchlistList, WatchlistQuotes } from "../../types/watchlist";
import type { Workspace, WorkspaceList } from "../../types/workspace";
import { change, price } from "../../lib/console/quoteFormat";

/**
 * The operator's own state — watchlists and workspaces — as the console draws
 * it. Presentation only: every rule about what may be saved is the server's.
 */

/** Every watchlist, by name. */
export function WatchlistsView({ listing }: { readonly listing: WatchlistList }) {
  if (listing.results.length === 0) {
    return (
      <div className="out">
        <p className="out__line">No watchlists yet.</p>
        <p className="out__hint">wl create &lt;name&gt;, then wl add &lt;name&gt; &lt;ticker&gt;…</p>
      </div>
    );
  }

  return (
    <div className="out">
      <table className="grid">
        <thead>
          <tr>
            <th scope="col">Watchlist</th>
            <th scope="col" className="grid__num">
              Instruments
            </th>
            <th scope="col">Updated</th>
          </tr>
        </thead>
        <tbody>
          {listing.results.map((list) => (
            <tr key={list.name}>
              <td className="grid__key">{list.name}</td>
              <td className="grid__num">{list.count}</td>
              <td className="grid__note">{day(list.updatedAtUtc)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

/**
 * The glyph and tone for each move. Direction is written, never only
 * coloured — the same rule breadth follows.
 */
const MOVE: Readonly<Record<PriceMove, { glyph: string; tone: string }>> = {
  Advancing: { glyph: "▲", tone: "is-up" },
  Declining: { glyph: "▼", tone: "is-down" },
  Unchanged: { glyph: "=", tone: "is-flat" },
  NoData: { glyph: "·", tone: "is-unmeasured" },
};

/**
 * One watchlist and its instruments, in the operator's order.
 *
 * With quotes, each row carries the server's close, change and reference —
 * the quotes breadth counts, so the two cannot disagree on a session. Nothing
 * here recomputes a change.
 */
export function WatchlistView({
  watchlist,
  quotes,
  quoteProblem,
}: {
  readonly watchlist: Watchlist;
  readonly quotes?: WatchlistQuotes | undefined;
  readonly quoteProblem?: string | undefined;
}) {
  const byInstrument = new Map((quotes?.quotes ?? []).map((quote) => [quote.instrumentId, quote]));
  const restated = (quotes?.quotes ?? []).filter((quote) => quote.adjustedForAction);

  return (
    <div className="out">
      <p className="out__line">
        <strong>{watchlist.name}</strong> — {watchlist.count} instrument{watchlist.count === 1 ? "" : "s"}
        {quotes?.session ? (
          <span className="out__line--dim">
            {" · "}
            {quotes.session}
            {quotes.sessionWasDefaulted ? " (the latest session any of them has a bar for)" : ""}
            {" against "}
            {quotes.venues.map((venue) => `${venue.code} ${venue.previousSession}`).join(", ")}
          </span>
        ) : null}
      </p>
      {quoteProblem ? <p className="out__line out__line--warn">No quotes: {quoteProblem}</p> : null}
      {watchlist.items.length === 0 ? (
        <p className="out__hint">Empty. wl add {watchlist.name} &lt;ticker&gt;…</p>
      ) : quotes ? (
        <>
          <table className="grid">
            <thead>
              <tr>
                <th scope="col">Ticker</th>
                <th scope="col" className="grid__num">
                  Close
                </th>
                <th scope="col" className="grid__num">
                  Change
                </th>
                <th scope="col" className="grid__num">
                  Ref
                </th>
                <th scope="col" className="grid__num">
                  Volume
                </th>
                <th scope="col">Venue</th>
                <th scope="col">Status</th>
              </tr>
            </thead>
            <tbody>
              {watchlist.items.map((item) => {
                const quote = byInstrument.get(item.instrumentId);
                const move = MOVE[quote?.move ?? "NoData"];

                return (
                  <tr key={item.instrumentId}>
                    <td className="grid__key">{item.ticker}</td>
                    <td className="grid__num">{price(quote?.close ?? null)}</td>
                    <td className={`grid__num ${move.tone}`}>
                      {move.glyph} {change(quote?.change ?? null)}
                    </td>
                    <td className="grid__num">
                      {price(quote?.reference ?? quote?.rawPreviousClose ?? null)}
                      {quote?.adjustedForAction ? " *" : ""}
                    </td>
                    <td className="grid__num">
                      {quote?.volume == null ? "—" : quote.volume.toLocaleString("en-US")}
                    </td>
                    <td>{item.exchangeCode}</td>
                    <td className={item.status === "Listed" ? undefined : "grid__note"}>{item.status}</td>
                  </tr>
                );
              })}
            </tbody>
          </table>
          {restated.length > 0 ? (
            <p className="out__line out__line--warn">
              *{" "}
              {restated
                .map(
                  (quote) =>
                    `${quote.ticker} went ex: its previous close ${price(quote.rawPreviousClose)} is restated to ${price(quote.reference)}`,
                )
                .join("; ")}
              , the exchange&apos;s reference rounded to the tick.
            </p>
          ) : null}
        </>
      ) : (
        <table className="grid">
          <thead>
            <tr>
              <th scope="col">Ticker</th>
              <th scope="col">Venue</th>
              <th scope="col">Status</th>
              <th scope="col">Name</th>
            </tr>
          </thead>
          <tbody>
            {watchlist.items.map((item) => (
              <tr key={item.instrumentId}>
                <td className="grid__key">{item.ticker}</td>
                <td>{item.exchangeCode}</td>
                {/* A delisted name on a list says so in words, not only by being greyed. */}
                <td className={item.status === "Listed" ? undefined : "grid__note"}>{item.status}</td>
                <td className="grid__note">{item.name}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
}

/** Every saved workspace. */
export function WorkspacesView({ listing }: { readonly listing: WorkspaceList }) {
  if (listing.results.length === 0) {
    return (
      <div className="out">
        <p className="out__line">No workspaces yet.</p>
        <p className="out__hint">Open the views you want, then ws save &lt;name&gt;.</p>
      </div>
    );
  }

  return (
    <div className="out">
      <table className="grid">
        <thead>
          <tr>
            <th scope="col">Workspace</th>
            <th scope="col" className="grid__num">
              Lines
            </th>
            <th scope="col">Replays</th>
          </tr>
        </thead>
        <tbody>
          {listing.results.map((workspace) => (
            <tr key={workspace.name}>
              <td className="grid__key">{workspace.name}</td>
              <td className="grid__num">{workspace.entries.length}</td>
              <td className="grid__note">{workspace.entries.join(" · ")}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

/** One workspace's lines, in replay order. */
export function WorkspaceView({ workspace }: { readonly workspace: Workspace }) {
  return (
    <div className="out">
      <p className="out__line">
        <strong>{workspace.name}</strong> — saved {day(workspace.updatedAtUtc)}; 'ws load {workspace.name}' replays:
      </p>
      <ol className="workspace-lines">
        {workspace.entries.map((entry, index) => (
          <li key={`${index}-${entry}`}>
            <code>{entry}</code>
          </li>
        ))}
      </ol>
    </div>
  );
}

function day(instant: string): string {
  return instant.slice(0, 10);
}