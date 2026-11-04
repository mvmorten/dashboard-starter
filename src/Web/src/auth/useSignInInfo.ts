import { useQuery } from "@tanstack/react-query";
import { fetchSignInInfo } from "./session";

export function useSignInInfo() {
  return useQuery({
    queryKey: ["sign-in-info"],
    queryFn: ({ signal }) => fetchSignInInfo(signal),
    // Configuration, not data: it doesn't change while the page is open.
    staleTime: Infinity,
  });
}
