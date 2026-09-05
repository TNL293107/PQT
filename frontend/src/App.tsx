import { Route, Routes } from "react-router-dom";
import { AppShell } from "./components/layout/AppShell";
import { CurrentSecurityProvider } from "./context/CurrentSecurityProvider";
import { CapabilitiesPage } from "./pages/CapabilitiesPage";
import { ConsolePage } from "./pages/ConsolePage";
import { NotFoundPage } from "./pages/NotFoundPage";
import { SystemStatusPage } from "./pages/SystemStatusPage";

/**
 * Route table for the terminal.
 *
 * The current-security provider sits outside the routes on purpose: the
 * selected security is terminal state, not page state, and must survive
 * navigation between views.
 */
export function App() {
  return (
    <CurrentSecurityProvider>
      <Routes>
        <Route element={<AppShell />}>
          <Route index element={<ConsolePage />} />
          <Route path="infrastructure" element={<SystemStatusPage />} />
          <Route path="capabilities" element={<CapabilitiesPage />} />
          <Route path="*" element={<NotFoundPage />} />
        </Route>
      </Routes>
    </CurrentSecurityProvider>
  );
}
