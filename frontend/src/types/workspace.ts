/** A saved workspace: console lines, as asked, in replay order. */
export interface Workspace {
  readonly name: string;
  readonly entries: readonly string[];
  readonly createdAtUtc: string;
  readonly updatedAtUtc: string;
}

/** Every workspace. */
export interface WorkspaceList {
  readonly results: readonly Workspace[];
}
