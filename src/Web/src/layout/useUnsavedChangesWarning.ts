import { useEffect } from "react";

const question =
  "You have unsaved changes. Leave this page without saving them?";

// While `unsaved` is true, asks before the page is left: closing the tab or reloading (the
// browser's own question), and following a link inside the app (ours).
//
// React Router's useBlocker would do the second part, but only in its data mode, and this app
// uses declarative mode (README, step 4). So this listens for clicks on links itself, before
// React Router sees them: a click listener on window in the capture phase runs before React's
// listeners on the root element. Saying "stay" stops the click there.
//
// It doesn't cover the browser's Back button, or buttons that navigate (sign out, switching
// tenants). Those leave without asking.
export function useUnsavedChangesWarning(unsaved: boolean): void {
  useEffect(() => {
    if (!unsaved) {
      return;
    }

    function onBeforeUnload(event: BeforeUnloadEvent) {
      event.preventDefault();
    }

    function onClick(event: MouseEvent) {
      if (!leavesThisPage(event)) {
        return;
      }

      if (!window.confirm(question)) {
        event.preventDefault();
        event.stopPropagation();
      }
    }

    window.addEventListener("beforeunload", onBeforeUnload);
    window.addEventListener("click", onClick, true);
    return () => {
      window.removeEventListener("beforeunload", onBeforeUnload);
      window.removeEventListener("click", onClick, true);
    };
  }, [unsaved]);
}

// A plain click on a link that goes somewhere else in this tab. Ctrl-click and the like open a
// new tab, and this page stays as it is.
function leavesThisPage(event: MouseEvent): boolean {
  if (
    event.button !== 0 ||
    event.metaKey ||
    event.ctrlKey ||
    event.shiftKey ||
    event.altKey
  ) {
    return false;
  }

  const link =
    event.target instanceof Element ? event.target.closest("a[href]") : null;
  if (!(link instanceof HTMLAnchorElement)) {
    return false;
  }

  if (link.target && link.target !== "_self") {
    return false;
  }

  const to = new URL(link.href, window.location.href);
  return (
    to.pathname !== window.location.pathname ||
    to.search !== window.location.search
  );
}
