# CLAUDE.md

Instructions for Claude Code (and any AI assistant) working in this repo. The README explains the decisions and the reasons behind them; this file is the short list of rules to follow. When the two disagree, the README wins and this file should be fixed.

## What this project is

An opinionated, open-source dashboard starter: React + TypeScript, ASP.NET Core, SQL Server. It is built in public, one blog post per step, and every step must leave the repo in a state a stranger can clone and run.

**The guiding principle: no magic.** The audience is developers who want to understand the whole stack. A reader should be able to follow any feature from a React component, through the API, to the exact SQL it runs. When a framework feature does a lot of work out of sight, prefer the plainer option you can read. Several popular defaults were rejected on purpose for this reason; do not reintroduce them (see "Do not" below).

## Workflow

- Never commit to `main`. `main` is protected: pull request required, squash merge only, linear history.
- One branch per change, named by area or step: `step-2/solution-layout`, `docs/readme-fix`.
- Every PR description explains what changed and **why**. PR descriptions become squash commit messages and are source material for the blog posts.
- Keep a short **build log** in the PR description while working: decisions made, things that surprised you, anything that broke and how it was fixed. Mistakes are part of the story; include them.
- Each blog post gets a release tag `v0.N` (post number). The maintainer creates tags and releases.
- If a change needs a decision that isn't covered here or in the README, stop and ask rather than picking a default.

## Stack

- **Front end:** React + TypeScript, Vite. Library choices beyond that (UI kit, grid, data fetching, router) are decided in the app shell step (step 4); don't add them before then.
- **API:** ASP.NET Core Web API with **controllers**, current .NET LTS.
- **Database:** SQL Server, one shared database for all tenants.
- **Data access:** Dapper, hand-written SQL inline in C#.
- **Schema:** idempotent SQL scripts in `/sql`, applied by `src/Db`.
- **Auth:** own user tables, Argon2id hashing, ASP.NET Core cookie authentication.
- **Local dev:** Docker Compose runs SQL Server only. API and front end run natively.
- **CI and deploy:** GitHub Actions. Hosting: Azure App Service + Azure SQL (see "Deployment").

## Code conventions

Enforced by `.editorconfig`, ESLint, and Prettier where possible.

