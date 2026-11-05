// The one place the front end talks HTTP to the API. Plain fetch, no client library.
// Each resource has its own hook next to its types (health/useHealth.ts, auth/useMe.ts)
// and calls these functions; components never call fetch.

// Any answer that isn't 2xx. `body` is the parsed JSON when the server sent some
// (the health endpoint answers 503 with a useful body, errors are ProblemDetails),
// otherwise null.
export class ApiError extends Error {
  readonly status: number;
  readonly body: unknown;

  constructor(status: number, body: unknown) {
    super(`The API answered HTTP ${status}.`);
    this.name = "ApiError";
    this.status = status;
    this.body = body;
  }

  // The human-readable `detail` of a ProblemDetails body, if there is one.
  get detail(): string | null {
    return this.problemField("detail");
  }

  // The ProblemDetails `title`: a short, fixed name for the kind of failure.
  get title(): string | null {
    return this.problemField("title");
  }

  private problemField(name: "detail" | "title"): string | null {
    if (this.body && typeof this.body === "object" && name in this.body) {
      const value = (this.body as Record<string, unknown>)[name];
      return typeof value === "string" ? value : null;
    }

    return null;
  }
}

// What to do when a request answers 401: the session ended (signed out elsewhere, password
// changed, access removed, or it expired). main.tsx sets this to clear the cached user,
// which sends the protected layout to the sign-in page.
let unauthorizedHandler: (() => void) | null = null;

export function setUnauthorizedHandler(handler: () => void): void {
  unauthorizedHandler = handler;
}

interface RequestOptions {
  signal?: AbortSignal;
  // For calls where 401 is an ordinary answer (who am I? wrong password), not a sign that
  // the session ended.
  unauthorizedIsAnswer?: boolean;
}

export async function apiGet<T>(
  path: string,
  options: RequestOptions = {},
): Promise<T> {
  const response = await fetch(path, {
    headers: { Accept: "application/json" },
    signal: options.signal,
  });

  await throwIfFailed(response, options);
  return (await response.json()) as T;
}

// POST with a JSON body. Sends the antiforgery token the API checks on every
// state-changing request. Returns undefined for 204 No Content.
export async function apiPost<T>(
  path: string,
  body?: unknown,
  options: RequestOptions = {},
): Promise<T> {
  if (readAntiforgeryToken() === null) {
    await refreshAntiforgeryToken();
  }

  let response = await post(path, body, options.signal);

  // The token was issued for a session that has since changed (it ended on the server, or
  // another tab switched tenants). Get one for the current session and try once more.
  if (response.status === 400 && (await isAntiforgeryRejection(response))) {
    await refreshAntiforgeryToken();
    response = await post(path, body, options.signal);
  }

  await throwIfFailed(response, options);
  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

// Antiforgery (src/Api/Auth/AuthCookies.cs): GET /api/auth/antiforgery sets a cookie that
// script can read, and every POST copies it into this header. Another site can make the
// browser send our cookies, but it can't read them, so it can't send the header.
//
// The token is tied to who is signed in and to their current tenant, so the app fetches a
// new one after sign-in, sign-out, and switching tenants (auth/session.ts).
const antiforgeryHeader = "X-XSRF-TOKEN";

// Deployed, the cookie's name has the __Host- prefix; in local development it doesn't.
const antiforgeryCookieNames = ["__Host-ds-xsrf", "ds-xsrf"];

export async function refreshAntiforgeryToken(): Promise<void> {
  const response = await fetch("/api/auth/antiforgery");
  if (!response.ok) {
    throw new ApiError(response.status, await readJson(response));
  }
}

function post(
  path: string,
  body: unknown,
  signal: AbortSignal | undefined,
): Promise<Response> {
  const headers = new Headers({ Accept: "application/json" });
  if (body !== undefined) {
    headers.set("Content-Type", "application/json");
  }

  const token = readAntiforgeryToken();
  if (token !== null) {
    headers.set(antiforgeryHeader, token);
  }

  return fetch(path, {
    method: "POST",
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
    signal,
  });
}

// The title AntiforgeryFilter (src/Api/Auth/AntiforgeryFilter.cs) answers with. Reads a
// clone, so the original body is still there for the error.
async function isAntiforgeryRejection(response: Response): Promise<boolean> {
  const body = await readJson(response.clone());
  return (
    !!body &&
    typeof body === "object" &&
    "title" in body &&
    body.title === "Missing or invalid antiforgery token"
  );
}

function readAntiforgeryToken(): string | null {
  for (const cookie of document.cookie.split("; ")) {
    const separator = cookie.indexOf("=");
    const name = cookie.slice(0, separator);
    if (antiforgeryCookieNames.includes(name)) {
      return decodeURIComponent(cookie.slice(separator + 1));
    }
  }

  return null;
}

async function throwIfFailed(
  response: Response,
  options: RequestOptions,
): Promise<void> {
  if (response.ok) {
    return;
  }

  if (response.status === 401 && !options.unauthorizedIsAnswer) {
    unauthorizedHandler?.();
  }

  throw new ApiError(response.status, await readJson(response));
}

async function readJson(response: Response): Promise<unknown> {
  try {
    return await response.json();
  } catch {
    // An error page from a proxy, or an empty body.
    return null;
  }
}
