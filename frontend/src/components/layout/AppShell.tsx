import { NavLink, Outlet } from "react-router-dom";

interface NavigationItem {
  readonly to: string;
  readonly label: string;
}

const NAVIGATION: readonly NavigationItem[] = [
  { to: "/", label: "Infrastructure" },
  { to: "/capabilities", label: "Capability Map" },
];

/**
 * The persistent frame around every page: identity bar, primary navigation,
 * and the phase marker.
 */
export function AppShell() {
  return (
    <div className="shell">
      <header className="shell__header">
        <div className="shell__brand">
          <span className="shell__mark" aria-hidden="true">
            PQ
          </span>
          <span className="shell__name">Personal Quant Terminal</span>
          <span className="shell__phase numeric">PHASE 0</span>
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

      <main className="shell__main">
        <Outlet />
      </main>

      <footer className="shell__footer">
        <span>Foundation only — no market data, trading, or broker connection.</span>
        <span className="numeric">LIVE_TRADING_ENABLED=false</span>
      </footer>
    </div>
  );
}
