// The new-password rules (passwordRules.ts) against the shared cases in password-cases.json.
// CI runs the same cases against the API (.github/scripts/rule-cases.mjs).

import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { describe, test } from "node:test";
import { checkPassword, checkPasswordLength } from "./passwordRules.ts";

type Part = string | { repeat: string; count: number };

interface PasswordCase {
  case: string;
  password: string | Part[];
  checkEmail?: boolean;
  error: string | null;
}

const file = JSON.parse(
  readFileSync(new URL("./password-cases.json", import.meta.url), "utf8"),
) as { email: string; cases: PasswordCase[] };

function expand(value: string | Part[]): string {
  if (!Array.isArray(value)) {
    return value;
  }

  return value
    .map((part) =>
      typeof part === "string" ? part : part.repeat.repeat(part.count),
    )
    .join("");
}

describe("the password rules, against password-cases.json", () => {
  for (const passwordCase of file.cases) {
    test(passwordCase.case, () => {
      const password = expand(passwordCase.password);
      const problem = passwordCase.checkEmail
        ? checkPassword(password, file.email)
        : checkPasswordLength(password);
      assert.equal(problem, passwordCase.error);
    });
  }
});

// Not in the shared cases: the API run would need an account whose email has an ß in it.
describe("comparing with the email ignores case like .NET's OrdinalIgnoreCase", () => {
  test("ß isn't the same as SS, though JavaScript upper-cases it to SS", () => {
    assert.equal(
      checkPassword("STRASSE@EXAMPLE.COM", "straße@example.com"),
      null,
    );
  });

  test("ß is the same as itself in any case around it", () => {
    assert.equal(
      checkPassword("STRAßE@EXAMPLE.COM", "straße@example.com"),
      "The password can't be your email address.",
    );
  });
});
