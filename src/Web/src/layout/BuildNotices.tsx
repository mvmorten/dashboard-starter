// Build information in the shell: the footer line, and the banner that appears when a deploy
// landed after this tab loaded. Both were on the health page in step 3; they live in the
// shell now so they show on every page.

import { isStale, pageBuild, repoUrl, type BuildInfo } from "../build";
import { useHealth } from "../health/useHealth";

// Shown when a deploy landed after this tab loaded the page.
export function StaleNotice() {
  const { data } = useHealth();
  const apiBuild = data?.kind === "answered" ? data.health.build : undefined;

  if (!apiBuild || !isStale(apiBuild)) {
    return null;
  }

  return (
    <p className="stale" role="status">
      A newer build{apiBuild.number ? ` (${apiBuild.number})` : ""} is live.{" "}
      <button type="button" onClick={() => window.location.reload()}>
        Reload
      </button>
    </p>
  );
}

export function BuildFooter() {
  return (
    <footer className="build">
      <BuildLine build={pageBuild} />
    </footer>
  );
}

function BuildLine({ build }: { build: BuildInfo }) {
  if (!build.commit) {
    return <>Local development build</>;
  }

  const shortCommit = build.commit.slice(0, 7);
  const commit = repoUrl ? (
    <a href={`${repoUrl}/commit/${build.commit}`}>
      <code>{shortCommit}</code>
    </a>
  ) : (
    <code>{shortCommit}</code>
  );
  const builtAt = build.builtAt
    ? new Date(build.builtAt).toLocaleString(undefined, {
        dateStyle: "medium",
        timeStyle: "short",
      })
    : null;

  return (
    <>
      Build {build.number ?? "?"} · {commit}
      {builtAt ? ` · ${builtAt}` : ""}
    </>
  );
}
