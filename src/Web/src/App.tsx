import { Route, Routes } from "react-router";
import AppShell from "./layout/AppShell";
import Dashboard from "./pages/Dashboard";
import Health from "./pages/Health";
import NotFound from "./pages/NotFound";

export default function App() {
  return (
    <AppShell>
      <Routes>
        <Route path="/" element={<Dashboard />} />
        <Route path="/health" element={<Health />} />
        <Route path="*" element={<NotFound />} />
      </Routes>
    </AppShell>
  );
}
