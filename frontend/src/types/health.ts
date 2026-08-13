/**
 * Types mirroring the JSON contract served by the API's health endpoints.
 * They are the only place the wire format is described on the client.
 */

/** Status string as reported by ASP.NET Core health checks. */
export type HealthCheckStatus = "Healthy" | "Degraded" | "Unhealthy";

/** A single dependency result inside a health report. */
export interface HealthCheckEntry {
  readonly name: string;
  readonly status: HealthCheckStatus;
  readonly durationMs: number;
  readonly description?: string;
}

/** The body returned by `/health` and `/health/ready`. */
export interface HealthReport {
  readonly status: HealthCheckStatus;
  readonly totalDurationMs: number;
  readonly checks: readonly HealthCheckEntry[];
}

/** Status of one row on the system status panel. */
export type ServiceStatus = "online" | "degraded" | "offline" | "unknown";

/** Identifiers for the services the terminal reports on in Phase 0. */
export type ServiceId = "backend" | "postgres" | "redis";

/** A resolved view of one service, ready to render. */
export interface ServiceHealth {
  readonly id: ServiceId;
  readonly label: string;
  readonly status: ServiceStatus;
  readonly detail?: string;
  readonly latencyMs?: number;
}

/** The complete system status shown on the dashboard. */
export interface SystemHealth {
  readonly services: readonly ServiceHealth[];
  readonly checkedAt: Date;

  /**
   * Whether the API answered at all.
   *
   * Separate from the per-service statuses: when this is false the dependency
   * rows report `unknown` rather than `offline`, because the browser has no
   * way to observe PostgreSQL or Redis directly.
   */
  readonly apiReachable: boolean;

  /** Why the API could not be reached, when it could not be. */
  readonly error?: string;
}
