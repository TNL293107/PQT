/**
 * Resolves the API base URL.
 *
 * Supplied at build time by `VITE_API_BASE_URL`. When it is absent the client
 * falls back to same-origin requests, which is what a reverse-proxied
 * deployment wants.
 */
export function resolveApiBaseUrl(): string {
  const configured = import.meta.env.VITE_API_BASE_URL;

  if (typeof configured !== "string" || configured.trim() === "") {
    return "";
  }

  // A trailing slash would produce '//health' when joined with a path.
  return configured.trim().replace(/\/+$/, "");
}

/** Builds an absolute (or same-origin relative) URL for an API path. */
export function apiUrl(path: string): string {
  const normalised = path.startsWith("/") ? path : `/${path}`;
  return `${resolveApiBaseUrl()}${normalised}`;
}
