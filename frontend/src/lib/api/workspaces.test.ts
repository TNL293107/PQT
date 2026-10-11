import { afterEach, describe, expect, it, vi } from "vitest";
import { saveWorkspace } from "./workspaces";

afterEach(() => vi.unstubAllGlobals());

describe("workspace client", () => {
  it("saves the lines with PUT under the name, carrying the key", async () => {
    const fetchMock = vi.fn(() =>
      Promise.resolve(
        new Response(
          JSON.stringify({ name: "morning", entries: ["FPT GP"], createdAtUtc: "", updatedAtUtc: "" }),
          { status: 201, headers: { "Content-Type": "application/json" } },
        ),
      ),
    );
    vi.stubGlobal("fetch", fetchMock);

    const saved = await saveWorkspace("morning", ["FPT GP"], "k".repeat(40));

    const [url, init] = (fetchMock.mock.calls as unknown as [string, RequestInit][])[0]!;
    expect(url).toMatch(/\/workspaces\/morning$/);
    expect(init.method).toBe("PUT");
    expect(init.body).toBe(JSON.stringify({ entries: ["FPT GP"] }));
    expect((init.headers as Record<string, string>)["Authorization"]).toBe(`Bearer ${"k".repeat(40)}`);
    expect(saved.entries).toEqual(["FPT GP"]);
  });
});
