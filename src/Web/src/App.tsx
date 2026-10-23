import { useEffect, useState, type ReactNode } from "react";
import { isStale, pageBuild, repoUrl, type BuildInfo } from "./build";
import { checkHealth, type DatabaseHealth, type HealthCheck } from "./health";

type State = { kind: "checking" } | HealthCheck;

export default function App() {
  const [state, setState] = useState<State>({ kind: "checking" });
  const [checkCount, setCheckCount] = useState(0);

  // Runs on load, and again each time "Check again" bumps checkCount.
  useEffect(() => {
    const controller = new AbortController();
    checkHealth(controller.signal).then((result) => {
      if (!controller.signal.aborted) {
        setState(result);
      }
    });
    return () => controller.abort();
  }, [checkCount]);

  function checkAgain() {
    setState({ kind: "checking" });
    setCheckCount((count) => count + 1);
  }

  return (
    <main>
      <h1>Dashboard Starter</h1>
      <p className="lead">
        Is everything connected? This page calls <code>GET /api/health</code>
        {import.meta.env.DEV ? " through the Vite proxy." : "."}
      </p>

      <ul className="checks">
        <Check
          label="Front end"
          ok={true}
          detail="React is rendering this page."
        />
        <ApiAndDatabase state={state} />
      </ul>

      <button onClick={checkAgain} disabled={state.kind === "checking"}>
        Check again
      </button>

      <StaleNotice
        apiBuild={state.kind === "answered" ? state.health.build : undefined}
      />

      <footer className="build">
        <BuildLine build={pageBuild} />
      </footer>
    </main>
  );
}

// Shown when a deploy landed after this tab loaded the page.
function StaleNotice({ apiBuild }: { apiBuild: BuildInfo | undefined }) {
  if (!apiBuild || !isStale(apiBuild)) {
    return null;
  }

  return (
    <p className="stale" role="status">
      A newer build{apiBuild.number ? ` (${apiBuild.number})` : ""} is live.{" "}
      <button onClick={() => window.location.reload()}>Reload</button>
    </p>
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

function ApiAndDatabase({ state }: { state: State }) {
  if (state.kind === "checking") {
    return (
      <>
        <Check label="API" ok={null} detail="Checking..." />
        <Check label="Database" ok={null} detail="Checking..." />
      </>
    );
  }

  if (state.kind === "apiDown") {
    return (
      <>
        <Check label="API" ok={false} detail={state.message} />
        <Check
          label="Database"
          ok={null}
          detail="Unknown until the API answers."
        />
      </>
    );
  }

  const { health } = state;
  const healthy = health.status === "Healthy";
  const apiDetail = health.checkedAt
    ? `Answered at ${new Date(health.checkedAt).toLocaleTimeString()}.`
    : "Answered.";

  return (
    <>
      <Check label="API" ok={true} detail={apiDetail} />
      <Check
        label="Database"
        ok={healthy}
        detail={
          healthy ? (
            <DatabaseDetail database={health.database} />
          ) : import.meta.env.DEV ? (
            "The API can't reach SQL Server. Is it running? docker compose up -d --wait"
          ) : (
            // A serverless database (useFreeDatabase in infra/main.bicep) pauses when idle.
            "The API can't reach the database. If it's waking up from a pause, try again in a minute."
          )
        }
      />
    </>
  );
}

// The API only sends database details in Development.
function DatabaseDetail({ database }: { database?: DatabaseHealth }) {
  if (!database) {
    return <>Reachable. Version details are only shown in Development.</>;
  }

  return (
    <>
      SQL Server {database.serverVersion}. Schema version{" "}
      <code>{database.schemaVersion ?? "none"}</code> (
      {database.appliedMigrations} of {database.expectedMigrations} migrations
      applied).
    </>
  );
}

interface CheckProps {
  label: string;
  // null means "don't know yet".
  ok: boolean | null;
  detail: ReactNode;
}

function Check({ label, ok, detail }: CheckProps) {
  let status = "unknown";
  if (ok === true) {
    status = "ok";
  } else if (ok === false) {
    status = "failed";
  }

  return (
    <li className={`check check-${status}`}>
      <span className="check-dot" aria-hidden="true" />
      <span className="check-label">{label}</span>
      <span className="check-status">{status}</span>
      <span className="check-detail">{detail}</span>
    </li>
  );
}
