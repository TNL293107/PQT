import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { SystemStatusPage } from "./SystemStatusPage";
import type { HealthReport } from "../types/health";

const LIVENESS: HealthReport = {
  status: "Healthy",
  totalDurationMs: 1.2,
  checks: [{ name: "self", status: "Healthy", durationMs: 0.1 }],
};

const READINESS_HEALTHY: HealthReport = {
  status: "Healthy",
  totalDurationMs: 6.3,
  checks: [
    { name: "postgres", status: "Healthy", durationMs: 4.1 },
    { name: "redis", status: "Healthy", durationMs: 2.2 },
  ],
};

function stubHealthyApi() {
  vi.stubGlobal(
    "fetch",
    vi.fn((input: string) => {
      const path = new URL(input, "http://localhost").pathname;
      const body = path === "/health" ? LIVENESS : READINESS_HEALTHY;
      return Promise.resolve(
        new Response(JSON.stringify(body), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        }),
      );
    }),
  );
}

function statusOf(serviceId: string): string {
  const row = screen.getByTestId(`status-row-${serviceId}`);
  return within(row).getByTestId("status-pill").textContent ?? "";
}

describe("SystemStatusPage", () => {
  it("shows a loading state before the first response arrives", () => {
    vi.stubGlobal("fetch", vi.fn(() => new Promise(() => {})));

    render(<SystemStatusPage />);

    expect(screen.getByRole("status")).toHaveTextContent(/querying system status/i);
  });

  it("renders one row per service once the health report arrives", async () => {
    stubHealthyApi();

    render(<SystemStatusPage />);

    await waitFor(() => {
      expect(screen.getByRole("table")).toBeInTheDocument();
    });

    expect(screen.getByText("Backend API")).toBeInTheDocument();
    expect(screen.getByText("PostgreSQL")).toBeInTheDocument();
    expect(screen.getByText("Redis")).toBeInTheDocument();
  });

  it("reports every dependency as online when the API is fully healthy", async () => {
    stubHealthyApi();

    render(<SystemStatusPage />);

    await waitFor(() => {
      expect(screen.getByRole("table")).toBeInTheDocument();
    });

    expect(statusOf("backend")).toBe("ONLINE");
    expect(statusOf("postgres")).toBe("ONLINE");
    expect(statusOf("redis")).toBe("ONLINE");
  });

  it("shows the backend offline and dependencies unknown when the API is down", async () => {
    vi.stubGlobal("fetch", vi.fn(() => Promise.reject(new Error("Failed to fetch"))));

    render(<SystemStatusPage />);

    await waitFor(() => {
      expect(screen.getByRole("table")).toBeInTheDocument();
    });

    expect(statusOf("backend")).toBe("OFFLINE");
    expect(statusOf("postgres")).toBe("UNKNOWN");
  });

  it("explains the failure and offers a retry when the API is unreachable", async () => {
    vi.stubGlobal("fetch", vi.fn(() => Promise.reject(new Error("Failed to fetch"))));

    render(<SystemStatusPage />);

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent(/cannot reach the api/i);
    expect(alert).toHaveTextContent(/failed to fetch/i);
    expect(screen.getByRole("button", { name: /retry/i })).toBeInTheDocument();
  });

  it("recovers when the API comes back and the user retries", async () => {
    const user = userEvent.setup();
    let apiIsDown = true;

    vi.stubGlobal(
      "fetch",
      vi.fn((input: string) => {
        if (apiIsDown) {
          return Promise.reject(new Error("Failed to fetch"));
        }
        const path = new URL(input, "http://localhost").pathname;
        const body = path === "/health" ? LIVENESS : READINESS_HEALTHY;
        return Promise.resolve(
          new Response(JSON.stringify(body), {
            status: 200,
            headers: { "Content-Type": "application/json" },
          }),
        );
      }),
    );

    render(<SystemStatusPage />);

    const retry = await screen.findByRole("button", { name: /retry/i });
    apiIsDown = false;
    await user.click(retry);

    await waitFor(() => {
      expect(statusOf("backend")).toBe("ONLINE");
    });
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });
});
