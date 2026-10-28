import { useEffect } from "react";

// Sets the browser tab (and screen reader) title for the current page.
export function usePageTitle(title: string): void {
  useEffect(() => {
    document.title = `${title} | Dashboard Starter`;
  }, [title]);
}
