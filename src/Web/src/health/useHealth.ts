import { useQuery } from "@tanstack/react-query";
import { checkHealth } from "./health";

export const healthQueryKey = ["health"] as const;

// The shell uses this too (to notice a newer deploy), so it shares one cached request
// with the health page. TanStack Query re-checks when the tab regains focus, which is
// when a deploy that landed in the background becomes visible.
export function useHealth() {
  return useQuery({
    queryKey: healthQueryKey,
    queryFn: ({ signal }) => checkHealth(signal),
    // checkHealth already turns failures into data; retrying would only delay "apiDown".
    retry: false,
  });
}
