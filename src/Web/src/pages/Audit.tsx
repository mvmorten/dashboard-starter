import { useMemo } from "react";
import { Link } from "react-router";
import { ApiError } from "../api";
import {
  auditListSpec,
  auditQuery,
  entityTypes,
  type AuditEntry,
} from "../audit/audit";
import { summary } from "../audit/changes";
import { useAudit } from "../audit/useAudit";
import { Permissions, can } from "../auth/permissions";
import { currentTenant } from "../auth/me";
import { useMe } from "../auth/useMe";
import { formatUtc, utcIso } from "../format";
import { DataTable, FilterSelect, type GridColumn } from "../grid/Grid";
import {
  defaultListState,
  isDefaultView,
  withFilter,
  withPage,
} from "../grid/listState";
import { Pager } from "../grid/Pager";
import { useListState } from "../grid/useListState";
import { useMediaQuery } from "../layout/useMediaQuery";
import { usePageTitle } from "../layout/usePageTitle";
import { useMembers } from "../members/useMembers";
import NoAccess from "./NoAccess";

// The table's columns, in order. Nothing sorts: the log is newest first, and that's the point
// of it. The summary names what changed; a customer's own page has the old and new values.
const columns: GridColumn<AuditEntry>[] = [
  {
    key: "when",
    label: "When",
    hideable: false,
    className: "nowrap",
    cell: (entry) => <When entry={entry} />,
  },
  {
    key: "who",
    label: "Who",
    hideable: false,
    cell: (entry) => who(entry),
  },
  {
    key: "what",
    label: "What",
    hideable: false,
    rowHeader: true,
    cell: (entry) => <EntityLink entry={entry} />,
  },
  {
    key: "change",
    label: "Change",
    hideable: false,
    cell: (entry) => summary(entry.entityType, entry.action, entry.changes),
  },
];

const typeOptions = [
  { value: "Customer", label: "Customers" },
  { value: "Member", label: "Members" },
] satisfies { value: (typeof entityTypes)[number]; label: string }[];

// The current tenant's audit log (audit.view): who changed what, newest first, 25 a page, with
// a filter by kind of thing and by member. A table on a wide screen and cards on a phone, and
// the view lives in the URL (?entityType=&userId=&page=), like the Customers list.
export default function Audit() {
  usePageTitle("Audit log");
  const { data: me } = useMe();
  const allowed = can(me, Permissions.auditView);
  // The member filter's values are the tenant's members, so the list waits for them: until
  // they're known, a ?userId= in the URL can't be told from a typo.
  const members = useMembers();
  const memberOptions = useMemo(
    () =>
      (members.data?.members ?? []).map((member) => ({
        value: String(member.userId),
        label: member.displayName,
      })),
    [members.data],
  );
  const spec = useMemo(
    () => auditListSpec(memberOptions.map((option) => option.value)),
    [memberOptions],
  );
  const { state: view, show } = useListState(spec);
  const audit = useAudit(auditQuery(view, spec), allowed && members.isSuccess);
  const wide = useMediaQuery("(min-width: 48rem)");

  if (!allowed) {
    return <NoAccess />;
  }

  const failed = audit.error ?? members.error;
  if (failed) {
    return failed instanceof ApiError && failed.status === 403 ? (
      <NoAccess />
    ) : (
      <div role="alert">
        <h1>Audit log</h1>
        <p className="lead">The server didn&apos;t answer.</p>
        <button
          type="button"
          onClick={() => {
            void members.refetch();
            void audit.refetch();
          }}
        >
          Try again
        </button>
      </div>
    );
  }

  const tenant = me ? currentTenant(me) : null;
  const filtered = !isDefaultView(view, spec);

  return (
    <>
      <h1>Audit log</h1>
      <p className="lead">
        Who changed what in {tenant?.name ?? "this tenant"}, newest first:
        customers, and members&apos; roles and access.
      </p>

      <div className="grid-toolbar">
        <div className="grid-controls">
          <FilterSelect
            label="Kind"
            value={view.filters.entityType ?? ""}
            options={typeOptions}
            onChange={(value) => show(withFilter(view, "entityType", value))}
          />
          <FilterSelect
            label="Changed by"
            value={view.filters.userId ?? ""}
            options={memberOptions}
            onChange={(value) => show(withFilter(view, "userId", value))}
          />
          {filtered && (
            <button
              type="button"
              className="grid-clear"
              onClick={() => show(defaultListState(spec))}
            >
              Clear
            </button>
          )}
        </div>
      </div>

      {audit.data === undefined ? (
        <p role="status">Loading...</p>
      ) : (
        <>
          <p className="result-summary">
            {resultSummary(
              audit.data.total,
              audit.data.page,
              audit.data.pageSize,
              audit.data.items.length,
              filtered,
            )}
          </p>
          {audit.data.items.length > 0 &&
            (wide ? (
              <DataTable
                columns={columns}
                rows={audit.data.items}
                rowKey={(entry) => entry.id}
                sort={{ key: "", dir: "desc" }}
                onSort={() => {}}
              />
            ) : (
              <AuditCards entries={audit.data.items} />
            ))}
          <Pager
            page={audit.data.page}
            pageSize={audit.data.pageSize}
            total={audit.data.total}
            loading={audit.isPlaceholderData}
            onPage={(next) => {
              show(withPage(view, next));
              // The pager is at the bottom; the new page starts at the top.
              window.scrollTo({ top: 0 });
            }}
          />
        </>
      )}
    </>
  );
}

