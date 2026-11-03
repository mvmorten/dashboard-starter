import { useQuery } from "@tanstack/react-query";
import { fetchMe, meQueryKey } from "./me";

// Who is signed in, their tenants, and the current one. The protected layout, the top bar,
// and the sign-in page share this one cached request.
export function useMe() {
  return useQuery({
    queryKey: meQueryKey,
    queryFn: ({ signal }) => fetchMe(signal),
  });
}
