// Types and calls for /api/members (src/Api/Members/MembersController.cs). Mirrors
// MemberModels.cs. The tenant is always the session's current one; no tenant id is sent.

import { apiGet, apiPut } from "../api";

export interface Member {
  userId: number;
  displayName: string;
  email: string;
  roleId: number;
  roleName: string;
  status: "Active" | "Disabled";
  // The account's last sign-in, to any tenant (ISO 8601, UTC). Null: never.
  lastSignInAt: string | null;
  // Sent back with a change, so two admins can't overwrite each other's edits (base64).
  rowVersion: string;
}

export interface Role {
  id: number;
  name: string;
  description: string;
}

export interface MembersResponse {
  members: Member[];
  roles: Role[];
}

export const membersQueryKey = ["members"] as const;

export function fetchMembers(signal: AbortSignal): Promise<MembersResponse> {
  return apiGet<MembersResponse>("/api/members", { signal });
}

export function changeRole(member: Member, roleId: number): Promise<unknown> {
  return apiPut(`/api/members/${member.userId}/role`, {
    roleId,
    rowVersion: member.rowVersion,
  });
}

export function changeStatus(
  member: Member,
  status: Member["status"],
): Promise<unknown> {
  return apiPut(`/api/members/${member.userId}/status`, {
    status,
    rowVersion: member.rowVersion,
  });
}
