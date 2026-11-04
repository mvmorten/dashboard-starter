// Types and fetch for GET /api/auth/me (src/Api/Auth/AuthController.cs). Mirrors
// MeResponse in src/Api/Auth/AuthModels.cs.

import { ApiError, apiGet } from "../api";

export interface Tenant {
  id: number;
  name: string;
}

export interface Me {
  user: {
    id: number;
    email: string;
    displayName: string;
    isDemo: boolean;
  };
  // Every tenant the user is an active member of, by name.
  tenants: Tenant[];
  // The tenant this session works in. Null when the user belongs to none.
  currentTenantId: number | null;
}

export const meQueryKey = ["me"] as const;

// Null means "nobody is signed in". That's an answer, not an error, so it comes back as
// data and the protected layout can send the user to the sign-in page.
export async function fetchMe(signal: AbortSignal): Promise<Me | null> {
  try {
    return await apiGet<Me>("/api/auth/me", {
      signal,
      unauthorizedIsAnswer: true,
    });
  } catch (error) {
    if (error instanceof ApiError && error.status === 401) {
      return null;
    }

    throw error;
  }
}

export function currentTenant(me: Me): Tenant | null {
  return me.tenants.find((tenant) => tenant.id === me.currentTenantId) ?? null;
}
