// Types and fetch for GET /api/health (src/Api/Health/HealthController.cs).

import { ApiError, apiGet } from "../api";
import type { BuildInfo } from "../build";

export interface DatabaseHealth {
  reachable: boolean;
  serverVersion: string | null;
  schemaVersion: string | null;
  appliedMigrations: number;
  expectedMigrations: number;
}

// Outside Development the API sends only `status` and `build`; the database details
// stay on dev machines.
export interface HealthResponse {
  status: "Healthy" | "Unhealthy";
  build: BuildInfo;
  checkedAt?: string;
  database?: DatabaseHealth;
}

export type HealthCheck =
  | { kind: "answered"; health: HealthResponse }
  | { kind: "apiDown"; message: string };

// The query function behind useHealth(). "The API is down" is an answer to the question
// this page asks, not an error, so it comes back as data rather than a thrown error.
export async function checkHealth(signal: AbortSignal): Promise<HealthCheck> {
  try {
    const health = await apiGet<HealthResponse>("/api/health", { signal });
    return { kind: "answered", health };
  } catch (error) {
    if (signal.aborted) {
      throw error;
    }

    // 503 still carries a body: the API is up but can't reach the database.
    if (error instanceof ApiError && error.status === 503 && error.body) {
      return { kind: "answered", health: error.body as HealthResponse };
    }

    if (error instanceof ApiError) {
      return {
        kind: "apiDown",
        message: import.meta.env.DEV
          ? `The API didn't answer (HTTP ${error.status}). Is it running? dotnet run --project src/Api`
          : `The API didn't answer (HTTP ${error.status}).`,
      };
    }

    return {
      kind: "apiDown",
      message: import.meta.env.DEV
        ? "The dev server didn't respond."
        : "The server didn't respond.",
    };
  }
}
