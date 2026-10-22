import { useEffect, useState, type ReactNode } from "react";
import { checkHealth, type HealthCheck } from "./health";

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
        Is everything connected? This page calls <code>GET /api/health</code>{" "}
        through the Vite proxy.
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
    </main>
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
  const database = health.database;
  const checkedAt = new Date(health.checkedAt).toLocaleTimeString();

  return (
    <>
      <Check label="API" ok={true} detail={`Answered at ${checkedAt}.`} />
      {database.reachable ? (
        <Check
          label="Database"
          ok={true}
          detail={
            <>
              SQL Server {database.serverVersion}. Schema version{" "}
              <code>{database.schemaVersion ?? "none"}</code> (
              {database.appliedMigrations} of {database.expectedMigrations}{" "}
              migrations applied).
            </>
          }
        />
      ) : (
        <Check
          label="Database"
          ok={false}
          detail="The API can't reach SQL Server. Is it running? docker compose up -d --wait"
        />
      )}
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
