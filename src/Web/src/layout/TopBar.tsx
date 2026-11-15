import { Link } from "react-router";
import Logo from "./Logo";
import TenantSwitcher from "./TenantSwitcher";
import UserMenu from "./UserMenu";
import { MenuIcon } from "./icons";

interface TopBarProps {
  // Only passed on phones, where the navigation lives in a drawer.
  onOpenMenu?: () => void;
}

export default function TopBar({ onOpenMenu }: TopBarProps) {
  return (
    <header className="topbar">
      {onOpenMenu && (
        <button
          type="button"
          className="icon-button"
          aria-label="Open menu"
          aria-haspopup="dialog"
          onClick={onOpenMenu}
        >
          <MenuIcon />
        </button>
      )}

      {/* Phones only (wide screens show the brand in the sidebar), and there just the logo:
          the room goes to the tenant name, which says whose data this is. The name stays
          in the link for screen readers. */}
      <Link to="/" className="topbar-brand">
        <Logo />
        <span className="visually-hidden">Dashboard Starter</span>
      </Link>

      <div className="topbar-spacer" />

      <TenantSwitcher />
      <UserMenu />
    </header>
  );
}
