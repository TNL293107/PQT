import { afterEach, describe, expect, it, vi } from "vitest";
import { apiUrl } from "./config";

describe("apiUrl", () => {
  afterEach(() => {
    vi.unstubAllEnvs();
  });

  it("prefixes the configured base URL", () => {
    vi.stubEnv("VITE_API_BASE_URL", "http://localhost:8080");

    expect(apiUrl("/health")).toBe("http://localhost:8080/health");
  });

  it("does not produce a double slash when the base URL has a trailing slash", () => {
    vi.stubEnv("VITE_API_BASE_URL", "http://localhost:8080/");

    expect(apiUrl("/health")).toBe("http://localhost:8080/health");
  });

  it("falls back to a same-origin path when no base URL is configured", () => {
    // This is the reverse-proxy deployment shape, where the API is served
    // under the same origin as the terminal.
    vi.stubEnv("VITE_API_BASE_URL", "");

    expect(apiUrl("/health")).toBe("/health");
  });

  it("normalises a path given without a leading slash", () => {
    vi.stubEnv("VITE_API_BASE_URL", "http://localhost:8080");

    expect(apiUrl("health")).toBe("http://localhost:8080/health");
  });
});
