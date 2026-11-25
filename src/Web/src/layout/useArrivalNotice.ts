import { useEffect, useState } from "react";
import { useLocation, useNavigate } from "react-router";

// A one-time message from the page that sent us here ("Deleted: Acme", "Customer added."),
// passed in router history state: navigate(path, { state: { notice: "..." } }).
//
// The notice is copied out of history state when the page opens, and the state is then removed
// from the history entry (the same URL, replaced, without it). History state survives a
// reload, so leaving it there would show the notice again on every reload.
//
// Returns the state the page arrived with (null if none, or once dropped), and a function to
// drop it when it no longer applies.
export function useArrivalNotice<Notice extends { notice: string }>(): [
  Notice | null,
  () => void,
] {
  const location = useLocation();
  const navigate = useNavigate();
  const [arrivedWith, setArrivedWith] = useState<Notice | null>(() =>
    hasNotice(location.state) ? (location.state as Notice) : null,
  );

  useEffect(() => {
    if (hasNotice(location.state)) {
      void navigate(
        {
          pathname: location.pathname,
          search: location.search,
          hash: location.hash,
        },
        { replace: true, state: null },
      );
    }
  }, [location, navigate]);

  return [arrivedWith, () => setArrivedWith(null)];
}

function hasNotice(state: unknown): boolean {
  return (
    typeof state === "object" &&
    state !== null &&
    "notice" in state &&
    typeof (state as { notice: unknown }).notice === "string"
  );
}
