// Which tenant the user picked in the switcher, remembered in this browser only.
//
// STEP 5: this is a client-side stand-in. The README's design is that the auth cookie holds
// the current tenant and switching checks the user's membership on the server. Until then,
// this choice changes what the top bar shows and nothing else: no request is scoped by it,
// and nothing here enforces tenant isolation.

const storageKey = "tenantId";

// The switcher and any page that shows the current tenant read the same choice, so the
// store tells subscribers (useSyncExternalStore in useCurrentTenant) when it changes.
const listeners = new Set<() => void>();

export function subscribeToTenantChoice(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

function readStoredTenantId(): number | null {
  try {
    const stored = window.localStorage.getItem(storageKey);
    if (stored === null) {
      return null;
    }

    const id = Number(stored);
    return Number.isInteger(id) ? id : null;
  } catch {
    // Storage can be blocked (private windows, strict settings). The app still works.
    return null;
  }
}

let chosenId: number | null | undefined;

// Read once, then served from memory, so a blocked or unavailable localStorage still
// keeps the choice for the life of the page.
export function getTenantChoice(): number | null {
  if (chosenId === undefined) {
    chosenId = readStoredTenantId();
  }

  return chosenId;
}

export function storeTenantId(id: number): void {
  chosenId = id;
  try {
    window.localStorage.setItem(storageKey, String(id));
  } catch {
    // Not remembering the choice is fine.
  }

  listeners.forEach((listener) => listener());
}
