import { useQueryClient } from "@tanstack/react-query";
import { useId, useRef, useState } from "react";
import { currentTenant } from "../auth/me";
import { switchTenant } from "../auth/session";
import { useMe } from "../auth/useMe";
import { CheckIcon, ChevronDownIcon } from "./icons";
import { focusMenuOnOpen, moveMenuFocus } from "./menuKeyboard";

// Built on the browser's own Popover API (see menuKeyboard.ts). The current tenant lives
// in the auth cookie: choosing another one asks the API to switch, which checks the
// membership and issues a new cookie, then everything on screen is fetched again.
export default function TenantSwitcher() {
  const queryClient = useQueryClient();
  const { data: me } = useMe();
  const [switching, setSwitching] = useState(false);
  const [failed, setFailed] = useState(false);
  const menuRef = useRef<HTMLDivElement>(null);
  const menuId = useId();

  if (!me) {
    return null;
  }

  const current = currentTenant(me);
  if (!current) {
    return <TenantText>No tenant</TenantText>;
  }

  // With one tenant there is nothing to choose, so no menu.
  if (me.tenants.length === 1) {
    return <TenantText>{current.name}</TenantText>;
  }

  async function choose(id: number) {
    // Closing the popover returns focus to the trigger.
    menuRef.current?.hidePopover();
    if (id === current?.id) {
      return;
    }

    setSwitching(true);
    setFailed(false);
    try {
      await switchTenant(queryClient, id);
    } catch {
      setFailed(true);
    } finally {
      setSwitching(false);
    }
  }

  return (
    <div className="tenant">
      <button
        type="button"
        className="tenant-trigger"
        popoverTarget={menuId}
        disabled={switching}
      >
        <span className="tenant-name">
          <span className="visually-hidden">Tenant: </span>
          {current.name}
        </span>
        <ChevronDownIcon />
      </button>
      <span role="status" className="visually-hidden">
        {switching ? "Switching tenant" : ""}
        {failed ? "Couldn't switch tenant. Try again." : ""}
      </span>

      <div
        id={menuId}
        ref={menuRef}
        popover="auto"
        className="topbar-menu"
        aria-label="Choose a tenant"
        onToggle={(event) => focusMenuOnOpen(menuRef, event)}
        onKeyDown={(event) => moveMenuFocus(menuRef, event)}
      >
        <ul>
          {me.tenants.map((tenant) => {
            const isCurrent = tenant.id === current.id;
            return (
              <li key={tenant.id}>
                <button
                  type="button"
                  className="menu-option"
                  aria-current={isCurrent ? "true" : undefined}
                  onClick={() => choose(tenant.id)}
                >
                  {isCurrent ? (
                    <CheckIcon />
                  ) : (
                    <span className="menu-option-spacer" />
                  )}
                  {tenant.name}
                </button>
              </li>
            );
          })}
        </ul>
      </div>
    </div>
  );
}

function TenantText({ children }: { children: string }) {
  return <p className="tenant tenant-text tenant-name">{children}</p>;
}
