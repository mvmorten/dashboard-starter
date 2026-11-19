// Which columns a wide table shows. Pure, with no imports (columns.test.ts); useColumnChoices.ts
// keeps the choices in localStorage.
//
// What's saved is only what the person changed: { "phone": false, "created": true }. A column
// they never touched follows its default, so a column added in a later version shows up (or
// stays hidden) as its default says, instead of being missing from an old saved list.

export interface ColumnChoice {
  key: string;
  label: string;
  // False for the column that names the row (Customers' Name, Members' Member): without it, a
  // row has no link or label.
  hideable: boolean;
  // Hidden until someone turns it on.
  hiddenByDefault?: boolean;
}

export type ColumnChoices = Record<string, boolean>;

export function isVisible(
  column: ColumnChoice,
  choices: ColumnChoices,
): boolean {
  if (!column.hideable) {
    return true;
  }

  return choices[column.key] ?? !column.hiddenByDefault;
}

export function visibleKeys(
  columns: readonly ColumnChoice[],
  choices: ColumnChoices,
): string[] {
  return columns
    .filter((column) => isVisible(column, choices))
    .map((column) => column.key);
}

// Show or hide one column. A column that can't be hidden doesn't change. A choice that matches
// the default is dropped, so the saved value stays small and a later default still applies.
export function toggleColumn(
  columns: readonly ColumnChoice[],
  choices: ColumnChoices,
  key: string,
): ColumnChoices {
  const column = columns.find((candidate) => candidate.key === key);
  if (!column || !column.hideable) {
    return choices;
  }

  const next = { ...choices };
  const visible = !isVisible(column, choices);
  if (visible === !column.hiddenByDefault) {
    delete next[key];
  } else {
    next[key] = visible;
  }

  return next;
}

// What localStorage held, made safe: anything that isn't an object of true/false for columns
// that exist and can be hidden is ignored. Saved by another version, edited by hand, or empty:
// the worst case is the default columns.
export function parseChoices(
  stored: string | null,
  columns: readonly ColumnChoice[],
): ColumnChoices {
  if (!stored) {
    return {};
  }

  let value: unknown;
  try {
    value = JSON.parse(stored);
  } catch {
    return {};
  }

  if (!value || typeof value !== "object" || Array.isArray(value)) {
    return {};
  }

  const choices: ColumnChoices = {};
  for (const column of columns) {
    const choice = (value as Record<string, unknown>)[column.key];
    if (column.hideable && typeof choice === "boolean") {
      choices[column.key] = choice;
    }
  }

  return choices;
}

// One key per person and grid: two people sharing a browser each keep their own columns.
export function choicesStorageKey(grid: string, userId: number): string {
  return `grid-columns:${grid}:${userId}`;
}
