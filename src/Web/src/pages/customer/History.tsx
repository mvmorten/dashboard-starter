import { useState } from "react";
import type { AuditEntry } from "../../audit/audit";
import {
  actionWords,
  changeRows,
  clip,
  startsLater,
} from "../../audit/changes";
import { useCustomerHistory } from "../../audit/useAudit";
import { formatUtc, utcIso } from "../../format";
import { Pager } from "../../grid/Pager";

// A customer's audit entries, newest first: who, when, and what changed. A stacked list at
// every width (an entry can hold a 2000-character note, which no table column fits).
// The entries come from GET /api/customers/{id}/history; saving the form above refreshes them.
export default function History({ customerId }: { customerId: number }) {
  // The page is this section's own, not the URL's: the customer page's address is the
  // customer, and Back shouldn't step through its history's pages.
  const [page, setPage] = useState(1);
  const history = useCustomerHistory(customerId, page);

  return (
    <section className="history" aria-labelledby="history-heading">
      <h2 id="history-heading">History</h2>
      {history.isPending ? (
        <p role="status">Loading...</p>
      ) : history.isError ? (
        <div role="alert">
          <p>The history didn&apos;t load.</p>
          <button type="button" onClick={() => void history.refetch()}>
            Try again
          </button>
        </div>
      ) : (
        <>
          {history.data.items.length > 0 && (
            <ol className="history-list">
              {history.data.items.map((entry) => (
                <HistoryEntry key={entry.id} entry={entry} />
              ))}
            </ol>
          )}
          <Pager
            page={history.data.page}
            pageSize={history.data.pageSize}
            total={history.data.total}
            loading={history.isPlaceholderData}
            onPage={setPage}
          />
          {startsLater(
            history.data.items.at(-1)?.action ?? null,
            history.data.page,
            history.data.pageSize,
            history.data.total,
          ) && (
            <p className="history-note">
              {history.data.total === 0
                ? "No changes recorded yet. "
                : "Earlier changes weren't recorded. "}
              History starts when the audit log was added.
            </p>
          )}
        </>
      )}
    </section>
  );
}

function HistoryEntry({ entry }: { entry: AuditEntry }) {
  const rows = changeRows(entry.changes);
  return (
    <li>
      <p className="history-head">
        <span>
          <strong>{entry.userName ?? "The system"}</strong>{" "}
          {actionWords(entry.entityType, entry.action)}
        </span>
        {/* The full date and time, in the reader's own time zone, as text: nothing to hover. */}
        <time dateTime={utcIso(entry.occurredAt)}>
          {formatUtc(entry.occurredAt)}
        </time>
      </p>
      {rows.length > 0 && (
        <dl className="history-changes">
          {rows.map((row) => (
            <div key={row.field}>
              <dt>{row.label}</dt>
              {/* A new customer has no "before": just the values it started with. */}
              {entry.action !== "Created" && (
                <dd>
                  <span className="history-tag">Was</span>
                  <ChangedValue value={row.old} />
                </dd>
              )}
              <dd>
                {entry.action !== "Created" && (
                  <span className="history-tag">Now</span>
                )}
                <ChangedValue value={row.new} />
              </dd>
            </div>
          ))}
        </dl>
      )}
    </li>
  );
}

// One old or new value. A long one (notes can be 2000 characters) shows its start, with a
// button for the rest: a real button, so it works by tap and keyboard.
function ChangedValue({ value }: { value: string | null }) {
  const [open, setOpen] = useState(false);
  if (value === null || value === "") {
    return <span className="empty-value">(empty)</span>;
  }

  const { text, clipped } = clip(value);
  if (!clipped) {
    return <span className="field-value">{value}</span>;
  }

  return (
    <span className="history-long">
      <span className="field-value">{open ? value : `${text}...`}</span>
      <button
        type="button"
        className="link-button"
        aria-expanded={open}
        onClick={() => setOpen(!open)}
      >
        {open ? "Show less" : `Show all ${value.length} characters`}
      </button>
    </span>
  );
}
