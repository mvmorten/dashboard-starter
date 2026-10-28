import { useId, useRef, useState, type KeyboardEvent } from "react";
import { readStoredTenantId, storeTenantId } from "../tenants/tenantChoice";
import { useTenants } from "../tenants/useTenants";
import { CheckIcon, ChevronDownIcon } from "./icons";

// Built on the browser's own Popover API: the trigger button opens a popover that closes on
// Escape or a click outside and gives focus back to the trigger, with no library. We add
// what it doesn't do: move focus into the list on open, and arrow keys between options.
export default function TenantSwitcher() {
  const { data: tenants, isPending, isError } = useTenants();
  const [storedId, setStoredId] = useState(readStoredTenantId);
  const menuRef = useRef<HTMLDivElement>(null);
  const menuId = useId();

  if (isPending) {
    return <TenantText>Loading tenants...</TenantText>;
  }

  if (isError) {
    return <TenantText>Tenants unavailable</TenantText>;
  }

  if (tenants.length === 0) {
    return <TenantText>No tenants yet</TenantText>;
  }

  // A saved id that no longer exists falls back to the first tenant.
  const current =
    tenants.find((tenant) => tenant.id === storedId) ?? tenants[0];

  // With one tenant there is nothing to choose, so no menu.
  if (tenants.length === 1) {
    return <TenantText>{current.name}</TenantText>;
  }

  function choose(id: number) {
    storeTenantId(id);
    setStoredId(id);
    // Closing the popover returns focus to the trigger.
    menuRef.current?.hidePopover();
  }

  function focusOptions(event: { newState?: string }) {
    if (event.newState !== "open") {
      return;
    }

    const options = menuRef.current?.querySelectorAll("button");
    const selected = menuRef.current?.querySelector<HTMLButtonElement>(
      '[aria-current="true"]',
    );
    (selected ?? options?.[0])?.focus();
  }

  function moveFocus(event: KeyboardEvent<HTMLDivElement>) {
    const options = Array.from(
      menuRef.current?.querySelectorAll("button") ?? [],
    );
    const index = options.indexOf(document.activeElement as HTMLButtonElement);

    let target: number | null = null;
    if (event.key === "ArrowDown") {
      target = (index + 1) % options.length;
    } else if (event.key === "ArrowUp") {
      target = (index - 1 + options.length) % options.length;
    } else if (event.key === "Home") {
      target = 0;
    } else if (event.key === "End") {
      target = options.length - 1;
    }

    if (target === null) {
      return;
    }

    event.preventDefault();
    options[target].focus();
  }

  return (
    <div className="tenant">
      <button type="button" className="tenant-trigger" popoverTarget={menuId}>
        <span className="tenant-name">
          <span className="visually-hidden">Tenant: </span>
          {current.name}
        </span>
        <ChevronDownIcon />
      </button>

      <div
        id={menuId}
        ref={menuRef}
        popover="auto"
        className="tenant-menu"
        aria-label="Choose a tenant"
        onToggle={focusOptions}
        onKeyDown={moveFocus}
      >
        <ul>
          {tenants.map((tenant) => {
            const isCurrent = tenant.id === current.id;
            return (
              <li key={tenant.id}>
                <button
                  type="button"
                  className="tenant-option"
                  aria-current={isCurrent ? "true" : undefined}
                  onClick={() => choose(tenant.id)}
                >
                  {isCurrent ? (
                    <CheckIcon />
                  ) : (
                    <span className="tenant-option-spacer" />
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
