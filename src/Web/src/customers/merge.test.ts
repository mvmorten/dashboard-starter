// Tests for the three-way merge. Run with `npm test`: Node's own test runner, which reads
// TypeScript directly (it strips the types), so there's no test library and no build step.

import assert from "node:assert/strict";
import { describe, test } from "node:test";
import { mergeThreeWay } from "./merge.ts";

const original = {
  name: "Ada Okafor",
  email: "ada@example.com",
  phone: "+1 555 0100",
  notes: "",
};

describe("mergeThreeWay", () => {
  test("nobody changed anything: nothing taken, no conflicts", () => {
    const result = mergeThreeWay(original, { ...original }, { ...original });

    assert.deepEqual(result.merged, original);
    assert.deepEqual(result.takenFromTheirs, []);
    assert.deepEqual(result.conflicts, []);
  });

  test("a field only I changed keeps mine", () => {
    const mine = { ...original, phone: "+1 555 0199" };
    const result = mergeThreeWay(original, mine, { ...original });

    assert.equal(result.merged.phone, "+1 555 0199");
    assert.deepEqual(result.takenFromTheirs, []);
    assert.deepEqual(result.conflicts, []);
  });

  test("a field only they changed takes theirs, and says so", () => {
    const theirs = { ...original, email: "ada@new.example" };
    const result = mergeThreeWay(original, { ...original }, theirs);

    assert.equal(result.merged.email, "ada@new.example");
    assert.deepEqual(result.takenFromTheirs, ["email"]);
    assert.deepEqual(result.conflicts, []);
  });

  test("both changed a field to the same value: fine, not a conflict", () => {
    const mine = { ...original, name: "Ada Okafor-Smith" };
    const theirs = { ...original, name: "Ada Okafor-Smith" };
    const result = mergeThreeWay(original, mine, theirs);

    assert.equal(result.merged.name, "Ada Okafor-Smith");
    assert.deepEqual(result.takenFromTheirs, []);
    assert.deepEqual(result.conflicts, []);
  });

  test("both changed a field to different values: a conflict, holding mine", () => {
    const mine = { ...original, name: "Ada O." };
    const theirs = { ...original, name: "A. Okafor" };
    const result = mergeThreeWay(original, mine, theirs);

    assert.deepEqual(result.conflicts, ["name"]);
    assert.equal(result.merged.name, "Ada O.");
    assert.deepEqual(result.takenFromTheirs, []);
  });

  test("each field is decided on its own", () => {
    const mine = { ...original, name: "Ada O.", phone: "+1 555 0199" };
    const theirs = {
      ...original,
      name: "A. Okafor",
      email: "ada@new.example",
      notes: "Prefers email.",
    };
    const result = mergeThreeWay(original, mine, theirs);

    assert.deepEqual(result.merged, {
      name: "Ada O.",
      email: "ada@new.example",
      phone: "+1 555 0199",
      notes: "Prefers email.",
    });
    assert.deepEqual(result.takenFromTheirs, ["email", "notes"]);
    assert.deepEqual(result.conflicts, ["name"]);
  });

  test("emptying a field is a change like any other", () => {
    const mine = { ...original, phone: "" };
    const theirs = { ...original, phone: "+1 555 0142" };
    const result = mergeThreeWay(original, mine, theirs);

    assert.deepEqual(result.conflicts, ["phone"]);

    const onlyTheyEmptied = mergeThreeWay(
      original,
      { ...original },
      { ...original, email: "" },
    );
    assert.equal(onlyTheyEmptied.merged.email, "");
    assert.deepEqual(onlyTheyEmptied.takenFromTheirs, ["email"]);
  });

  test("values are compared exactly: case and spaces count", () => {
    const mine = { ...original, name: "ada okafor" };
    const theirs = { ...original, name: "ADA OKAFOR" };
    const result = mergeThreeWay(original, mine, theirs);

    assert.deepEqual(result.conflicts, ["name"]);
  });

  test("conflicts and taken fields come in the original's field order", () => {
    const mine = { ...original, notes: "mine", name: "mine" };
    const theirs = {
      ...original,
      notes: "theirs",
      name: "theirs",
      phone: "theirs",
      email: "theirs",
    };
    const result = mergeThreeWay(original, mine, theirs);

    assert.deepEqual(result.conflicts, ["name", "notes"]);
    assert.deepEqual(result.takenFromTheirs, ["email", "phone"]);
  });

  test("doesn't change its inputs", () => {
    const mine = { ...original, name: "Ada O." };
    const theirs = { ...original, email: "ada@new.example" };
    const before = JSON.stringify([original, mine, theirs]);

    mergeThreeWay(original, mine, theirs);

    assert.equal(JSON.stringify([original, mine, theirs]), before);
  });
});
