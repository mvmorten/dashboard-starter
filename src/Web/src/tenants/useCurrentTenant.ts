import { useSyncExternalStore } from "react";
import { getTenantChoice, subscribeToTenantChoice } from "./tenantChoice";
import { useTenants, type Tenant } from "./useTenants";

// The tenant picked in the top bar, or undefined while tenants load or when there are none.
// A saved id that no longer exists falls back to the first tenant.
// STEP 5: the current tenant will come from the signed-in session instead of this choice.
export function useCurrentTenant(): Tenant | undefined {
  const { data: tenants } = useTenants();
  const chosenId = useSyncExternalStore(
    subscribeToTenantChoice,
    getTenantChoice,
  );

  return tenants?.find((tenant) => tenant.id === chosenId) ?? tenants?.[0];
}
