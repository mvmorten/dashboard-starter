// Tests for column choices. Run with `npm test` (Node's own test runner).

import assert from "node:assert/strict";
import { describe, test } from "node:test";
import {
  choicesStorageKey,
  parseChoices,
  toggleColumn,
  visibleKeys,
  type ColumnChoice,
} from "./columns.ts";

const columns: ColumnChoice[] = [
  { key: "name", label: "Name", hideable: false },
  { key: "email", label: "Email", hideable: true },
  { key: "created", label: "Created", hideable: true, hiddenByDefault: true },
];

describe("visibleKeys", () => {
  test("no choices: the defaults", () => {
    assert.deepEqual(visibleKeys(columns, {}), ["name", "email"]);
  });

  test("choices override the defaults, except for a column that can't be hidden", () => {
    assert.deepEqual(
      visibleKeys(columns, { email: false, created: true, name: false }),
      ["name", "created"],
    );
  });
});

describe("toggleColumn", () => {
  test("hides a shown column, and shows it again", () => {
    const hidden = toggleColumn(columns, {}, "email");
    assert.deepEqual(hidden, { email: false });
    assert.deepEqual(toggleColumn(columns, hidden, "email"), {});
  });

  test("shows a column hidden by default", () => {
    assert.deepEqual(toggleColumn(columns, {}, "created"), { created: true });
  });

  test("leaves a column that can't be hidden, and an unknown one, alone", () => {
    const choices = { email: false };
    assert.equal(toggleColumn(columns, choices, "name"), choices);
    assert.equal(toggleColumn(columns, choices, "nope"), choices);
  });
});

describe("parseChoices", () => {
  test("reads what toggleColumn saved", () => {
    assert.deepEqual(parseChoices('{"email":false,"created":true}', columns), {
      email: false,
      created: true,
    });
  });

  test("anything else is no choices at all, or only the parts that make sense", () => {
    assert.deepEqual(parseChoices(null, columns), {});
    assert.deepEqual(parseChoices("", columns), {});
    assert.deepEqual(parseChoices("not json", columns), {});
    assert.deepEqual(parseChoices("[1,2]", columns), {});
    assert.deepEqual(parseChoices("null", columns), {});
    assert.deepEqual(
      parseChoices(
        '{"email":"no","name":false,"gone":true,"created":true}',
        columns,
      ),
      {
        created: true,
      },
    );
  });
});

describe("choicesStorageKey", () => {
  test("one key per grid and person", () => {
    assert.equal(choicesStorageKey("customers", 7), "grid-columns:customers:7");
    assert.notEqual(
      choicesStorageKey("customers", 7),
      choicesStorageKey("customers", 8),
    );
    assert.notEqual(
      choicesStorageKey("customers", 7),
      choicesStorageKey("members", 7),
    );
  });
});
