import { Route, Routes } from "react-router";
import RequireSignIn from "./layout/RequireSignIn";
import Dashboard from "./pages/Dashboard";
import Health from "./pages/Health";
import NotFound from "./pages/NotFound";
import SignIn from "./pages/SignIn";

// /sign-in stands alone. Every other page is inside RequireSignIn, which shows the app
// shell to a signed-in user and sends anyone else to sign in.
export default function App() {
  return (
    <Routes>
      <Route path="/sign-in" element={<SignIn />} />
      <Route element={<RequireSignIn />}>
        <Route path="/" element={<Dashboard />} />
        <Route path="/health" element={<Health />} />
        <Route path="*" element={<NotFound />} />
      </Route>
    </Routes>
  );
}
