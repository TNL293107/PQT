import { Route, Routes } from "react-router-dom";
import { AppShell } from "./components/layout/AppShell";
import { CapabilitiesPage } from "./pages/CapabilitiesPage";
import { NotFoundPage } from "./pages/NotFoundPage";
import { SystemStatusPage } from "./pages/SystemStatusPage";

/** Route table for the terminal. */
export function App() {
  return (
    <Routes>
      <Route element={<AppShell />}>
        <Route index element={<SystemStatusPage />} />
        <Route path="capabilities" element={<CapabilitiesPage />} />
        <Route path="*" element={<NotFoundPage />} />
      </Route>
    </Routes>
  );
}
