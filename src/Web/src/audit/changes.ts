// What an audit entry says, as words for the screen. Pure, with no imports, so `npm test`
// checks it without a browser (changes.test.ts).
//
// An entry's changes are the API's: {"name": {"old": "Acme", "new": "Acme Ltd"}}, keyed by the
// API's field names, only the fields that changed (src/Db/Audit/AuditChanges.cs).

export interface FieldChange {
  old: string | null;
  new: string | null;
}

export type Changes = Record<string, FieldChange>;

// The labels the forms use for the same fields. A field without one (a fork added it to the
// log but not here) shows under the API's own name rather than not at all.
const fieldLabels: Record<string, string> = {
  name: "Name",
  email: "Email",
  phone: "Phone",
  status: "Status",
  notes: "Notes",
  role: "Role",
};

export function fieldLabel(field: string): string {
  return fieldLabels[field] ?? field;
}

export interface ChangeRow {
  field: string;
  label: string;
  old: string | null;
  new: string | null;
}

// The changed fields, in the order the entry lists them (the form's order).
export function changeRows(changes: Changes | null): ChangeRow[] {
  return Object.entries(changes ?? {}).map(([field, change]) => ({
    field,
    label: fieldLabel(field),
    old: change.old,
    new: change.new,
  }));
}

// "Sam Rivera [added this customer]", for one entity's history.
export function actionWords(entityType: string, action: string): string {
  const thing = entityType === "Member" ? "member" : "customer";
  switch (action) {
    case "Created":
      return `added this ${thing}`;
    case "Updated":
      return `changed this ${thing}`;
    case "Deleted":
      return `deleted this ${thing}`;
    case "Restored":
      return `restored this ${thing}`;
    default:
      return action;
  }
}

// One line for the audit page's list. Short values (a role, a status) are shown; a customer's
// changed fields are only named, because a note can be 2000 characters and the customer's own
// history shows them in full.
export function summary(
  entityType: string,
  action: string,
  changes: Changes | null,
): string {
  if (action !== "Updated") {
    return action === "Created" ? "Added" : action;
  }

  const rows = changeRows(changes);
  if (entityType === "Member") {
    return rows
      .map(
        (row) => `${row.label}: ${row.old ?? "none"} to ${row.new ?? "none"}`,
      )
      .join(", ");
  }

  return `Changed ${listOf(rows.map((row) => row.label.toLowerCase()))}`;
}

// How much of a long value shows before "Show all": about three lines on a phone.
export const clipLength = 160;

// The start of a long value, cut at a word where there's one nearby, never through the middle
// of a character that takes two UTF-16 units (an emoji).
export function clip(
  value: string,
  max: number = clipLength,
): { text: string; clipped: boolean } {
  if (value.length <= max) {
    return { text: value, clipped: false };
  }

  let end = max;
  const code = value.charCodeAt(end - 1);
  if (code >= 0xd800 && code <= 0xdbff) {
    end -= 1;
  }

  let text = value.slice(0, end);
  const space = text.lastIndexOf(" ");
  if (space > max * 0.75) {
    text = text.slice(0, space);
  }

  return { text: text.trimEnd(), clipped: true };
}

// Whether the history on screen is missing an earlier part: this is the last page, and its
// oldest entry isn't "Created". History starts when the audit log was added, so an older
// customer's first entry is a change, or it has none at all.
export function startsLater(
  oldestAction: string | null,
  page: number,
  pageSize: number,
  total: number,
): boolean {
  return page * pageSize >= total && oldestAction !== "Created";
}

function listOf(items: string[]): string {
  if (items.length <= 1) {
    return items.join("");
  }

  return `${items.slice(0, -1).join(", ")} and ${items[items.length - 1]}`;
}