- **2-space indentation everywhere** (C#, TS, SQL, JSON, YAML). Spaces, not tabs. Intentionally not the .NET default of 4.
- **Braces always.** Never a single-line `if`/`for`/`while` without braces, in C# or TypeScript.
- **Prefer early returns** (guard clauses) over nesting. Pull deep nesting into well-named methods.
- **Brace placement:** own line in C# (Allman), same line in TypeScript (Prettier default).
- **C#:**
  - Classic `Program` class with `static void Main` / `static async Task Main`. No top-level statements.
  - `<ImplicitUsings>disable</ImplicitUsings>`. Every file lists its own `using` lines.
  - File-scoped namespaces (`namespace DashboardStarter.Api.Users;`).
  - Nullable reference types enabled.
- **TypeScript / React:** standard Prettier formatting; ESLint with `curly: "all"`.
- **Mobile first.** Write the base CSS for a phone, then add `min-width` media queries for larger screens (never the other way around).
  - Every screen works at 390 px wide with no sideways scrolling of the page. Check at 390 px and 1280 px before opening a PR, and say in the PR that you did.
  - Wide content (data grids, code, diagrams) scrolls inside its own container, never the page.
  - Touch targets at least 44 x 44 px. Form inputs at least 16 px font size, so iOS doesn't zoom in on focus.
  - Don't rely on hover: anything a hover reveals must also be reachable by tap and keyboard.

## Logging

- Built-in `Microsoft.Extensions.Logging` with the console provider. No logging library.
- Always inject `ILogger<T>` (constructor injection). Not `ILogger`, not `ILoggerFactory`, not `app.Logger`.
- Message templates with named placeholders, never interpolation: `_logger.LogInformation("Applied {Migration}", name)`, not `$"Applied {name}"`. CA2254 is an error, so the build enforces this.
- Never log secrets: passwords, connection strings, tokens, cookies, password hashes.
- Not yet: per-request scopes, JSON output, and a global exception handler come in later steps. Don't add them early.
- The Db runner is a CLI: its per-file results go to `Console`. That's output, not logging.

## Dependencies

- Prefer what's built in: .NET and ASP.NET Core, browser APIs, React.
- A library must earn its place through **necessity** (no built-in way), **safety** (closes a security hole), or a **real gain in correctness** (Dapper: hand-written row mapping is error-prone code). Convenience alone isn't enough.
- Loosely held: a guide for the decision, not a ban on good libraries.
- Ask before adding any dependency (NuGet or npm, runtime or dev tooling). Justify it in the PR and add a row to the README's Dependencies table. Removing one also updates the table.

## Database conventions

- **Keys:** `int IDENTITY` on every table; `bigint` only for tables that grow without limit (audit log, events). No public GUID columns.
- **Tenant-owned tables** carry `TenantId`, lead their indexes with it, and are covered by the Row-Level Security policy.
- **Global tables** (no `TenantId`, no RLS): `Users`, `Tenants`, `TenantUsers`. One user account can belong to many tenants; roles are per tenant.
- **Standard columns** on tenant-owned tables: `TenantId`, `CreatedAt`, `CreatedBy`, `UpdatedAt`, `UpdatedBy` (`datetime2`, UTC), `RowVersion rowversion`. Always write `rowversion`, never `timestamp`.
- **Optimistic concurrency:** updates include `AND RowVersion = @RowVersion` and use `OUTPUT inserted.RowVersion`. Zero rows updated: 404 if the row is gone, otherwise 409 Conflict with the current version.
- **Row-Level Security:** the API sets `SESSION_CONTEXT(N'TenantId')` on each connection; a security policy filters every tenant-owned table. App queries still filter by `TenantId` explicitly; RLS is the safety net, not the primary filter.
- **SQL in C#:** raw string literals (`"""`), **parameters only**. Never build SQL with `$` interpolation or concatenation of values. Dynamic pieces (sort column, direction) come only from a fixed allow-list in code.
- **Stored procedures** only where they clearly help (set-based batch work, heavy reporting). Default is inline SQL.

### `/sql` folder

```
/sql
  /migrations     run once, in order; numbered 0001_name.sql; idempotent; never edited after they have run
  /programmable   CREATE OR ALTER; rerun every time; edit in place
  /seed           development only
```

- Every migration checks before it changes (`IF NOT EXISTS`, `IF COL_LENGTH(...) IS NULL`, `IF OBJECT_ID(...) IS NULL`).
- To change something a migration already did, write a new migration.
- Apply with `dotnet run --project src/Db -- migrate`; seed with `dotnet run --project src/Db -- seed`; create or update the API's login with `dotnet run --project src/Db -- app-user`.
- The runner journals migrations in `SchemaVersions` (file name, content hash, applied at), skips applied ones, and fails if an applied migration's hash changed.
- **The API never changes the schema.** At startup it only checks that no migrations are pending and refuses to start with a clear message naming the command to run.

## Deployment

- Azure App Service (Linux) runs the API and serves the React build from `wwwroot`; Azure SQL holds the data. Resources are defined in `infra/main.bicep`; the one-time setup is in `docs/deploy-azure.md`.
- `.github/workflows/deploy.yml` deploys `main` after CI passes: build, `migrate`, `app-user`, set app settings, zip deploy, health check. Order matters: migrate before deploying code. Only CI calls it; to redeploy, run CI by hand on `main`.
- One build stamp (CI run number, commit, UTC time) for the whole app: passed to `dotnet publish` as `-p:BuildNumber/BuildCommit/BuildTime` and to Vite as `VITE_BUILD_*`. The health check must see the deployed commit, not just "Healthy".
- The deploy runs `migrate` **before** the new code starts, so the old code briefly runs against the new schema. Migrations must only add (new tables, nullable or defaulted columns). Removing or renaming something takes two deploys: stop using it, then drop it.
- The API connects as its own login in the `app_runtime` role (data only, no DDL). Only the Db runner uses the admin login. Never give the API's login schema rights.
- No Azure-specific code in the app. Configuration comes from environment variables (`ConnectionStrings__Default`, `CanonicalHost`).
- Secrets live in the GitHub `demo` environment and App Service settings, never in the repo or the Bicep file. Bicep must not set app settings: it would replace the whole set and wipe the connection string.

## Security rules

- Passwords: Argon2id behind an `IPasswordHasher` interface; parameters m=19456 KiB, t=2, p=1, configurable; PHC string format; rehash on sign-in when parameters are weaker than current config.
- Random values for anything security-related (tokens, salts, codes): `System.Security.Cryptography.RandomNumberGenerator` only. Store only a SHA-256 hash of tokens.
- Cookies: `HttpOnly`, `Secure`, `SameSite`; antiforgery tokens on state-changing requests.
- Before opening a PR that touches authentication, authorization, tenant filtering or RLS, cookies, tokens, SQL, file handling, or the workflows, run `/security-review` and put a one-line summary of its result in the PR description (findings fixed, or why they don't apply).
- CodeQL runs on every PR from `.github/workflows/codeql.yml` (advanced setup). Keep CodeQL "default setup" off in the repo settings: GitHub rejects the workflow's results while it is on. Fix its findings rather than dismissing them; if one is a false positive, say why in the PR.
- Security reports go through GitHub private vulnerability reporting (`SECURITY.md`). Never discuss an unfixed vulnerability in a public issue, PR, or commit message.

## Do not

- Add Entity Framework Core, ASP.NET Core Identity, JWT bearer auth for the browser, minimal API endpoints, or top-level statements.
- Turn on implicit usings.
- Run migrations from the API, from a hosted service, or on startup in any environment.
- Use `System.Random` or `Guid.NewGuid()` for tokens or secrets.
- Interpolate values into SQL strings.
- Add a front-end library, a NuGet package, or dev tooling without asking first (see "Dependencies").
- Interpolate values into log messages, or log secrets.
- Commit secrets. Local dev credentials live in `.env` (gitignored) with a committed `.env.example`.

## Writing style for docs, PRs, and comments

- Plain, direct English. Explain the "why", not just the "what".
- Don't use em dashes.
