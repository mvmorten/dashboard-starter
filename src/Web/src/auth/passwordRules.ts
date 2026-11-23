// What a new password must be, as the reset and change password forms check it before they send
// anything. A copy of src/Db/Passwords/PasswordRules.cs, with the same messages; the API checks
// again and has the final word. No rules here that the server doesn't have: a length range and
// "not your email", nothing about digits or symbols.
//
// Kept in step by password-cases.json, run against this file by passwordRules.test.ts and
// against the API by .github/scripts/rule-cases.mjs.
//
// No imports, so Node's test runner can load it as it is.

export const passwordMinLength = 12;
export const passwordMaxLength = 128;

// PasswordRules.CheckLength. Returns what's wrong, or null.
export function checkPasswordLength(password: string): string | null {
  if (password.length < passwordMinLength) {
    return `The password must be at least ${passwordMinLength} characters.`;
  }

  if (password.length > passwordMaxLength) {
    return `The password can be at most ${passwordMaxLength} characters.`;
  }

  return null;
}

// PasswordRules.Check, for when the account's email is known (changing your password while
// signed in). A reset link's form doesn't know the email, so it can only check the length.
export function checkPassword(password: string, email: string): string | null {
  const lengthProblem = checkPasswordLength(password);
  if (lengthProblem !== null) {
    return lengthProblem;
  }

  if (sameIgnoringCase(trimLikeServer(password), trimLikeServer(email))) {
    return "The password can't be your email address.";
  }

  return null;
}

// string.Equals(a, b, StringComparison.OrdinalIgnoreCase): the same length, and each
// character the same once both are upper case. Upper-cased one character at a time and only
// where that gives one character, like .NET: JavaScript's "ß".toUpperCase() is "SS", but
// .NET leaves ß as it is.
function sameIgnoringCase(a: string, b: string): boolean {
  const left = Array.from(a);
  const right = Array.from(b);
  if (left.length !== right.length) {
    return false;
  }

  return left.every((char, i) => upper(char) === upper(right[i]));
}

function upper(char: string): string {
  const upperCased = char.toUpperCase();
  return Array.from(upperCased).length === 1 ? upperCased : char;
}

// The same as trimLikeServer in customers/rules.ts, written out twice to keep both files
// import-free: exactly what .NET's Trim() removes, which isn't quite what JavaScript's trim()
// removes.
// prettier-ignore
const serverWhiteSpace = new Set([
  0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x20, // tab, the line breaks, space
  0x85, 0xa0, 0x1680, // next line, no-break space, Ogham space mark
  0x2000, 0x2001, 0x2002, 0x2003, 0x2004, 0x2005, // en quad to ...
  0x2006, 0x2007, 0x2008, 0x2009, 0x200a, // ... hair space
  0x2028, 0x2029, // line and paragraph separators
  0x202f, 0x205f, 0x3000, // narrow no-break, math and ideographic spaces
]);

function trimLikeServer(value: string): string {
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
