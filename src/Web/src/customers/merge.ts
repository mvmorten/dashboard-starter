// A three-way merge of one record's fields, for when two people edit the same customer at once.
// The API answers a stale save with 409 and the record as it is now (CustomersController), so
// the form has three versions of every field:
//
//   original  what the form loaded, before either person changed anything
//   mine      what is in the form now
//   theirs    what the other person saved
//
// Each field on its own:
//
//   mine and theirs are the same    nothing to decide (neither changed it, or both made the
//                                   same change)
//   only I changed it               keep mine
//   only they changed it            take theirs
//   we both changed it, differently a conflict: the person chooses
//
// Pure on purpose: no imports, no React, no I/O. That keeps it easy to read, and easy to test
// with node:test (merge.test.ts), without a browser or a test library.
//
// Values are compared exactly as given. The form trims text first, as the API does, so
// "Ada " and "Ada" count as the same.

export interface MergeResult<T extends Record<string, string>> {
  // Every field, merged. A conflicting field holds mine until the person chooses.
  merged: T;
  // Fields only they changed, now holding their value: say so, so nothing changes unseen.
  takenFromTheirs: (keyof T & string)[];
  // Fields we both changed to different values, in the order of `original`'s keys.
  conflicts: (keyof T & string)[];
}

export function mergeThreeWay<T extends Record<string, string>>(
  original: T,
  mine: T,
  theirs: T,
): MergeResult<T> {
  const merged = { ...mine };
  const takenFromTheirs: (keyof T & string)[] = [];
  const conflicts: (keyof T & string)[] = [];

  for (const field of Object.keys(original) as (keyof T & string)[]) {
    if (mine[field] === theirs[field]) {
      continue;
    }

    if (mine[field] === original[field]) {
      merged[field] = theirs[field];
      takenFromTheirs.push(field);
      continue;
    }

    if (theirs[field] === original[field]) {
      continue;
    }

    conflicts.push(field);
  }

  return { merged, takenFromTheirs, conflicts };
}
