import type {
  HealthCheckStatus,
  HealthReport,
  ServiceHealth,
  ServiceStatus,
  SystemHealth,
} from "../../types/health";
import { apiUrl } from "./config";

/** Milliseconds before a health request is abandoned. */
const REQUEST_TIMEOUT_MS = 5_000;

/**
 * Fetches a health report.
 *
 * A 503 is a valid, meaningful answer from `/health/ready` — it carries the
 * per-dependency breakdown — so the response body is parsed for any status
 * that has a JSON body, not only for 2xx.
 */
async function fetchHealthReport(path: string): Promise<HealthReport> {
  const response = await fetch(apiUrl(path), {
    headers: { Accept: "application/json" },
    signal: AbortSignal.timeout(REQUEST_TIMEOUT_MS),
  });

  const body: unknown = await response.json();

  if (!isHealthReport(body)) {
    throw new Error(`Unexpected health payload from ${path}.`);
  }

  return body;
}

function isHealthReport(value: unknown): value is HealthReport {
  if (typeof value !== "object" || value === null) {
    return false;
  }

  const candidate = value as Partial<HealthReport>;
  return typeof candidate.status === "string" && Array.isArray(candidate.checks);
}

function toServiceStatus(status: HealthCheckStatus | undefined): ServiceStatus {
  switch (status) {
    case "Healthy":
      return "online";
    case "Degraded":
      return "degraded";
    case "Unhealthy":
      return "offline";
    default:
      return "unknown";
  }
}

/**
 * Loads the complete system status shown on the dashboard.
 *
 * Liveness and readiness are requested together: liveness establishes whether
 * the API answers at all, readiness reports each dependency behind it. If the
 * API is unreachable the dependencies are reported as unknown rather than
 * offline, because from the browser their true state cannot be observed.
 */
export async function fetchSystemHealth(): Promise<SystemHealth> {
  const checkedAt = new Date();

  const [liveness, readiness] = await Promise.allSettled([
    fetchHealthReport("/health"),
    fetchHealthReport("/health/ready"),
  ]);

  const backendOnline = liveness.status === "fulfilled";

  const readinessChecks =
    readiness.status === "fulfilled" ? readiness.value.checks : [];

  const dependency = (name: string): ServiceStatus => {
    if (!backendOnline) {
      return "unknown";
    }
    return toServiceStatus(
      readinessChecks.find((check) => check.name === name)?.status,
    );
  };

  const dependencyDetail = (name: string): string | undefined =>
    readinessChecks.find((check) => check.name === name)?.description;

  const services: ServiceHealth[] = [
    {
      id: "backend",
      label: "Backend API",
      status: backendOnline ? "online" : "offline",
      detail: backendOnline
        ? "Liveness probe answered."
        : "The API did not respond.",
      latencyMs:
        liveness.status === "fulfilled"
          ? liveness.value.totalDurationMs
          : undefined,
    },
    {
      id: "postgres",
      label: "PostgreSQL",
      status: dependency("postgres"),
      detail: dependencyDetail("postgres"),
    },
    {
      id: "redis",
      label: "Redis",
      status: dependency("redis"),
      detail: dependencyDetail("redis"),
    },
  ];

  return {
    services,
    checkedAt,
    apiReachable: backendOnline,
    error:
      liveness.status === "rejected"
        ? describeFailure(liveness.reason)
        : undefined,
  };
}

function describeFailure(reason: unknown): string {
  if (reason instanceof DOMException && reason.name === "TimeoutError") {
    return "The API did not respond within 5 seconds.";
  }
  if (reason instanceof Error) {
    return reason.message;
  }
  return "The API could not be reached.";
}
