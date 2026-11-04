import type { KeyboardEvent, RefObject } from "react";

// Keyboard behavior for the top bar's popover menus (tenant switcher, user menu). The
// browser's Popover API already closes on Escape or a click outside and gives focus back
// to the trigger. These add what it doesn't do: move focus into the menu when it opens,
// and arrow keys, Home, and End between its buttons.

// For the popover's onToggle: focus the current option, or the first button.
export function focusMenuOnOpen(
  menuRef: RefObject<HTMLElement | null>,
  event: { newState?: string },
): void {
  if (event.newState !== "open") {
    return;
  }

  const selected = menuRef.current?.querySelector<HTMLButtonElement>(
    '[aria-current="true"]',
  );
  const first = menuRef.current?.querySelector<HTMLButtonElement>("button");
  (selected ?? first)?.focus();
}

// For the popover's onKeyDown.
export function moveMenuFocus(
  menuRef: RefObject<HTMLElement | null>,
  event: KeyboardEvent<HTMLElement>,
): void {
  const buttons = Array.from(
    menuRef.current?.querySelectorAll<HTMLButtonElement>("button") ?? [],
  );
  const index = buttons.indexOf(document.activeElement as HTMLButtonElement);

  let target: number | null = null;
  if (event.key === "ArrowDown") {
    target = (index + 1) % buttons.length;
  } else if (event.key === "ArrowUp") {
    target = (index - 1 + buttons.length) % buttons.length;
  } else if (event.key === "Home") {
    target = 0;
  } else if (event.key === "End") {
    target = buttons.length - 1;
  }

  if (target === null) {
    return;
  }

  event.preventDefault();
  buttons[target].focus();
}
