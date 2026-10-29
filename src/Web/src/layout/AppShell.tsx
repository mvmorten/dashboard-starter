import { useEffect, useRef, type MouseEvent, type ReactNode } from "react";
import { useLocation } from "react-router";
import { BuildFooter, StaleNotice } from "./BuildNotices";
import Sidebar from "./Sidebar";
import TopBar from "./TopBar";
import { useMediaQuery } from "./useMediaQuery";

// From this width up the navigation is a permanent sidebar; below it, a drawer.
// Keep in step with the 48rem breakpoint in styles/shell.css.
const WIDE_QUERY = "(min-width: 48rem)";

export default function AppShell({ children }: { children: ReactNode }) {
  const isWide = useMediaQuery(WIDE_QUERY);
  const drawerRef = useRef<HTMLDialogElement>(null);
  const mainRef = useRef<HTMLElement>(null);
  const { pathname } = useLocation();
  const previousPath = useRef(pathname);

  // A page change in a single-page app doesn't move keyboard focus by itself, so focus
  // would stay on the link that was clicked. Move it to the top of the new page instead.
  useEffect(() => {
    if (previousPath.current === pathname) {
      return;
    }

    previousPath.current = pathname;
    mainRef.current?.focus();
  }, [pathname]);

  function closeDrawer() {
    drawerRef.current?.close();
  }

  // A click on the dimmed area behind the drawer lands on the <dialog> itself.
  function closeOnBackdropClick(event: MouseEvent<HTMLDialogElement>) {
    if (event.target === event.currentTarget) {
      closeDrawer();
    }
  }

  return (
    <div className="shell">
      <a className="skip-link" href="#main">
        Skip to content
      </a>

      <TopBar
        onOpenMenu={isWide ? undefined : () => drawerRef.current?.showModal()}
      />

      {isWide ? (
        <aside className="sidebar">
          <Sidebar />
        </aside>
      ) : (
        // showModal() makes everything else inert, closes on Escape, and returns focus to
        // the menu button when it closes.
        <dialog
          ref={drawerRef}
          className="drawer"
          aria-label="Menu"
          onClick={closeOnBackdropClick}
        >
          <Sidebar onClose={closeDrawer} />
        </dialog>
      )}

      <main id="main" ref={mainRef} tabIndex={-1} className="content">
        <div className="content-inner">
          <StaleNotice />
          {children}
          <BuildFooter />
        </div>
      </main>
    </div>
  );
}
