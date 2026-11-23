// What a character count says, if anything. A pure module with no imports, tested in
// count.test.ts.

// The count appears once the text reaches this share of the limit (1,600 of 2,000 for a
// customer's notes). Below it, a number that changes on every key is noise.
export const countShowsFrom = 0.8;

// "214 characters left", "1 character too many", or null while there's plenty of room.
export function countMessage(length: number, max: number): string | null {
  if (length < max * countShowsFrom) {
    return null;
  }

  const left = max - length;
  if (left >= 0) {
    return left === 1 ? "1 character left" : `${left} characters left`;
  }

  const over = -left;
  return over === 1 ? "1 character too many" : `${over} characters too many`;
}
