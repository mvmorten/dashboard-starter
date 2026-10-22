// Types and fetch for GET /api/health (src/Api/Health/HealthController.cs).

export interface DatabaseHealth {
  reachable: boolean;
  serverVersion: string | null;
  schemaVersion: string | null;
  appliedMigrations: number;
  expectedMigrations: number;
}

// Outside Development the API sends only `status`; the details stay on dev machines.
export interface HealthResponse {
  status: "Healthy" | "Unhealthy";
  checkedAt?: string;
  database?: DatabaseHealth;
}

export type HealthCheck =
  | { kind: "answered"; health: HealthResponse }
  | { kind: "apiDown"; message: string };

export async function checkHealth(signal: AbortSignal): Promise<HealthCheck> {
  let response: Response;
  try {
    response = await fetch("/api/health", { signal });
  } catch {
    return { kind: "apiDown", message: "The dev server didn't respond." };
  }

  // 503 still carries a body: the API is up but can't reach the database.
  if (response.status !== 200 && response.status !== 503) {
    return {
      kind: "apiDown",
      message: `The API didn't answer (HTTP ${response.status}). Is it running? dotnet run --project src/Api`,
    };
  }

  const health = (await response.json()) as HealthResponse;
  return { kind: "answered", health };
}
