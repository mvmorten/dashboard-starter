import { useId, type ReactNode } from "react";
import type { ColumnChoice } from "./columns";
import type { SortDir } from "./listState";

// The grid: a table on a wide screen, with sortable headers and a choice of columns; on a phone,
// the page's own cards, with a sort select above them. The page owns the data and the view
// (useListState); these components only draw it and say what was clicked.
//
// A header click, end to end (Customers):
//   DataTable's header button -> onSort("updated") -> sortBy() in listState.ts
//   -> the URL becomes ?sort=updated&dir=desc -> useCustomers asks GET /api/customers?sort=updated&dir=desc
//   -> CustomerStore.SortColumns["updated"] -> ORDER BY c.UpdatedAt DESC, c.Id DESC

export interface GridColumn<Row> extends ColumnChoice {
  // Set if the column sorts: the API's (or sortRows') name for it.
  sortKey?: string;
  // What the sort select calls each direction. Text sorts say "A to Z" unless this says otherwise.
  sortWords?: Record<SortDir, string>;
  // The cell that names the row: a <th scope="row"> rather than a <td>.
  rowHeader?: boolean;
  // A column of buttons, say, whose header is for screen readers only.
  labelHidden?: boolean;
  className?: string;
  cell: (row: Row) => ReactNode;
}

export interface GridSort {
  key: string;
  dir: SortDir;
}

const textWords: Record<SortDir, string> = { asc: "A to Z", desc: "Z to A" };

export function DataTable<Row>({
  columns,
  rows,
  rowKey,
  sort,
  onSort,
}: {
  // Only the columns to show, in order.
  columns: readonly GridColumn<Row>[];
  rows: readonly Row[];
  rowKey: (row: Row) => string | number;
  sort: GridSort;
  onSort: (key: string) => void;
}) {
  return (
    // If the table is still wider than its space, it scrolls inside this box, not the page.
    <div className="table-scroll">
      <table className="data-table">
        <thead>
          <tr>
            {columns.map((column) => (
              <HeaderCell
                key={column.key}
                column={column}
                sort={sort}
                onSort={onSort}
              />
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={rowKey(row)}>
              {columns.map((column) =>
                column.rowHeader ? (
                  <th key={column.key} scope="row" className={column.className}>
                    {column.cell(row)}
                  </th>
                ) : (
                  <td key={column.key} className={column.className}>
                    {column.cell(row)}
                  </td>
                ),
              )}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

// A sortable header is a button inside the <th>: reachable with Tab, pressed with Enter or
// Space. aria-sort on the <th> tells a screen reader which column is sorted, and which way.
function HeaderCell<Row>({
  column,
  sort,
  onSort,
}: {
  column: GridColumn<Row>;
  sort: GridSort;
  onSort: (key: string) => void;
}) {
  if (column.labelHidden) {
    return (
      <th scope="col">
        <span className="visually-hidden">{column.label}</span>
      </th>
    );
  }

  const { sortKey } = column;
  if (!sortKey) {
    return <th scope="col">{column.label}</th>;
  }

  const sorted = sort.key === sortKey;
  return (
    <th
      scope="col"
      aria-sort={
        sorted ? (sort.dir === "asc" ? "ascending" : "descending") : undefined
      }
    >
      <button
        type="button"
        className="sort-button"
        onClick={() => onSort(sortKey)}
      >
        {column.label}
        {/* Seen, not read: aria-sort already says it. An unsorted column shows that it sorts. */}
        <span
          className={sorted ? "sort-mark" : "sort-mark sort-mark-off"}
          aria-hidden="true"
        >
          {sorted ? (sort.dir === "asc" ? "▲" : "▼") : "↕"}
        </span>
      </button>
    </th>
  );
}

// The phone's sort: one select, a column and a direction in each option ("Updated: newest
// first"), since cards have no headers to tap. Each column's first direction comes first.
export function SortSelect<Row>({
  columns,
  sort,
  firstDirOf,
  onSort,
}: {
  columns: readonly GridColumn<Row>[];
  sort: GridSort;
  firstDirOf: (key: string) => SortDir;
  onSort: (key: string, dir: SortDir) => void;
}) {
  const id = useId();
  const options = columns.flatMap((column) => {
    if (!column.sortKey) {
      return [];
    }

    const words = column.sortWords ?? textWords;
    const first = firstDirOf(column.sortKey);
    const dirs: SortDir[] = first === "asc" ? ["asc", "desc"] : ["desc", "asc"];
    return dirs.map((dir) => ({
      value: `${column.sortKey}:${dir}`,
      label: `${column.label}: ${words[dir]}`,
    }));
  });

  return (
    <div className="grid-select grid-select-sort">
      <label htmlFor={id}>Sort</label>
      <select
        id={id}
        value={`${sort.key}:${sort.dir}`}
        onChange={(event) => {
          const [key, dir] = event.target.value.split(":");
          onSort(key, dir as SortDir);
        }}
      >
        {options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
    </div>
  );
}

// A filter with a few fixed values, and "All" ("" in the URL) for none.
export function FilterSelect({
  label,
  value,
  options,
  onChange,
}: {
  label: string;
  value: string;
  options: readonly { value: string; label: string }[];
  onChange: (value: string) => void;
}) {
  const id = useId();
  return (
    <div className="grid-select">
      <label htmlFor={id}>{label}</label>
      <select
        id={id}
        value={value}
        onChange={(event) => onChange(event.target.value)}
      >
        <option value="">All</option>
        {options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
    </div>
  );
}

// Show or hide columns, on a wide screen. A <details> opens and closes with a tap, a click or
// the keyboard, with no script and nothing that needs hover. Columns that can't be hidden
// aren't listed.
export function ColumnPicker<Row>({
  columns,
  visible,
  onToggle,
}: {
  columns: readonly GridColumn<Row>[];
  visible: readonly string[];
  onToggle: (key: string) => void;
}) {
  return (
    <details className="column-picker">
      <summary>Columns</summary>
      <fieldset>
        <legend className="visually-hidden">Columns to show</legend>
        {columns
          .filter((column) => column.hideable)
          .map((column) => (
            <label key={column.key}>
              <input
                type="checkbox"
                checked={visible.includes(column.key)}
                onChange={() => onToggle(column.key)}
              />
              {column.label}
            </label>
          ))}
      </fieldset>
    </details>
  );
}
