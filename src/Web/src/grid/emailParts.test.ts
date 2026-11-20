// Tests for where an email address may wrap. Run with `npm test` (Node's own test runner).

import assert from "node:assert/strict";
import { describe, test } from "node:test";
import { emailParts } from "./emailParts.ts";

describe("emailParts", () => {
  test("splits before each dot and after the @", () => {
    assert.deepEqual(emailParts("sam.rivera@example.com"), [
      "sam",
      ".rivera@",
      "example",
      ".com",
    ]);
    assert.deepEqual(emailParts("a@b.co.uk"), ["a@", "b", ".co", ".uk"]);
  });

  test("the parts put back together are the address, letter for letter", () => {
    for (const email of [
      "x@y",
      "first.middle.last+tag@mail.sub.example.org",
      "..odd@@case.",
      "",
    ]) {
      assert.equal(emailParts(email).join(""), email);
    }
  });

  test("never an empty part", () => {
    assert.deepEqual(emailParts(".a@.b."), [".a@", ".b", "."]);
    assert.deepEqual(emailParts(""), []);
  });

  test("an address without a dot or an @ stays whole", () => {
    assert.deepEqual(emailParts("localpart"), ["localpart"]);
  });
});
