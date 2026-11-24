import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { useMe } from "../auth/useMe";
import { apiQuery, type ListSpec, type ListState } from "../grid/listState";
import {
  customerListQueryKey,
  customerQueryKey,
  deletedCustomerQueryKey,
  fetchCustomer,
  fetchCustomers,
  fetchDeletedCustomer,
} from "./customers";

// One page of the current tenant's customers, for a view of the list (grid/listState.ts).
// While the next page, search, filter or sort loads, the last one stays on screen instead of a
// flash of "Loading...".
export function useCustomers(view: ListState, spec: ListSpec) {
  const { data: me } = useMe();
  const query = apiQuery(view, spec).toString();
  return useQuery({
    queryKey: customerListQueryKey(me?.currentTenantId ?? null, query),
    queryFn: ({ signal }) => fetchCustomers(query, signal),
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

// A deleted customer, for the page that shows it with Restore. Only asked once the plain
// lookup has said 404, and only by someone with customers.edit (the caller decides).
export function useDeletedCustomer(id: number, enabled: boolean) {
  const { data: me } = useMe();
  return useQuery({
    queryKey: deletedCustomerQueryKey(me?.currentTenantId ?? null, id),
    queryFn: ({ signal }) => fetchDeletedCustomer(id, signal),
    enabled,
    retry: false,
  });
}
