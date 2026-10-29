import { Link } from "react-router";
import Logo from "./Logo";
import TenantSwitcher from "./TenantSwitcher";
import ThemeToggle from "./ThemeToggle";
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

      <Link to="/" className="topbar-brand">
        <Logo />
        Dashboard Starter
      </Link>

      <div className="topbar-spacer" />

      <TenantSwitcher />
      <ThemeToggle />
    </header>
  );
}
