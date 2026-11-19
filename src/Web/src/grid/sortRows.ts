// Sorting rows that are all in the browser already (Members: the API sends the whole list).
// Pure, with no imports (sortRows.test.ts). Customers sorts on the server instead, because it
// only ever has one page.
//
// The same rules as the API's ORDER BY, so the two grids behave alike: one column, then the
// row's id in the same direction to break ties, and an empty value (null) first when
// ascending, as SQL Server does.

export type SortValue = string | number | null;

export function sortRows<Row>(
  rows: readonly Row[],
  valueOf: (row: Row) => SortValue,
  idOf: (row: Row) => number,
  dir: "asc" | "desc",
): Row[] {
  const sign = dir === "asc" ? 1 : -1;
  return [...rows].sort((a, b) => {
    const byValue = compareValues(valueOf(a), valueOf(b));
    return sign * (byValue !== 0 ? byValue : idOf(a) - idOf(b));
  });
}

// Text compares the way people read it: case and accents don't push "élan" after "zebra", and
// "Item 10" comes after "Item 9". ISO dates (the API's) compare correctly as text.
const collator = new Intl.Collator(undefined, {
  sensitivity: "base",
  numeric: true,
});

function compareValues(a: SortValue, b: SortValue): number {
  if (a === null || b === null) {
    return a === b ? 0 : a === null ? -1 : 1;
  }

  if (typeof a === "number" && typeof b === "number") {
    return a - b;
  }

  return collator.compare(String(a), String(b));
}
