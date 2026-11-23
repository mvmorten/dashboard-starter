// The shared rule cases, run against the API: the other half of rules.test.ts and
// passwordRules.test.ts, which run the same cases against the front end's copy of the rules.
// Passing both means the form and the server agree, message for message.
//
//   src/Web/src/customers/rule-cases.json  -> POST /api/customers (CustomerValidation.cs)
//   src/Web/src/auth/password-cases.json   -> reset-password and change-password (PasswordRules.cs)
//
// Node, not bash and jq like the other check scripts: the cases are JSON, Node reads them as
// they are, and it runs on any machine that builds the front end. Plain fetch and node:assert,
// nothing to install.
//
// CI runs it (.github/workflows/ci.yml) against its own API instance. Locally, after migrate,
// with the API in Development and its password rate limit raised (the password cases make
// about 13 requests; the limit is 5 a minute):
//
//   PasswordReset__PermitLimit=100 dotnet run --project src/Api
//   BASE=http://localhost:5080 node .github/scripts/rule-cases.mjs
//
// Expects these users, with the password a-long-enough-password (PW to change it):
//   cmem@example.com   Member in Acme Corporation (the customer cases)
//   rules@example.com  any tenant (the password cases change its password, and put it back)

import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

const BASE = process.env.BASE ?? "http://localhost:5080";
const PW = process.env.PW ?? "a-long-enough-password";
const customerUser = process.env.CUSTOMER_USER ?? "cmem@example.com";

const read = (path) =>
  JSON.parse(readFileSync(new URL(path, import.meta.url), "utf8"));
const customerCases = read("../../src/Web/src/customers/rule-cases.json");
const passwordCases = read("../../src/Web/src/auth/password-cases.json");

// A value as the case files write it: a string, or parts joined together.
function expand(value) {
  if (!Array.isArray(value)) {
    return value;
  }

  return value
    .map((part) =>
      typeof part === "string" ? part : part.repeat.repeat(part.count),
    )
    .join("");
}

// A browser-like session: a cookie jar, and the antiforgery token sent back as a header on
// every change, as src/Web/src/api.ts does.
class Session {
  cookies = new Map();

  async send(method, path, body) {
    const headers = { "Content-Type": "application/json" };
    if (method !== "GET") {
      await this.send("GET", "/api/auth/antiforgery");
      const name = [...this.cookies.keys()].find((key) =>
        key.endsWith("ds-xsrf"),
      );
      headers["X-XSRF-TOKEN"] = this.cookies.get(name);
    }

    headers.Cookie = [...this.cookies]
      .map(([key, value]) => `${key}=${value}`)
      .join("; ");
    const response = await fetch(BASE + path, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    for (const cookie of response.headers.getSetCookie()) {
      const pair = cookie.split(";")[0];
      const at = pair.indexOf("=");
      const value = pair.slice(at + 1);
      if (value === "" || /expires=Thu, 01 Jan 1970/i.test(cookie)) {
        this.cookies.delete(pair.slice(0, at));
      } else {
        this.cookies.set(pair.slice(0, at), value);
      }
    }

    const text = await response.text();
    return { status: response.status, body: text ? JSON.parse(text) : null };
  }

  async signIn(email) {
    const result = await this.send("POST", "/api/auth/sign-in", {
      email,
      password: PW,
    });
    assert.equal(result.status, 200, `signing in as ${email}`);
  }
}

const failures = [];

async function check(name, run) {
  try {
    await run();
    console.log(`  ok    ${name}`);
  } catch (error) {
    failures.push(name);
    console.log(`  FAIL  ${name}\n${indent(error.message)}`);
  }
}

function indent(text) {
  return text
    .split("\n")
    .map((line) => `        ${line}`)
    .join("\n");
}

console.log("Customer rules (rule-cases.json) against POST /api/customers");
const customers = new Session();
await customers.signIn(customerUser);

for (const ruleCase of customerCases.cases) {
  await check(ruleCase.case, async () => {
    const input = { ...customerCases.defaults };
    for (const [field, value] of Object.entries(ruleCase.input)) {
      input[field] = expand(value);
    }

    const result = await customers.send("POST", "/api/customers", input);
    if (ruleCase.errors) {
      assert.equal(result.status, 400, JSON.stringify(result.body));
      const firstMessages = Object.fromEntries(
        Object.entries(result.body.errors).map(([field, messages]) => [
          field,
          messages[0],
        ]),
      );
      assert.deepEqual(firstMessages, ruleCase.errors);
      return;
    }

    assert.equal(result.status, 201, JSON.stringify(result.body));
    const expected = {};
    for (const [field, value] of Object.entries(customerCases.defaults)) {
      expected[field] = value === "" ? null : value;
    }

    for (const [field, value] of Object.entries(ruleCase.stored)) {
      expected[field] = expand(value);
    }

    const stored = Object.fromEntries(
      Object.keys(expected).map((field) => [field, result.body[field]]),
    );
    // Clean up first, so a mismatch doesn't leave the row behind.
    const deleted = await customers.send(
      "DELETE",
      `/api/customers/${result.body.id}`,
      { rowVersion: result.body.rowVersion },
    );
    assert.equal(deleted.status, 204, "deleting the case's customer");
    assert.deepEqual(stored, expected);
  });
}

console.log(
  "Password rules (password-cases.json) against the password endpoints",
);
const anonymous = new Session();
const account = new Session();
await account.signIn(passwordCases.email);

for (const passwordCase of passwordCases.cases) {
  await check(passwordCase.case, async () => {
    const password = expand(passwordCase.password);

    // The length alone: a reset link's form. The length is checked before the token, so a
    // made-up token still gets the length's answer, and an acceptable length gets "Reset
    // link not valid".
    if (!passwordCase.checkEmail) {
      const result = await anonymous.send("POST", "/api/auth/reset-password", {
        token: "not-a-real-reset-token",
        newPassword: password,
      });
      assert.equal(result.status, 400, JSON.stringify(result.body));
      if (passwordCase.error === null) {
        assert.equal(result.body.title, "Reset link not valid");
      } else {
        assert.equal(result.body.detail, passwordCase.error);
      }

      return;
    }

    // The account's own email too: change the password while signed in.
    const result = await account.send("POST", "/api/auth/change-password", {
      currentPassword: PW,
      newPassword: password,
    });
    if (passwordCase.error !== null) {
      assert.equal(result.status, 400, JSON.stringify(result.body));
      assert.equal(result.body.detail, passwordCase.error);
      return;
    }

    assert.equal(result.status, 204, JSON.stringify(result.body));
    const back = await account.send("POST", "/api/auth/change-password", {
      currentPassword: password,
      newPassword: PW,
    });
    assert.equal(back.status, 204, "putting the password back");
  });
}

if (failures.length > 0) {
  console.log(
    `\n${failures.length} case(s) where the API and the case files disagree. Either the rules ` +
      "changed on one side only (change both, in CustomerValidation.cs or PasswordRules.cs and in " +
      "rules.ts or passwordRules.ts), or a case is wrong.",
  );
  process.exit(1);
}

console.log("\nEvery case agrees with the API.");
