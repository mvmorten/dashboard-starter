import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { useMe } from "../auth/useMe";
import {
  auditQueryKey,
  customerHistoryQueryKey,
  fetchAudit,
  fetchCustomerHistory,
} from "./audit";

// One page of the current tenant's audit log, for a view of the audit page. While the next
// page or filter loads, the last one stays on screen.
export function useAudit(query: string, enabled: boolean) {
  const { data: me } = useMe();
  return useQuery({
    queryKey: auditQueryKey(me?.currentTenantId ?? null, query),
    queryFn: ({ signal }) => fetchAudit(query, signal),
    placeholderData: keepPreviousData,
    enabled,
    // A 403 means "not for you", and asking again won't change that.
    retry: false,
  });
}

// One page of one customer's history, newest first.
export function useCustomerHistory(id: number, page: number) {
  const { data: me } = useMe();
  return useQuery({
    queryKey: customerHistoryQueryKey(me?.currentTenantId ?? null, id, page),
    queryFn: ({ signal }) => fetchCustomerHistory(id, page, signal),
    placeholderData: keepPreviousData,
    retry: false,
  });
}
