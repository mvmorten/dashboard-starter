import { useQuery } from "@tanstack/react-query";
import { apiGet } from "../api";

// Mirrors TenantResponse in src/Api/Tenants/TenantResponse.cs.
export interface Tenant {
  id: number;
  name: string;
}

export const tenantsQueryKey = ["tenants"] as const;

// STEP 5: this is the stub boundary. Today GET /api/tenants lists every tenant in the
// database (src/Api/Tenants/TenantsController.cs). In step 5 the API returns only the
// tenants the signed-in user belongs to; this hook and its callers shouldn't need to change.
export function useTenants() {
  return useQuery({
    queryKey: tenantsQueryKey,
    queryFn: ({ signal }) => apiGet<Tenant[]>("/api/tenants", { signal }),
  });
}
