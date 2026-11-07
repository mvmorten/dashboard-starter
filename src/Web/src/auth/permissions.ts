// The front end's copy of the permission names in src/Db/Authorization/Permissions.cs, and one
// helper to ask about them. Hiding a button the user can't use is a convenience; the API checks
// the permission again on every request, so nothing here is the security boundary.

import type { Me } from "./me";
import { useMe } from "./useMe";

export const Permissions = {
  membersView: "members.view",
  membersManage: "members.manage",
} as const;

export type Permission = (typeof Permissions)[keyof typeof Permissions];

// Does the signed-in user have this permission in the current tenant?
export function can(
  me: Me | null | undefined,
  permission: Permission,
): boolean {
  return me?.permissions.includes(permission) ?? false;
}

// can() for the signed-in user, in a component: const allowed = useCan();
export function useCan(): (permission: Permission) => boolean {
  const { data: me } = useMe();
  return (permission) => can(me, permission);
}