function resultSummary(
  total: number,
  page: number,
  pageSize: number,
  shown: number,
  filtered: boolean,
): string {
  if (total === 0) {
    return filtered
      ? "No entries match."
      : "Nothing yet. The log starts with the first change made after it was added.";
  }

  if (shown === 0) {
    return `Page ${page} is past the end: there are ${total} entries.`;
  }

  const first = (page - 1) * pageSize + 1;
  const last = first + shown - 1;
  const entries = total === 1 ? "entry" : "entries";
  return first === 1 && last === total
    ? `${total} ${entries}`
    : `${first} to ${last} of ${total} ${entries}`;
}

function who(entry: AuditEntry): string {
  return entry.userName ?? "The system";
}

// The full date and time, in the reader's own time zone, as text: nothing to hover.
function When({ entry }: { entry: AuditEntry }) {
  return (
    <time dateTime={utcIso(entry.occurredAt)}>
      {formatUtc(entry.occurredAt)}
    </time>
  );
}

// The thing the entry is about, as a link to it. A customer links to its page either way: for
// a deleted one, that page shows it as deleted, with its history and Restore (to those who
// may see it). A member links to the Members page, where roles and access are.
function EntityLink({ entry }: { entry: AuditEntry }) {
  if (entry.entityType === "Member") {
    return (
      <span className="audit-entity">
        <Link to="/members">{entry.entityName ?? "A former member"}</Link>
        <span className="audit-kind">Member</span>
      </span>
    );
  }

  return (
    <span className="audit-entity">
      <Link to={`/customers/${entry.entityId}`}>
        {entry.entityName ?? `Customer ${entry.entityId}`}
      </Link>
      <span className="audit-kind">
        {entry.entityDeleted ? "Customer, deleted" : "Customer"}
      </span>
    </span>
  );
}

// On a phone: one card per entry, no table to scroll sideways.
function AuditCards({ entries }: { entries: AuditEntry[] }) {
  return (
    <ul className="card-list">
      {entries.map((entry) => (
        <li key={entry.id} className="audit-card">
          <p className="audit-card-what">
            <EntityLink entry={entry} />
          </p>
          <p>{summary(entry.entityType, entry.action, entry.changes)}</p>
          <p className="audit-card-meta">
            {who(entry)}, <When entry={entry} />
          </p>
        </li>
      ))}
    </ul>
  );
}
