import { useState } from "react";
import { useMe } from "../auth/useMe";
import {
  choicesStorageKey,
  parseChoices,
  toggleColumn,
  type ColumnChoice,
  type ColumnChoices,
} from "./columns";

// Which columns this person shows in this grid, kept in this browser's localStorage.
//
// Why localStorage and not the server: it's a display preference, and losing it costs one
// click. A server-side preference would need a table, an endpoint and sandbox cleanup for
// something this small. It also stays out of the URL on purpose: a link you share shows the
// same rows, not your columns.
//
// localStorage can be missing or refuse (a private window, blocked site data). Then the grid
// shows its default columns, and a change lasts until the page is left.
export function useColumnChoices(
  grid: string,
  columns: readonly ColumnChoice[],
) {
  const { data: me } = useMe();
  // Pages render only once /me has loaded (RequireSignIn), so there's always a user here.
  const storageKey = choicesStorageKey(grid, me?.user.id ?? 0);
  const [choices, setChoices] = useState<ColumnChoices>(() =>
    load(storageKey, columns),
  );

  function toggle(key: string) {
    const next = toggleColumn(columns, choices, key);
    setChoices(next);
    save(storageKey, next);
  }

  return { choices, toggle };
}

function load(
  storageKey: string,
  columns: readonly ColumnChoice[],
): ColumnChoices {
  try {
    return parseChoices(window.localStorage.getItem(storageKey), columns);
  } catch {
    return {};
  }
}

function save(storageKey: string, choices: ColumnChoices) {
  try {
    if (Object.keys(choices).length === 0) {
      window.localStorage.removeItem(storageKey);
    } else {
      window.localStorage.setItem(storageKey, JSON.stringify(choices));
    }
  } catch {
    // Not saved: the choice still applies until the page is left.
  }
}
