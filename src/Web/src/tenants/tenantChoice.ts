// Which tenant the user picked in the switcher, remembered in this browser only.
//
// STEP 5: this is a client-side stand-in. The README's design is that the auth cookie holds
// the current tenant and switching checks the user's membership on the server. Until then,
// this choice changes what the top bar shows and nothing else: no request is scoped by it,
// and nothing here enforces tenant isolation.

const storageKey = "tenantId";

export function readStoredTenantId(): number | null {
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

export function storeTenantId(id: number): void {
  try {
    window.localStorage.setItem(storageKey, String(id));
  } catch {
    // Not remembering the choice is fine.
  }
}
