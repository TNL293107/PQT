/**
 * The operator key, held for this browser tab only (ADR-027).
 *
 * `sessionStorage`, never `localStorage`, a cookie or the URL: the key is the
 * whole of the deployment's write permission, and a tab is the narrowest
 * place a browser will keep it. Closing the tab forgets it.
 */

const STORAGE_KEY = "pqt.operatorKey";

/** The shortest key the API accepts; a shorter one cannot be right. */
export const MINIMUM_KEY_LENGTH = 32;

/** Reads the key this tab holds, or null. */
export function readOperatorKey(storage: Storage | null = sessionStorageOrNull()): string | null {
  try {
    const value = storage?.getItem(STORAGE_KEY) ?? null;
    return value === null || value === "" ? null : value;
  } catch {
    // Storage can be disabled outright; that is the same as holding no key.
    return null;
  }
}

/** Holds a key for this tab, or forgets it when given null. */
export function writeOperatorKey(
  key: string | null,
  storage: Storage | null = sessionStorageOrNull(),
): boolean {
  try {
    if (storage === null) {
      return false;
    }

    if (key === null) {
      storage.removeItem(STORAGE_KEY);
    } else {
      storage.setItem(STORAGE_KEY, key);
    }

    return true;
  } catch {
    return false;
  }
}

function sessionStorageOrNull(): Storage | null {
  try {
    return typeof window === "undefined" ? null : window.sessionStorage;
  } catch {
    return null;
  }
}
