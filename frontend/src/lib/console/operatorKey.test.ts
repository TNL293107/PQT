import { describe, expect, it } from "vitest";
import { readOperatorKey, writeOperatorKey } from "./operatorKey";

function memoryStorage(): Storage {
  const held = new Map<string, string>();

  return {
    get length() {
      return held.size;
    },
    clear: () => held.clear(),
    getItem: (key) => held.get(key) ?? null,
    key: (index) => [...held.keys()][index] ?? null,
    removeItem: (key) => void held.delete(key),
    setItem: (key, value) => void held.set(key, value),
  };
}

describe("operator key storage", () => {
  it("holds a key and forgets it", () => {
    const storage = memoryStorage();

    expect(readOperatorKey(storage)).toBeNull();
    expect(writeOperatorKey("k".repeat(40), storage)).toBe(true);
    expect(readOperatorKey(storage)).toBe("k".repeat(40));

    writeOperatorKey(null, storage);
    expect(readOperatorKey(storage)).toBeNull();
  });

  it("reads storage that throws as holding no key", () => {
    // Some browsers throw on any access with storage disabled.
    const hostile = {
      getItem: () => {
        throw new Error("denied");
      },
      setItem: () => {
        throw new Error("denied");
      },
    } as unknown as Storage;

    expect(readOperatorKey(hostile)).toBeNull();
    expect(writeOperatorKey("k".repeat(40), hostile)).toBe(false);
  });

  it("uses the tab's session storage by default, not local storage", () => {
    writeOperatorKey("s".repeat(40));

    expect(window.sessionStorage.getItem("pqt.operatorKey")).toBe("s".repeat(40));
    expect(window.localStorage.getItem("pqt.operatorKey")).toBeNull();

    writeOperatorKey(null);
  });
});
