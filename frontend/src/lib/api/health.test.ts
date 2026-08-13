import { beforeEach, describe, expect, it, vi } from "vitest";
import { fetchSystemHealth } from "./health";
import type { HealthReport } from "../../types/health";

const LIVENESS: HealthReport = {
  status: "Healthy",
  totalDurationMs: 1.2,
  checks: [{ name: "self", status: "Healthy", durationMs: 0.1 }],
};

function readinessReport(
  postgres: HealthReport["status"],
  redis: HealthReport["status"],
): HealthReport {
  return {
    status: postgres === "Healthy" && redis === "Healthy" ? "Healthy" : "Unhealthy",
    totalDurationMs: 8.4,
    checks: [
      {
        name: "postgres",
        status: postgres,
        durationMs: 4.1,
        description: "PostgreSQL responded to a round-trip query.",
      },
      {
        name: "redis",
        status: redis,
        durationMs: 2.2,
        description: "Redis responded to PING.",
      },
    ],
  };
}

/** Routes each health path to a supplied response. */
function stubFetch(
  handler: (path: string) => { status: number; body: unknown } | "network-error",
) {
  vi.stubGlobal(
    "fetch",
    vi.fn((input: string) => {
      const path = new URL(input, "http://localhost").pathname;
      const result = handler(path);

      if (result === "network-error") {
        return Promise.reject(new Error("Failed to fetch"));
      }

      return Promise.resolve(
        new Response(JSON.stringify(result.body), {
          status: result.status,
          headers: { "Content-Type": "application/json" },
        }),
      );
    }),
  );
}

describe("fetchSystemHealth", () => {
  beforeEach(() => {
    vi.unstubAllGlobals();
  });

  it("reports every service online when both endpoints are healthy", async () => {
    stubFetch((path) =>
      path === "/health"
        ? { status: 200, body: LIVENESS }
        : { status: 200, body: readinessReport("Healthy", "Healthy") },
    );

    const health = await fetchSystemHealth();

    expect(health.services.map((service) => service.status)).toEqual([
      "online",
      "online",
      "online",
    ]);
  });

  it("reads the dependency breakdown out of a 503 readiness response", async () => {
    // Readiness answers 503 when a dependency is down, and the body still
    // carries which one. Treating 503 as a transport failure would lose that.
    stubFetch((path) =>
      path === "/health"
        ? { status: 200, body: LIVENESS }
        : { status: 503, body: readinessReport("Healthy", "Unhealthy") },
    );

    const health = await fetchSystemHealth();

    expect(health.services.find((s) => s.id === "backend")?.status).toBe("online");
    expect(health.services.find((s) => s.id === "postgres")?.status).toBe("online");
    expect(health.services.find((s) => s.id === "redis")?.status).toBe("offline");
  });

  it("maps a degraded dependency to the degraded state", async () => {
    stubFetch((path) =>
      path === "/health"
        ? { status: 200, body: LIVENESS }
        : { status: 200, body: readinessReport("Degraded", "Healthy") },
    );

    const health = await fetchSystemHealth();

    expect(health.services.find((s) => s.id === "postgres")?.status).toBe("degraded");
  });

  it("reports dependencies as unknown, not offline, when the API is unreachable", async () => {
    // The browser cannot observe PostgreSQL directly. Claiming it is offline
    // would be an assertion the client has no evidence for.
    stubFetch(() => "network-error");

    const health = await fetchSystemHealth();

    expect(health.services.find((s) => s.id === "backend")?.status).toBe("offline");
    expect(health.services.find((s) => s.id === "postgres")?.status).toBe("unknown");
    expect(health.services.find((s) => s.id === "redis")?.status).toBe("unknown");
  });

  it("rejects a payload that is not a health report", async () => {
    stubFetch(() => ({ status: 200, body: { unexpected: true } }));

    const health = await fetchSystemHealth();

    // Liveness failed to parse, so the backend is reported down rather than
    // silently treated as healthy.
    expect(health.services.find((s) => s.id === "backend")?.status).toBe("offline");
  });
});
