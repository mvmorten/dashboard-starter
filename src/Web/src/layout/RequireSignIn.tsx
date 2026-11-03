import { Navigate, Outlet, useLocation } from "react-router";
import { useMe } from "../auth/useMe";
import AppShell from "./AppShell";

// Wraps every page except sign-in. Nobody signed in: off to /sign-in, with the page they
// asked for in returnUrl so they come back to it. The API enforces the same rule on every
// request; this only decides what to show.
export default function RequireSignIn() {
  const { data: me, isPending, isError, refetch } = useMe();
  const location = useLocation();

  if (isPending) {
    return (
      <p className="page-status" role="status">
        Loading...
      </p>
    );
  }

  if (isError) {
    return (
      <div className="page-status" role="alert">
        <p>The server didn&apos;t answer.</p>
        <button type="button" onClick={() => refetch()}>
          Try again
        </button>
      </div>
    );
  }

  if (!me) {
    const here = location.pathname + location.search + location.hash;
    const target =
      here === "/"
        ? "/sign-in"
        : `/sign-in?returnUrl=${encodeURIComponent(here)}`;
    return <Navigate to={target} replace />;
  }

  return (
    <AppShell>
      <Outlet />
    </AppShell>
  );
}
