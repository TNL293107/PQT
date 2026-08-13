import type { ServiceStatus } from "../../types/health";

const LABELS: Record<ServiceStatus, string> = {
  online: "ONLINE",
  degraded: "DEGRADED",
  offline: "OFFLINE",
  unknown: "UNKNOWN",
};

interface StatusPillProps {
  readonly status: ServiceStatus;
}

/**
 * Renders a service state.
 *
 * The state is carried by the text as well as the colour, so the panel is
 * still readable without colour perception.
 */
export function StatusPill({ status }: StatusPillProps) {
  return (
    <span className={`status-pill status-pill--${status}`} data-testid="status-pill">
      <span className="status-pill__dot" aria-hidden="true" />
      {LABELS[status]}
    </span>
  );
}
