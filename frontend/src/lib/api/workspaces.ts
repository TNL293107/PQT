import type { Workspace, WorkspaceList } from "../../types/workspace";
import { send } from "./watchlists";

/** Lists every saved workspace. */
export function fetchWorkspaces(signal?: AbortSignal): Promise<WorkspaceList> {
  return send<WorkspaceList>("GET", "/workspaces", null, signal);
}

/** Reads one saved workspace. */
export function fetchWorkspace(name: string, signal?: AbortSignal): Promise<Workspace> {
  return send<Workspace>("GET", `/workspaces/${encodeURIComponent(name)}`, null, signal);
}

/** Saves a workspace, creating it or replacing its lines. Needs the operator key. */
export function saveWorkspace(
  name: string,
  entries: readonly string[],
  key: string | null,
  signal?: AbortSignal,
): Promise<Workspace> {
  return send<Workspace>("PUT", `/workspaces/${encodeURIComponent(name)}`, key, signal, { entries });
}

/** Deletes a workspace. Needs the operator key. */
export async function deleteWorkspace(name: string, key: string | null, signal?: AbortSignal): Promise<void> {
  await send<null>("DELETE", `/workspaces/${encodeURIComponent(name)}`, key, signal);
}
