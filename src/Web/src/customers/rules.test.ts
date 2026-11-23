// The customer field rules (rules.ts) against the shared cases in rule-cases.json. CI runs the
// same cases against the API (.github/scripts/rule-cases.mjs), so passing both means the form
// and CustomerValidation.cs agree, message for message.

import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { describe, test } from "node:test";
import {
  trimLikeServer,
  validateCustomer,
  type CustomerInput,
} from "./rules.ts";

type Part = string | { repeat: string; count: number };
type Value = string | null | Part[];

interface RuleCase {
  case: string;
  input: Partial<Record<keyof CustomerInput, Value>>;
  errors?: Partial<Record<keyof CustomerInput, string>>;
  stored?: Partial<Record<keyof CustomerInput, Value>>;
}

const file = JSON.parse(
  readFileSync(new URL("./rule-cases.json", import.meta.url), "utf8"),
) as { defaults: CustomerInput; cases: RuleCase[] };

// A value as the file writes it: a string, or parts joined together.
function expand(value: Value): string | null {
  if (!Array.isArray(value)) {
    return value;
  }

  return value
    .map((part) =>
      typeof part === "string" ? part : part.repeat.repeat(part.count),
    )
    .join("");
}

function inputOf(ruleCase: RuleCase): CustomerInput {
  const input = { ...file.defaults };
  for (const [field, value] of Object.entries(ruleCase.input)) {
    input[field as keyof CustomerInput] = expand(value) ?? "";
  }

  return input;
}

// The defaults as the API stores them ("" becomes null), with the case's own values on top.
function storedOf(ruleCase: RuleCase) {
  const stored: Record<string, string | null> = {};
  for (const [field, value] of Object.entries(file.defaults)) {
    stored[field] = value === "" ? null : value;
  }

  for (const [field, value] of Object.entries(ruleCase.stored ?? {})) {
    stored[field] = expand(value);
  }

  return stored;
}

describe("validateCustomer, against rule-cases.json", () => {
  test("the file has cases of both kinds", () => {
    assert.ok(file.cases.some((ruleCase) => ruleCase.errors));
    assert.ok(file.cases.some((ruleCase) => ruleCase.stored));
    for (const ruleCase of file.cases) {
      assert.ok(
        (ruleCase.errors === undefined) !== (ruleCase.stored === undefined),
        `"${ruleCase.case}" needs errors or stored, not both`,
      );
    }
  });

  for (const ruleCase of file.cases) {
    test(ruleCase.case, () => {
      const result = validateCustomer(inputOf(ruleCase));
      if (ruleCase.errors) {
        assert.deepEqual(result.errors, ruleCase.errors);
        return;
      }

      assert.deepEqual(result.errors, {});
      assert.deepEqual(result.cleaned, storedOf(ruleCase));
    });
  }
});

describe("trimLikeServer", () => {
  test("trims what .NET's Trim() trims, not what JavaScript's trim() trims", () => {
    assert.equal(trimLikeServer("\u0085 Ada \u3000"), "Ada");
    assert.equal(trimLikeServer("\ufeffAda"), "\ufeffAda");
  });

  test("leaves white space inside alone", () => {
    assert.equal(trimLikeServer("  Ada  Okafor\n"), "Ada  Okafor");
  });
});
