import { NavLink } from "react-router";
import { CloseIcon, DashboardIcon, HealthIcon } from "./icons";

interface SidebarProps {
  // Set when the sidebar is shown in the phone drawer: adds a close button and is also
  // called after following a link, so the drawer closes.
  onClose?: () => void;
}

export default function Sidebar({ onClose }: SidebarProps) {
  return (
    <>
      <div className="sidebar-head">
        <p className="sidebar-title">Dashboard Starter</p>
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
