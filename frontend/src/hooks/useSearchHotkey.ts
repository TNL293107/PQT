import { useEffect } from "react";

/**
 * Opens the security search on Ctrl+K, or Cmd+K on a Mac.
 *
 * Registered on the document rather than on a container, because the shortcut
 * has to work wherever focus happens to be — that is the whole point of a
 * keyboard-first terminal.
 *
 * It does not fire while the search is already open. The dialog owns its own
 * keys once it has focus, and re-triggering the opener would reset the query
 * the user is halfway through typing.
 *
 * @param isOpen Whether the search is already showing.
 * @param onOpen Called when the shortcut fires.
 */
export function useSearchHotkey(isOpen: boolean, onOpen: () => void): void {
  useEffect(() => {
    if (isOpen) {
      return;
    }

    const handle = (event: KeyboardEvent) => {
      if (event.key.toLowerCase() !== "k" || !(event.ctrlKey || event.metaKey)) {
        return;
      }

      // Ctrl+K is the browser's focus-the-address-bar shortcut in some
      // browsers, so the default has to go for the terminal to keep it.
      event.preventDefault();
      onOpen();
    };

    document.addEventListener("keydown", handle);
    return () => document.removeEventListener("keydown", handle);
  }, [isOpen, onOpen]);
}
