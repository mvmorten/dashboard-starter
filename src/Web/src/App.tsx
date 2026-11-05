import { Route, Routes } from "react-router";
import RequireSignIn from "./layout/RequireSignIn";
import ChangePassword from "./pages/ChangePassword";
import Dashboard from "./pages/Dashboard";
import ForgotPassword from "./pages/ForgotPassword";
import Health from "./pages/Health";
import NotFound from "./pages/NotFound";
import ResetPassword from "./pages/ResetPassword";
import SignIn from "./pages/SignIn";

// The pages for getting in (sign in, forgot and reset password) stand alone. Every other
// page is inside RequireSignIn, which shows the app shell to a signed-in user and sends
// anyone else to sign in.
export default function App() {
  return (
    <Routes>
      <Route path="/sign-in" element={<SignIn />} />
      <Route path="/forgot-password" element={<ForgotPassword />} />
      <Route path="/reset-password" element={<ResetPassword />} />
      <Route element={<RequireSignIn />}>
        <Route path="/" element={<Dashboard />} />
        <Route path="/health" element={<Health />} />
        <Route path="/change-password" element={<ChangePassword />} />
        <Route path="*" element={<NotFound />} />
      </Route>
    </Routes>
  );
}
