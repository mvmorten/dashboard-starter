// Tests for sorting rows in the browser. Run with `npm test` (Node's own test runner).

import assert from "node:assert/strict";
import { describe, test } from "node:test";
import { sortRows, type SortValue } from "./sortRows.ts";

interface Row {
  id: number;
  name: string;
  seen: string | null;
}

const rows: Row[] = [
  { id: 3, name: "bea", seen: "2026-03-01T10:00:00" },
  { id: 1, name: "Ana", seen: null },
  { id: 4, name: "Álvaro", seen: "2026-01-01T10:00:00" },
  { id: 2, name: "bea", seen: "2026-03-01T10:00:00" },
];

const ids = (sorted: Row[]) => sorted.map((row) => row.id);
const by = (pick: (row: Row) => SortValue) => (dir: "asc" | "desc") =>
  ids(sortRows(rows, pick, (row) => row.id, dir));

describe("sortRows", () => {
  test("text ignores case and accents; ties go by id, in the same direction", () => {
    const name = by((row) => row.name);
    assert.deepEqual(name("asc"), [4, 1, 2, 3]);
    assert.deepEqual(name("desc"), [3, 2, 1, 4]);
  });

  test("null first when ascending, last when descending, like SQL Server", () => {
    const seen = by((row) => row.seen);
    assert.deepEqual(seen("asc"), [1, 4, 2, 3]);
    assert.deepEqual(seen("desc"), [3, 2, 4, 1]);
  });

  test("numbers in text sort as numbers", () => {
    const items = [
      { id: 1, label: "Item 10" },
      { id: 2, label: "Item 9" },
    ];
    const sorted = sortRows(
      items,
      (item) => item.label,
      (item) => item.id,
      "asc",
    );
    assert.deepEqual(
      sorted.map((item) => item.id),
      [2, 1],
    );
  });

  test("leaves the rows it was given alone", () => {
    const before = ids(rows);
    sortRows(
      rows,
      (row) => row.name,
      (row) => row.id,
      "desc",
    );
    assert.deepEqual(ids(rows), before);
  });
});
