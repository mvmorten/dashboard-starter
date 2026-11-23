// The rules for a customer's fields, as the form checks them before it sends anything. A copy
// of src/Api/Customers/CustomerValidation.cs, line for line, with the same messages: the
// server checks every rule again and has the final word. This copy only means people find out
// sooner, without a round trip. It's never a security boundary.
//
// The two copies are kept in step by rule-cases.json: rules.test.ts runs every case against
// this file, and .github/scripts/rule-cases.mjs runs the same cases against the API in CI. Change
// a rule in both places and add a case, or CI fails.
//
// No imports, so Node's test runner can load it as it is.

// The column lengths in sql/migrations/0013_customers.sql.
export const nameMaxLength = 200;
export const emailMaxLength = 320;
export const phoneMaxLength = 50;
export const notesMaxLength = 2000;

export const customerStatuses = ["Active", "Inactive"] as const;

// What the form holds: always strings, "" for none. Shaped like CustomerFields in
// customers.ts (and the API's CustomerFields), written out here to keep this file import-free.
export interface CustomerInput {
  name: string;
  email: string;
  phone: string;
  status: string;
  notes: string;
}

export type CustomerRuleField = keyof CustomerInput;

// What the API would store, as ValidCustomer: trimmed, an empty optional field as null.
export interface CleanCustomer {
  name: string | null;
  email: string | null;
  phone: string | null;
  status: string | null;
  notes: string | null;
}

export interface CustomerCheck {
  // One message per field that has a problem, keyed like the API's ProblemDetails errors.
  errors: Partial<Record<CustomerRuleField, string>>;
  cleaned: CleanCustomer;
}

// CustomerValidation.Validate.
export function validateCustomer(fields: CustomerInput): CustomerCheck {
  const errors: Partial<Record<CustomerRuleField, string>> = {};

  const name = clean(fields.name);
  if (name === null) {
    errors.name = "Enter a name.";
  } else if (name.length > nameMaxLength) {
    errors.name = `Use ${nameMaxLength} characters or fewer.`;
  }

  const email = clean(fields.email);
  if (email !== null && email.length > emailMaxLength) {
    errors.email = `Use ${emailMaxLength} characters or fewer.`;
  } else if (email !== null && !looksLikeEmail(email)) {
    errors.email =
      "Enter an email address like name@example.com, or leave it empty.";
  }

  const phone = clean(fields.phone);
  if (phone !== null && phone.length > phoneMaxLength) {
    errors.phone = `Use ${phoneMaxLength} characters or fewer.`;
  }

  const notes = clean(fields.notes);
  if (notes !== null && notes.length > notesMaxLength) {
    errors.notes = `Use ${notesMaxLength} characters or fewer.`;
  }

  const status = clean(fields.status);
  if (status !== "Active" && status !== "Inactive") {
    errors.status = "Choose Active or Inactive.";
  }

  return { errors, cleaned: { name, email, phone, status, notes } };
}

// CustomerValidation.Clean: trimmed, and null if nothing is left.
export function clean(value: string): string | null {
  const trimmed = trimLikeServer(value);
  return trimmed === "" ? null : trimmed;
}

// .NET's string.Trim() and JavaScript's trim() don't remove quite the same characters:
// .NET removes U+0085 (next line) and JavaScript doesn't; JavaScript removes U+FEFF (byte
// order mark) and .NET doesn't. So this trims exactly what .NET calls white space
// (char.IsWhiteSpace), listed by code. Otherwise a name of just U+0085 would pass here and
// fail on the server. The same list is in auth/passwordRules.ts.
// prettier-ignore
const serverWhiteSpace = new Set([
  0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x20, // tab, the line breaks, space
  0x85, 0xa0, 0x1680, // next line, no-break space, Ogham space mark
  0x2000, 0x2001, 0x2002, 0x2003, 0x2004, 0x2005, // en quad to ...
  0x2006, 0x2007, 0x2008, 0x2009, 0x200a, // ... hair space
  0x2028, 0x2029, // line and paragraph separators
  0x202f, 0x205f, 0x3000, // narrow no-break, math and ideographic spaces
]);

export function trimLikeServer(value: string): string {
  let start = 0;
  let end = value.length;
  while (start < end && serverWhiteSpace.has(value.charCodeAt(start))) {
    start++;
  }

  while (end > start && serverWhiteSpace.has(value.charCodeAt(end - 1))) {
    end--;
  }

  return value.slice(start, end);
}

// CustomerValidation.LooksLikeEmail. Deliberately loose: one @ with something on each side,
// no spaces (only the space character itself, as on the server).
function looksLikeEmail(email: string): boolean {
  const at = email.indexOf("@");
  return (
    at > 0 &&
    at === email.lastIndexOf("@") &&
    at < email.length - 1 &&
    !email.includes(" ")
  );
}
