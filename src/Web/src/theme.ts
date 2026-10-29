// Theme choice: "system" (follow the operating system), "light", or "dark".
// Saved in localStorage under one key and applied as a data-theme attribute on <html>;
// "system" means no attribute. The CSS that reacts to it is in styles/tokens.css.
//
// The same read-and-apply logic also runs as a tiny inline script in index.html, before the
// first paint, so a dark-theme user never sees a white flash. Keep the two in step.

export type Theme = "system" | "light" | "dark";

const storageKey = "theme";

export function readTheme(): Theme {
  try {
    const stored = window.localStorage.getItem(storageKey);
    if (stored === "light" || stored === "dark") {
      return stored;
    }
  } catch {
    // Storage can be blocked. Fall through to following the system.
  }

  return "system";
}

export function applyTheme(theme: Theme): void {
  if (theme === "system") {
    delete document.documentElement.dataset.theme;
  } else {
    document.documentElement.dataset.theme = theme;
  }

  try {
    if (theme === "system") {
      window.localStorage.removeItem(storageKey);
    } else {
      window.localStorage.setItem(storageKey, theme);
    }
  } catch {
    // Not remembering the choice is fine.
  }
}

export function nextTheme(theme: Theme): Theme {
  if (theme === "system") {
    return "light";
  }

  return theme === "light" ? "dark" : "system";
}
