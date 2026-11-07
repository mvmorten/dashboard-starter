import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  changeRole,
  changeStatus,
  fetchMembers,
  membersQueryKey,
  type Member,
} from "./members";

// The current tenant's members and the roles they can have. Switching tenants refetches it
// (auth/session.ts invalidates everything but /me).
export function useMembers() {
  return useQuery({
    queryKey: membersQueryKey,
    queryFn: ({ signal }) => fetchMembers(signal),
    // A 403 means "not for you", and asking again won't change that.
    retry: false,
  });
}

export type MemberChange =
  | { kind: "role"; member: Member; roleId: number }
  | { kind: "status"; member: Member; status: Member["status"] };

// One change at a time, then reload the list, so every row has its new RowVersion. A refused
// change (409: someone else got there first, or the tenant's last admin) reloads it too.
export function useChangeMember() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (change: MemberChange) =>
      change.kind === "role"
        ? changeRole(change.member, change.roleId)
        : changeStatus(change.member, change.status),
    onSettled: () =>
      queryClient.invalidateQueries({ queryKey: membersQueryKey }),
  });
}
