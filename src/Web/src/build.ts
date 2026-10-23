// Which build this page is. The front end and the API ship in one zip, so this is the
// same build the API reports in GET /api/health (src/Api/Hosting/BuildInfo.cs).

export interface BuildInfo {
  number: string | null;
  commit: string | null;
  builtAt: string | null;
}

export const pageBuild: BuildInfo = {
  number: import.meta.env.VITE_BUILD_NUMBER || null,
  commit: import.meta.env.VITE_BUILD_COMMIT || null,
  builtAt: import.meta.env.VITE_BUILD_TIME || null,
};

export const repoUrl: string | null = import.meta.env.VITE_REPO_URL || null;

// True when the API is running a different build than the page in this tab.
// That happens when a deploy lands while the tab is open: the API is new, the
// JavaScript already loaded here is not. Local builds have no commit, so never.
export function isStale(apiBuild: BuildInfo | undefined): boolean {
  if (!pageBuild.commit || !apiBuild?.commit) {
    return false;
  }

  return pageBuild.commit !== apiBuild.commit;
}
