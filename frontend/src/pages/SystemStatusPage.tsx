import { SystemStatusPanel } from "../components/status/SystemStatusPanel";
import { ErrorState } from "../components/ui/ErrorState";
import { LoadingState } from "../components/ui/LoadingState";
import { useSystemHealth } from "../hooks/useSystemHealth";

/**
 * The Phase 0 landing page.
 *
 * It reports whether the foundation is actually running. That is the whole
 * deliverable of this phase, so it is what the terminal opens on.
 *
 * When the API is unreachable the page shows both the failure notice and the
 * table: the notice explains what to fix, and the table still states what is
 * known and what is merely unobservable from the browser.
 */
export function SystemStatusPage() {
  const { state, data, error, refresh } = useSystemHealth();

  const unreachable = data !== null && !data.apiReachable;

  return (
    <div className="page">
      <div className="page__intro">
        <h1 className="page__title">Infrastructure</h1>
        <p className="page__lede">
          Live availability of the services the terminal is built on. No market
          data is connected yet.
        </p>
      </div>

      {state === "loading" && data === null ? <LoadingState /> : null}

      {state === "error" ? (
        <ErrorState detail={error ?? "Unknown failure."} onRetry={refresh} />
      ) : null}

      {unreachable ? (
        <ErrorState
          detail={data.error ?? "The API did not respond."}
          onRetry={refresh}
        />
      ) : null}

      {data !== null ? <SystemStatusPanel health={data} /> : null}
    </div>
  );
}
