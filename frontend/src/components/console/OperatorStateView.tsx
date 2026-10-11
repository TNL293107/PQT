import type { Watchlist, WatchlistList } from "../../types/watchlist";
import type { Workspace, WorkspaceList } from "../../types/workspace";

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

/** One watchlist and its instruments, in the operator's order. */
export function WatchlistView({ watchlist }: { readonly watchlist: Watchlist }) {
  return (
    <div className="out">
      <p className="out__line">
        <strong>{watchlist.name}</strong> — {watchlist.count} instrument{watchlist.count === 1 ? "" : "s"}
      </p>
      {watchlist.items.length === 0 ? (
        <p className="out__hint">Empty. wl add {watchlist.name} &lt;ticker&gt;…</p>
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
