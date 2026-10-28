import type { ReactNode } from "react";
import type { DatabaseHealth } from "../health/health";
import { useHealth } from "../health/useHealth";
import { useCurrentTenant } from "../tenants/useCurrentTenant";
import { usePageTitle } from "../layout/usePageTitle";

export default function Health() {
  usePageTitle("Health");
  const { data, isFetching, refetch } = useHealth();
  const tenant = useCurrentTenant();

  return (
    <>
      <h1>Health</h1>
      <p className="lead">
        Is everything connected? This page calls <code>GET /api/health</code>
        {import.meta.env.DEV ? " through the Vite proxy." : "."}
      </p>

      <p className="tenant-note">
        Current tenant: <strong>{tenant ? tenant.name : "none"}</strong>. This
        is your choice in the top bar, kept in this browser. The API doesn't use
        it yet.
      </p>

      <ul className="checks">
        <Check
          label="Front end"
          ok={true}
          detail="React is rendering this page."
        />
        <ApiAndDatabase data={data} />
      </ul>

      <button
        type="button"
        onClick={() => void refetch()}
        disabled={isFetching}
      >
        {isFetching ? "Checking..." : "Check again"}
      </button>
    </>
  );
}

function ApiAndDatabase({
  data,
}: {
  data: ReturnType<typeof useHealth>["data"];
}) {
  if (!data) {
    return (
      <>
        <Check label="API" ok={null} detail="Checking..." />
        <Check label="Database" ok={null} detail="Checking..." />
      </>
    );
  }

  if (data.kind === "apiDown") {
    return (
      <>
        <Check label="API" ok={false} detail={data.message} />
        <Check
          label="Database"
          ok={null}
          detail="Unknown until the API answers."
        />
      </>
    );
  }

  const { health } = data;
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
