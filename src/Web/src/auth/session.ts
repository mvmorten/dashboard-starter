// Sign in, sign out, and switch tenants: the calls that change who the session belongs to.
// Each one is followed by a fresh antiforgery token, because the token is tied to the user
// and tenant it was issued for.

import type { QueryClient } from "@tanstack/react-query";
import { apiGet, apiPost, refreshAntiforgeryToken } from "../api";
import { meQueryKey, type Me } from "./me";

export async function signIn(
  queryClient: QueryClient,
  email: string,
  password: string,
): Promise<Me> {
  // A wrong password is a 401 we show on the form, not a session that ended.
  const me = await apiPost<Me>(
    "/api/auth/sign-in",
    { email, password },
    { unauthorizedIsAnswer: true },
  );
  await refreshAntiforgeryToken();

  // Nothing cached from before belongs to this user.
  queryClient.clear();
  queryClient.setQueryData(meQueryKey, me);
  return me;
}

export async function signOut(queryClient: QueryClient): Promise<void> {
  try {
    await apiPost<void>("/api/auth/sign-out", undefined, {
      unauthorizedIsAnswer: true,
    });
  } finally {
    // Signed out either way: if the session had already ended, the API said 401.
    queryClient.clear();
    queryClient.setQueryData(meQueryKey, null);
    await refreshAntiforgeryToken().catch(() => {});
  }
}

export async function switchTenant(
  queryClient: QueryClient,
  tenantId: number,
): Promise<void> {
  const me = await apiPost<Me>("/api/auth/switch-tenant", { tenantId });
  await refreshAntiforgeryToken();

  // Everything else on screen belonged to the previous tenant.
  queryClient.setQueryData(meQueryKey, me);
  await queryClient.invalidateQueries({
    predicate: (query) => query.queryKey[0] !== meQueryKey[0],
  });
}

// GET /api/auth/sign-in-info: the public demo account, when this deployment has one, and
// whether it can send password reset emails. Both come from the API's configuration, not
// from this bundle, so a fork without a demo or email shows neither.
export interface SignInInfo {
  demoAccount: { email: string; password: string } | null;
  emailEnabled: boolean;
}

export function fetchSignInInfo(signal: AbortSignal): Promise<SignInInfo> {
  return apiGet<SignInInfo>("/api/auth/sign-in-info", { signal });
}

// Only a path on this site: "/health?x=1" yes; "https://evil.example", "//evil.example",
// and "/\evil.example" (which some browsers treat as "//") no. Anything else goes home,
// so a crafted sign-in link can't send someone to another site after they sign in.
export function safeReturnUrl(value: string | null): string {
  if (!value || !value.startsWith("/") || value.startsWith("//")) {
    return "/";
  }

  if (value.startsWith("/\\")) {
    return "/";
  }

  try {
    const url = new URL(value, window.location.origin);
    if (url.origin !== window.location.origin || url.pathname === "/sign-in") {
      return "/";
    }

    return url.pathname + url.search + url.hash;
  } catch {
    return "/";
  }
}
