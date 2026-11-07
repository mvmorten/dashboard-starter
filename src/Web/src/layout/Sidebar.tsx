import { NavLink } from "react-router";
import { Permissions, useCan } from "../auth/permissions";
import Logo from "./Logo";
import { CloseIcon, DashboardIcon, HealthIcon, MembersIcon } from "./icons";

interface SidebarProps {
  // Set when the sidebar is shown in the phone drawer: adds a close button and is also
  // called after following a link, so the drawer closes.
  onClose?: () => void;
}

export default function Sidebar({ onClose }: SidebarProps) {
  const can = useCan();

  return (
    <>
      <div className="sidebar-head">
        <p className="sidebar-title">
          <Logo />
          Dashboard Starter
        </p>
        {onClose && (
          <button
            type="button"
            className="icon-button"
            aria-label="Close menu"
            onClick={onClose}
          >
            <CloseIcon />
          </button>
        )}
      </div>

      <nav aria-label="Main">
        <ul className="nav-list">
          <li>
            <NavLink to="/" end className="nav-link" onClick={onClose}>
              <DashboardIcon />
              Dashboard
            </NavLink>
          </li>
          {/* Hidden without the permission; the page and the API check it too. */}
          {can(Permissions.membersView) && (
            <li>
              <NavLink to="/members" className="nav-link" onClick={onClose}>
                <MembersIcon />
                Members
              </NavLink>
            </li>
          )}
          <li>
            <NavLink to="/health" className="nav-link" onClick={onClose}>
              <HealthIcon />
              Health
            </NavLink>
          </li>
        </ul>
      </nav>
    </>
  );
}
