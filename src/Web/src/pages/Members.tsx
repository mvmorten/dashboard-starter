import { useState } from "react";
import { ApiError } from "../api";
import { Permissions, can } from "../auth/permissions";
import { currentTenant, type Me } from "../auth/me";
import { useMe } from "../auth/useMe";
import { useMediaQuery } from "../layout/useMediaQuery";
import { usePageTitle } from "../layout/usePageTitle";
import type { Member, Role } from "../members/members";
import {
  useChangeMember,
  useMembers,
  type MemberChange,
} from "../members/useMembers";
import NoAccess from "./NoAccess";

// The current tenant's members. Everyone with members.view sees the list; members.manage adds
// a role picker and a disable button per member. The API enforces every rule shown here (not
// yourself, never the tenant's last admin); the page only mirrors them.
export default function Members() {
  usePageTitle("Members");
  const { data: me } = useMe();
  const members = useMembers();
  const change = useChangeMember();
  const [error, setError] = useState<string | null>(null);
  const wide = useMediaQuery("(min-width: 48rem)");

  if (!can(me, Permissions.membersView)) {
    return <NoAccess />;
  }

  if (members.isError) {
    return members.error instanceof ApiError && members.error.status === 403 ? (
      <NoAccess />
    ) : (
      <div role="alert">
        <h1>Members</h1>
        <p className="lead">The server didn&apos;t answer.</p>
        <button type="button" onClick={() => void members.refetch()}>
          Try again
        </button>
      </div>
    );
  }

  const tenant = me ? currentTenant(me) : null;
  const manage = can(me, Permissions.membersManage);

  async function apply(next: MemberChange) {
    setError(null);
    try {
      await change.mutateAsync(next);
    } catch (caught) {
      setError(
        caught instanceof ApiError
          ? (caught.detail ?? caught.title ?? "That didn't work.")
          : "The server didn't answer. Try again in a moment.",
      );
    }
  }

  const controls: ControlsContext = {
    me,
    roles: members.data?.roles ?? [],
    enabled: manage && !change.isPending,
    manage,
    apply,
  };

  return (
    <>
      <h1>Members</h1>
      <p className="lead">
        Everyone in {tenant?.name ?? "this tenant"}, and their role here. A
        person can have a different role in each tenant they belong to.
      </p>

      <div role="alert" className="form-error">
        {error}
      </div>

      {members.isPending ? (
        <p role="status">Loading...</p>
      ) : wide ? (
        <MemberTable members={members.data.members} controls={controls} />
      ) : (
        <MemberCards members={members.data.members} controls={controls} />
      )}
    </>
  );
}

interface ControlsContext {
  me: Me | null | undefined;
  roles: Role[];
  enabled: boolean;
  manage: boolean;
  apply: (change: MemberChange) => Promise<void>;
}

function MemberTable({
  members,
  controls,
}: {
  members: Member[];
  controls: ControlsContext;
}) {
  return (
    <div className="table-scroll">
      <table className="members-table">
        <thead>
          <tr>
            <th scope="col">Member</th>
            <th scope="col">Role</th>
            <th scope="col">Status</th>
            <th scope="col">Last sign-in</th>
            {controls.manage && (
              <th scope="col">
                <span className="visually-hidden">Access</span>
              </th>
            )}
          </tr>
        </thead>
        <tbody>
          {members.map((member) => (
            <tr key={member.userId}>
              <th scope="row">
                <MemberName member={member} controls={controls} />
                <span className="member-email">{member.email}</span>
              </th>
              <td className="member-nowrap">
                <RoleControl member={member} controls={controls} />
              </td>
              <td>
                <StatusBadge member={member} />
              </td>
              <td className="member-nowrap">
                {formatSignIn(member.lastSignInAt)}
              </td>
              {controls.manage && (
                <td>
                  <StatusButton member={member} controls={controls} />
                </td>
              )}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function MemberCards({
  members,
  controls,
}: {
  members: Member[];
  controls: ControlsContext;
}) {
  return (
    <ul className="member-cards">
      {members.map((member) => (
        <li key={member.userId} className="member-card">
          <div className="member-card-head">
            <p className="member-card-name">
              <MemberName member={member} controls={controls} />
            </p>
            <StatusBadge member={member} />
          </div>
          <p className="member-email">{member.email}</p>
          <p className="member-card-meta">
            Last sign-in: {formatSignIn(member.lastSignInAt)}
          </p>
          <div className="member-card-controls">
            <RoleControl member={member} controls={controls} />
            {controls.manage && (
              <StatusButton member={member} controls={controls} />
            )}
          </div>
        </li>
      ))}
    </ul>
  );
}

function isSelf(member: Member, controls: ControlsContext): boolean {
  return member.userId === controls.me?.user.id;
}

function MemberName({
  member,
  controls,
}: {
  member: Member;
  controls: ControlsContext;
}) {
  return (
    <>
      {member.displayName}
      {isSelf(member, controls) && <span className="member-you"> (you)</span>}
    </>
  );
}

// A picker for someone who can manage members, plain text for everyone else.
function RoleControl({
  member,
  controls,
}: {
  member: Member;
  controls: ControlsContext;
}) {
  if (!controls.manage) {
    return <>{member.roleName}</>;
  }

  const self = isSelf(member, controls);
  return (
    <select
      className="role-select"
      aria-label={`Role for ${member.displayName}`}
      value={member.roleId}
      disabled={!controls.enabled || self}
      title={self ? "You can't change your own role." : undefined}
      onChange={(event) =>
        void controls.apply({
          kind: "role",
          member,
          roleId: Number(event.target.value),
        })
      }
    >
      {controls.roles.map((role) => (
        <option key={role.id} value={role.id}>
          {role.name}
        </option>
      ))}
    </select>
  );
}

function StatusButton({
  member,
  controls,
}: {
  member: Member;
  controls: ControlsContext;
}) {
  const self = isSelf(member, controls);
  const active = member.status === "Active";
  return (
    <button
      type="button"
      className="member-status-button"
      disabled={!controls.enabled || self}
      title={self ? "You can't disable yourself." : undefined}
      aria-label={`${active ? "Disable" : "Enable"} ${member.displayName}`}
      onClick={() =>
        void controls.apply({
          kind: "status",
          member,
          status: active ? "Disabled" : "Active",
        })
      }
    >
      {active ? "Disable" : "Enable"}
    </button>
  );
}

function StatusBadge({ member }: { member: Member }) {
  return (
    <span
      className={
        member.status === "Active"
          ? "member-status"
          : "member-status member-status-disabled"
      }
    >
      {member.status}
    </span>
  );
}

function formatSignIn(value: string | null): string {
  if (!value) {
    return "Never";
  }

  // The API sends UTC without a zone marker (datetime2), so add one before converting.
  const utc = /[zZ]|[+-]\d\d:\d\d$/.test(value) ? value : `${value}Z`;
  return new Date(utc).toLocaleString(undefined, {
    dateStyle: "medium",
    timeStyle: "short",
  });
}
