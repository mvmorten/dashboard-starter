import { useState } from "react";
import { ApiError } from "../api";
import { Permissions, can } from "../auth/permissions";
import { currentTenant, type Me } from "../auth/me";
import { useMe } from "../auth/useMe";
import { formatUtc, formatUtcDate } from "../format";
import { visibleKeys } from "../grid/columns";
import { Email } from "../grid/Email";
import {
  ColumnPicker,
  DataTable,
  SortSelect,
  type GridColumn,
} from "../grid/Grid";
import {
  defaultListState,
  firstDirOf,
  isDefaultView,
  sortBy,
  withSort,
  type ListSpec,
} from "../grid/listState";
import { sortRows, type SortValue } from "../grid/sortRows";
import { useColumnChoices } from "../grid/useColumnChoices";
import { useListState } from "../grid/useListState";
import { useMediaQuery } from "../layout/useMediaQuery";
import { usePageTitle } from "../layout/usePageTitle";
import type { Member, Role } from "../members/members";
import {
  useChangeMember,
  useMembers,
  type MemberChange,
} from "../members/useMembers";
import NoAccess from "./NoAccess";

// Members sorts in the browser: GET /api/members sends the whole tenant at once (there's no
// paging until invites make lists long), so asking the server again for another order would
// fetch the same rows. The sort is in the URL all the same (?sort=&dir=), like Customers.
const memberListSpec: ListSpec = {
  sorts: [
    { key: "name", firstDir: "asc" },
    { key: "email", firstDir: "asc" },
    { key: "role", firstDir: "asc" },
    { key: "status", firstDir: "asc" },
    { key: "lastSignIn", firstDir: "desc" },
  ],
  defaultSort: "name",
  filters: {},
  paged: false,
  searchable: false,
};

// What each sort compares (sortRows.ts breaks ties by user id).
const sortValues: Record<string, (member: Member) => SortValue> = {
  name: (member) => member.displayName,
  email: (member) => member.email,
  role: (member) => member.roleName,
  status: (member) => member.status,
  lastSignIn: (member) => member.lastSignInAt,
};

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
  const { state: view, show } = useListState(memberListSpec);
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
  const columns = memberColumns(controls);
  const { choices, toggle } = useColumnChoices("members", columns);

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
  const sort = { key: view.sort, dir: view.dir };
  const rows = members.data
    ? sortRows(
        members.data.members,
        sortValues[view.sort] ?? sortValues.name,
        (member) => member.userId,
        view.dir,
      )
    : [];
  const visible = visibleKeys(columns, choices);

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

      <div className="grid-toolbar">
        <div className="grid-controls">
          {wide ? (
            <ColumnPicker
              columns={columns}
              visible={visible}
              onToggle={toggle}
            />
          ) : (
            <SortSelect
              columns={columns}
              sort={sort}
              firstDirOf={(key) => firstDirOf(memberListSpec, key)}
              onSort={(key, dir) => show(withSort(view, key, dir))}
            />
          )}
          {!isDefaultView(view, memberListSpec) && (
            <button
              type="button"
              className="grid-clear"
              onClick={() => show(defaultListState(memberListSpec))}
            >
              Clear
            </button>
          )}
        </div>
      </div>

      {members.isPending ? (
        <p role="status">Loading...</p>
      ) : wide ? (
        <DataTable
          columns={columns.filter((column) => visible.includes(column.key))}
          rows={rows}
          rowKey={(member) => member.userId}
          sort={sort}
          onSort={(key) => show(sortBy(view, key, memberListSpec))}
        />
      ) : (
        <MemberCards members={rows} controls={controls} />
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

// The columns, with the Access column (the disable button) only for someone who can manage
// members. Member names the row, so it can't be hidden; Access holds controls, not data, so it
// stays too.
function memberColumns(controls: ControlsContext): GridColumn<Member>[] {
  const columns: GridColumn<Member>[] = [
    {
      key: "name",
      label: "Member",
      hideable: false,
      sortKey: "name",
      rowHeader: true,
      cell: (member) => <MemberName member={member} controls={controls} />,
    },
    {
      key: "email",
      label: "Email",
      hideable: true,
      sortKey: "email",
      className: "member-email",
      cell: (member) => <Email address={member.email} />,
    },
    {
      key: "role",
      label: "Role",
      hideable: true,
      sortKey: "role",
      className: "nowrap",
      cell: (member) => <RoleControl member={member} controls={controls} />,
    },
    {
      key: "status",
      label: "Status",
      hideable: true,
      sortKey: "status",
      sortWords: { asc: "Active first", desc: "Disabled first" },
      cell: (member) => <StatusBadge member={member} />,
    },
    {
      key: "lastSignIn",
      label: "Last sign-in",
      hideable: true,
      sortKey: "lastSignIn",
      sortWords: { asc: "never, then oldest", desc: "newest first" },
      className: "nowrap",
      cell: (member) =>
        member.lastSignInAt ? formatUtcDate(member.lastSignInAt) : "Never",
    },
  ];

  if (controls.manage) {
    columns.push({
      key: "access",
      label: "Access",
      hideable: false,
      labelHidden: true,
      cell: (member) => <StatusButton member={member} controls={controls} />,
    });
  }

  return columns;
}

function MemberCards({
  members,
  controls,
}: {
  members: Member[];
  controls: ControlsContext;
}) {
  return (
    <ul className="card-list">
      {members.map((member) => (
        <li key={member.userId} className="member-card">
          <div className="member-card-head">
            <p className="member-card-name">
              <MemberName member={member} controls={controls} />
            </p>
            <StatusBadge member={member} />
          </div>
          <p className="member-email">
            <Email address={member.email} />
          </p>
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

// A picker for someone who can manage members, plain text for everyone else, and for your own
// row: you can't change your own role, and StatusButton says so where it can be read.
function RoleControl({
  member,
  controls,
}: {
  member: Member;
  controls: ControlsContext;
}) {
  if (!controls.manage || isSelf(member, controls)) {
    return <>{member.roleName}</>;
  }

  return (
    <select
      className="role-select"
      aria-label={`Role for ${member.displayName}`}
      value={member.roleId}
      disabled={!controls.enabled}
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

// Your own row says why there's nothing to press, in text anyone can read. (This was a
// tooltip, which only a mouse can show.)
function StatusButton({
  member,
  controls,
}: {
  member: Member;
  controls: ControlsContext;
}) {
  if (isSelf(member, controls)) {
    return (
      <span className="member-self-note">
        You can&apos;t change your own role or access.
      </span>
    );
  }

  const active = member.status === "Active";
  return (
    <button
      type="button"
      className="member-status-button"
      disabled={!controls.enabled}
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
  return value ? formatUtc(value) : "Never";
}
