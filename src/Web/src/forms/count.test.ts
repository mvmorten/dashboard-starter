import assert from "node:assert/strict";
import { describe, test } from "node:test";
import { countMessage } from "./count.ts";

describe("countMessage", () => {
  test("says nothing while there's plenty of room", () => {
    assert.equal(countMessage(0, 2000), null);
    assert.equal(countMessage(1599, 2000), null);
  });

  test("appears at 80% of the limit", () => {
    assert.equal(countMessage(1600, 2000), "400 characters left");
  });

  test("counts down to the limit", () => {
    assert.equal(countMessage(1999, 2000), "1 character left");
    assert.equal(countMessage(2000, 2000), "0 characters left");
  });

  test("says how many to take out when over", () => {
    assert.equal(countMessage(2001, 2000), "1 character too many");
    assert.equal(countMessage(2012, 2000), "12 characters too many");
  });
});
