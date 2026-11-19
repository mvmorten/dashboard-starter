// Where a long email address may wrap. Pure, with no imports (emailParts.test.ts); Email.tsx puts
// a <wbr> (a line break opportunity) between the parts.
//
// The browser alone either won't wrap an address at all (it's one "word") or, with
// overflow-wrap: anywhere, wraps it at any letter: "sam.rivera@exam" / "ple.com". Splitting it
// before each dot and after the @ lets it wrap only where a reader expects a break:
//
//   sam.rivera@example.com  ->  "sam" ".rivera@" "example" ".com"
//
// A table cell is then never narrower than its longest part, so a column too narrow for that
// makes the table scroll inside its box (lists.css) rather than break a word.
export function emailParts(email: string): string[] {
  const parts: string[] = [];
  let current = "";
  for (const character of email) {
    if (character === "." && current !== "") {
      parts.push(current);
      current = "";
    }

    current += character;
    if (character === "@") {
      parts.push(current);
      current = "";
    }
  }

  if (current !== "") {
    parts.push(current);
  }

  return parts;
}
