import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { useMe } from "../auth/useMe";
import {
  customerListQueryKey,
  customerQueryKey,
  fetchCustomer,
  fetchCustomers,
} from "./customers";

// One page of the current tenant's customers. While the next page or search loads, the last
// one stays on screen instead of a flash of "Loading...".
export function useCustomers(search: string, page: number) {
  const { data: me } = useMe();
  return useQuery({
    queryKey: customerListQueryKey(me?.currentTenantId ?? null, search, page),
    queryFn: ({ signal }) => fetchCustomers(search, page, signal),
    placeholderData: keepPreviousData,
    // A 403 means "not for you", and asking again won't change that.
    retry: false,
  });
}

// One customer, for the form. The form copies it into its own state once (CustomerForm.tsx),
// so a refetch in the background never overwrites what someone is typing.
export function useCustomer(id: number) {
  const { data: me } = useMe();
  return useQuery({
    queryKey: customerQueryKey(me?.currentTenantId ?? null, id),
    queryFn: ({ signal }) => fetchCustomer(id, signal),
    // 404 (no such customer, or deleted) and 403 won't change by asking again.
    retry: false,
  });
}
