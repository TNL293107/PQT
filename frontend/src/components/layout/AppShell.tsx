import { useCallback, useRef, useState } from "react";
import { NavLink, Outlet } from "react-router-dom";
import { useCurrentSecurity } from "../../context/currentSecurity";
import { useSearchHotkey } from "../../hooks/useSearchHotkey";
import type { Instrument } from "../../types/instrument";
import { InstrumentSearchDialog } from "../search/InstrumentSearchDialog";
import { CurrentSecurityBar } from "./CurrentSecurityBar";

interface NavigationItem {
  readonly to: string;
  readonly label: string;
}

const NAVIGATION: readonly NavigationItem[] = [
  { to: "/", label: "Console" },
  { to: "/infrastructure", label: "Infrastructure" },
  { to: "/capabilities", label: "Capability Map" },
];

/**
 * The persistent frame around every page: identity bar, primary navigation,
 * the current security, and the search overlay that sets it.
 */
export function AppShell() {
  const [isSearchOpen, setSearchOpen] = useState(false);
  const { select } = useCurrentSecurity();

  // Where focus was when the overlay opened, so closing it puts focus back
  // rather than dropping the user at the top of the document.
  const restoreFocusTo = useRef<HTMLElement | null>(null);

  const openSearch = useCallback(() => {
    restoreFocusTo.current = document.activeElement as HTMLElement | null;
    setSearchOpen(true);
  }, []);

  const closeSearch = useCallback(() => {
    setSearchOpen(false);
    restoreFocusTo.current?.focus();
  }, []);

  const selectSecurity = useCallback(
    (instrument: Instrument) => {
      select(instrument);
      closeSearch();
    },
    [select, closeSearch],
  );

  useSearchHotkey(isSearchOpen, openSearch);

  return (
    <div className="shell">
      <header className="shell__header">
        <div className="shell__brand">
          <span className="shell__mark" aria-hidden="true">
            PQ
          </span>
          <span className="shell__name">Personal Quant Terminal</span>
          <span className="shell__phase numeric">PHASE 1</span>
        </div>

        <nav className="shell__nav" aria-label="Primary">
          {NAVIGATION.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.to === "/"}
              className={({ isActive }) =>
                isActive ? "shell__link shell__link--active" : "shell__link"
              }
            >
              {item.label}
            </NavLink>
          ))}
        </nav>
      </header>

      <CurrentSecurityBar onOpenSearch={openSearch} />

      <main className="shell__main">
        <Outlet />
      </main>

      <footer className="shell__footer">
        <span>Read-only. Every write — ingest, resolve a finding — belongs to the operator CLI.</span>
        <span className="numeric">LIVE_TRADING_ENABLED=false</span>
      </footer>

      {isSearchOpen ? (
        <InstrumentSearchDialog onSelect={selectSecurity} onClose={closeSearch} />
      ) : null}
    </div>
  );
}
