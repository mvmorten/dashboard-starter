// Types and calls for the audit log: GET /api/audit (src/Api/Audit/AuditController.cs) and
// GET /api/customers/{id}/history. Mirrors AuditModels.cs. The tenant is always the session's
// current one; no tenant id is sent.

import { apiGet } from "../api";
import { customersQueryKey } from "../customers/customers";
import {
  writeListState,
  type ListSpec,
  type ListState,
} from "../grid/listState";
import type { Changes } from "./changes";

export interface AuditEntry {
  id: number;
  // ISO 8601, UTC, without a zone marker (format.ts adds it).
  occurredAt: string;
  // Who did it. Null when no person did (the Db runner's set-role).
  userId: number | null;
  userName: string | null;
  entityType: "Customer" | "Member";
  // The customer's id, or for a Member the member's user id.
  entityId: number;
  // Its name as it is now. Null if it can't be found.
  entityName: string | null;
  // A customer that is deleted now.
  entityDeleted: boolean;
  action: "Created" | "Updated" | "Deleted" | "Restored";
  changes: Changes | null;
}

export interface AuditList {
  items: AuditEntry[];
  total: number;
  page: number;
  pageSize: number;
}

export const entityTypes = ["Customer", "Member"] as const;

// What the audit page can filter by: the values GET /api/audit accepts, which says 400 to
// anything else. The user filter's values are the tenant's members, so the page passes their
// ids in. There's no sort to choose: the log is newest first.
export function auditListSpec(userIds: readonly string[]): ListSpec {
  return {
    sorts: [],
    defaultSort: "",
    filters: { entityType: entityTypes, userId: userIds },
    paged: true,
    searchable: false,
  };
}

// The query for the API: the filters and the page. (grid/listState.ts's apiQuery would add a
// sort, and this list has none.)
export function auditQuery(state: ListState, spec: ListSpec): string {
  const params = writeListState(state, spec);
  params.set("page", String(state.page));
  return params.toString();
}

export function auditQueryKey(tenantId: number | null, query: string) {
  return ["audit", tenantId, query] as const;
}

// Under the customers key, so the invalidate after any customer change refreshes it too.
export function customerHistoryQueryKey(
  tenantId: number | null,
  id: number,
  page: number,
) {
  return [...customersQueryKey, tenantId, "history", id, page] as const;
}

export function fetchAudit(
  query: string,
  signal: AbortSignal,
): Promise<AuditList> {
  return apiGet<AuditList>(`/api/audit?${query}`, { signal });
}

export function fetchCustomerHistory(
  id: number,
  page: number,
  signal: AbortSignal,
): Promise<AuditList> {
  return apiGet<AuditList>(`/api/customers/${id}/history?page=${page}`, {
    signal,
  });
}
