// The one place the front end talks HTTP to the API. Plain fetch, no client library.
// Each resource has its own hook next to its types (health/useHealth.ts, tenants/useTenants.ts)
// and calls these functions; components never call fetch.

// Any answer that isn't 2xx. `body` is the parsed JSON when the server sent some
// (the health endpoint answers 503 with a useful body), otherwise null.
export class ApiError extends Error {
  readonly status: number;
  readonly body: unknown;

  constructor(status: number, body: unknown) {
    super(`The API answered HTTP ${status}.`);
    this.name = "ApiError";
    this.status = status;
    this.body = body;
  }
}

function buildHeaders(): Headers {
  const headers = new Headers({ Accept: "application/json" });

  // STEP 5: state-changing requests (POST, PUT, DELETE) will add the antiforgery token
  // header here. Nothing sends one yet, because every call so far is a GET.

  return headers;
}

export async function apiGet<T>(
  path: string,
  options: { signal?: AbortSignal } = {},
): Promise<T> {
  const response = await fetch(path, {
    headers: buildHeaders(),
    signal: options.signal,
  });

  if (!response.ok) {
    throw new ApiError(response.status, await readJson(response));
  }

  return (await response.json()) as T;
}

async function readJson(response: Response): Promise<unknown> {
  try {
    return await response.json();
  } catch {
    // An error page from a proxy, or an empty body.
    return null;
  }
}
