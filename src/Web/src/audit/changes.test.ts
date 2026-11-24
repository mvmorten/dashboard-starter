// Tests for the audit entries' words. Run with `npm test` (Node's own test runner).

import assert from "node:assert/strict";
import { describe, test } from "node:test";
import {
  actionWords,
  changeRows,
  clip,
  fieldLabel,
  startsLater,
  summary,
} from "./changes.ts";

describe("changeRows", () => {
  test("in the entry's order, with the form's labels", () => {
    assert.deepEqual(
      changeRows({
        status: { old: "Active", new: "Inactive" },
        notes: { old: null, new: "Closed" },
      }),
      [
        { field: "status", label: "Status", old: "Active", new: "Inactive" },
        { field: "notes", label: "Notes", old: null, new: "Closed" },
      ],
    );
  });

  test("no changes (Deleted, Restored): no rows", () => {
    assert.deepEqual(changeRows(null), []);
  });

  test("a field without a label shows under its own name", () => {
    assert.equal(fieldLabel("vatNumber"), "vatNumber");
  });
});

describe("actionWords", () => {
  test("names the kind of thing", () => {
    assert.equal(actionWords("Customer", "Created"), "added this customer");
    assert.equal(actionWords("Customer", "Restored"), "restored this customer");
    assert.equal(actionWords("Member", "Updated"), "changed this member");
  });
});

describe("summary", () => {
  test("a customer's update names the fields, not their values", () => {
    assert.equal(
      summary("Customer", "Updated", {
        name: { old: "A", new: "B" },
        email: { old: null, new: "a@b.example" },
        notes: { old: "x".repeat(2000), new: null },
      }),
      "Changed name, email and notes",
    );
  });

  test("a member's update shows the role or status", () => {
    assert.equal(
      summary("Member", "Updated", { role: { old: "Member", new: "Admin" } }),
      "Role: Member to Admin",
    );
  });

  test("the other actions are one word", () => {
    assert.equal(
      summary("Customer", "Created", { name: { old: null, new: "A" } }),
      "Added",
    );
    assert.equal(summary("Customer", "Deleted", null), "Deleted");
    assert.equal(summary("Customer", "Restored", null), "Restored");
  });
});

describe("clip", () => {
  test("a short value is whole", () => {
    assert.deepEqual(clip("Prefers phone calls."), {
      text: "Prefers phone calls.",
      clipped: false,
    });
  });

  test("exactly the limit is whole", () => {
    assert.equal(clip("x".repeat(160)).clipped, false);
  });

  test("a long value is cut at a word", () => {
    const { text, clipped } = clip("word ".repeat(100));
    assert.equal(clipped, true);
    assert.equal(text.length <= 160, true);
    assert.equal(text.endsWith("word"), true);
  });

  test("one long word is cut at the limit", () => {
    assert.equal(clip("x".repeat(2000)).text.length, 160);
  });

  test("never through the middle of an emoji", () => {
    const { text } = clip("x".repeat(9) + "😀" + "x".repeat(50), 10);
    assert.equal(text, "x".repeat(9));
  });
});

describe("startsLater", () => {
  test("the oldest entry is Created: the history is whole", () => {
    assert.equal(startsLater("Created", 1, 25, 3), false);
  });

  test("the oldest entry is a change: there was something before it", () => {
    assert.equal(startsLater("Updated", 1, 25, 3), true);
  });

  test("not on the last page: the oldest entry isn't on screen yet", () => {
    assert.equal(startsLater("Updated", 1, 25, 40), false);
    assert.equal(startsLater("Updated", 2, 25, 40), true);
  });

  test("no entries at all", () => {
    assert.equal(startsLater(null, 1, 25, 0), true);
  });
});
