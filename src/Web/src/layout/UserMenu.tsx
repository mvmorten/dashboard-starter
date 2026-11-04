import { useQueryClient } from "@tanstack/react-query";
import { useId, useRef } from "react";
import { useNavigate } from "react-router";
import { signOut } from "../auth/session";
import { useMe } from "../auth/useMe";
import ThemeToggle from "./ThemeToggle";
import { SignOutIcon, UserIcon } from "./icons";
import { focusMenuOnOpen, moveMenuFocus } from "./menuKeyboard";

// The account button at the right of the top bar: who you are, the theme, and sign out.
// The theme button moved in here from the top bar, so on a phone the bar still holds the
// same number of buttons as before sign-in existed.
export default function UserMenu() {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const { data: me } = useMe();
  const menuRef = useRef<HTMLDivElement>(null);
  const menuId = useId();

  if (!me) {
    return null;
  }

  async function signOutAndLeave() {
    menuRef.current?.hidePopover();
    await signOut(queryClient).catch(() => {});
    navigate("/sign-in", { replace: true });
  }

  return (
    <div className="user">
      <button
        type="button"
        className="icon-button"
        popoverTarget={menuId}
        aria-label={`Account: ${me.user.displayName}`}
      >
        <UserIcon />
      </button>

      <div
        id={menuId}
        ref={menuRef}
        popover="auto"
        className="topbar-menu"
        aria-label="Account"
        onToggle={(event) => focusMenuOnOpen(menuRef, event)}
        onKeyDown={(event) => moveMenuFocus(menuRef, event)}
      >
        <div className="user-identity">
          <p className="user-name">{me.user.displayName}</p>
          <p className="user-email">{me.user.email}</p>
        </div>
        <ul>
          <li>
            <ThemeToggle withLabel />
          </li>
          <li>
            <button
              type="button"
              className="menu-option"
              onClick={signOutAndLeave}
            >
              <SignOutIcon />
              Sign out
            </button>
          </li>
        </ul>
      </div>
    </div>
  );
}
