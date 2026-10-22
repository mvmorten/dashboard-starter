# dashboard-starter

An opinionated, open-source dashboard starter for building real business apps with **React**, **C# / ASP.NET Core**, and **SQL Server**.

Clone it, rename it, and start on your actual product instead of rebuilding auth, layout, and plumbing for the tenth time.

> **Status:** Just getting started. This project is being built in public, and every step is documented in a blog series. Expect the code to change quickly while the foundation takes shape.

---

## Why this exists

Most starter templates fall into one of two camps: a pretty UI with no real backend, or a backend sample with no usable front end. Very few show how a full production app fits together end to end, and fewer still explain *why* each decision was made.

**This project is for developers who want to understand the whole stack.** You should be able to follow any feature from a React component, through the API, to the exact SQL it runs, without digging through framework internals. Most of the choices below follow from that: when a tool does a lot of work you can't see, we usually pick the plainer option you can read.

This repo aims to be:

- **Usable.** A working dashboard you can fork and ship on.
- **Opinionated.** One clear way to do things, not a menu of every option.
- **Explained.** Every major decision is written up in the accompanying blog posts, so you can disagree with it knowingly.

## The stack

| Layer | Choice |
| --- | --- |
| Front end | React + TypeScript, built with Vite |
| API | ASP.NET Core Web API with controllers (C#, current .NET LTS) |
| Database | SQL Server, one shared database for all tenants |
| Data access | Dapper, with hand-written SQL inline in C# |
| Schema changes | Idempotent SQL scripts in `/sql`, applied by an explicit command |
| Multi-tenancy | `TenantId` on every tenant-owned table, backed by SQL Server Row-Level Security |
| Auth | Our own user tables, Argon2id password hashing, ASP.NET Core cookie authentication |
| Local dev | Docker Compose runs SQL Server only; the API and front end run natively |
| CI | GitHub Actions for build, test, and lint |

## Multi-tenancy

- **One shared database, many tenants.** One schema, one migration run, one backup, one connection string. That's the economy of scale that makes SaaS work. See the FAQ for why not one database per tenant.
- **One account, many tenants.** A person signs in once and can belong to several tenants. Nobody wants a separate account for every organization they work with.
- **Global tables** have no `TenantId`: `Users`, `Tenants`, and `TenantUsers`. Your account, email (unique across the system), password hash, and lockout state belong to you, not to any one tenant.
- **Everything else is tenant-owned**: it carries a `TenantId`, the `TenantId` leads its indexes, and every query filters by the current tenant.
- **Row-Level Security is the safety net.** The API puts the current tenant in `SESSION_CONTEXT` on each connection, and a security policy filters every tenant-owned table. A query that forgets its `WHERE TenantId = @TenantId` still can't see another tenant's rows.
- **Roles are per tenant.** You can be an admin in one tenant and read-only in another (`TenantUserRoles`).
- **Switching tenants.** With one tenant you go straight in. With several, you land in the one you used last and can switch from the top bar. The auth cookie holds your user and current tenant; switching checks your membership and issues a new cookie.
- **Removing access works right away.** Memberships can be disabled per tenant, and the API rechecks membership on each request (briefly cached).
- The development seed creates two demo tenants and a demo user who belongs to both. A real deployment starts empty: create your first tenant, and if your app only ever needs one, you can ignore the rest.

## Database conventions

- **Dapper, not an ORM.** Queries are plain SQL you can read, tune, and run in SSMS.
- **Inline SQL, parameters only.** Queries live in the C# that uses them, written as raw string literals (`"""`). Values always go in as parameters, never with `$` string interpolation. Dynamic pieces, like a sortable column, come only from a fixed list in code, never straight from user input.
- **Stored procedures only where they help**, such as set-based batch work or heavy reporting. See the FAQ.
- **Keys:** `int` identity on every table, `bigint` on tables that grow without limit (audit log, events). Links that must be unguessable, like password resets and invites, use random tokens, not IDs.
- **Standard columns** on every tenant-owned table: `TenantId`, `CreatedAt`, `CreatedBy`, `UpdatedAt`, `UpdatedBy` (`datetime2`, UTC), and `RowVersion`.
- **Optimistic concurrency with `rowversion`.** Every read returns the row's version. Every update includes it (`WHERE Id = @Id AND RowVersion = @RowVersion`). If someone else saved first, the API returns `409 Conflict` instead of silently overwriting their change. Always write `rowversion`, never `timestamp`: same type, but `timestamp` is the old name and looks like a date.
- **History:** system-versioned temporal tables keep every past version of a row, so the audit log doesn't depend on triggers.

### Schema scripts

```
/sql
  /migrations     run once, in order: tables, columns, indexes, data fixes
  /programmable   rerun every time: views, functions, procs, RLS predicates
  /seed           development only: demo tenant, users, sample data
```

- **Migrations** are numbered (`0001_tenants.sql`, `0002_users.sql`, ...) and idempotent: each checks before it changes (`IF NOT EXISTS`, `IF COL_LENGTH(...) IS NULL`), so running one twice is safe. Once a migration has run, it's never edited. A change is a new script.
- **Programmable** objects use `CREATE OR ALTER` and are edited in place. The file is the definition.
- **Applying them is a command you run on purpose:**

  ```
  dotnet run --project src/Db -- migrate    # migrations + programmable objects
  dotnet run --project src/Db -- seed       # development only
  ```

  The runner records each migration in a `SchemaVersions` table with a hash of its contents, skips ones already applied, and fails loudly if an applied script was edited. The same command runs on your laptop, in CI, and in deployment.
- **The API never changes the database.** On startup it only checks that the schema is current. If it isn't, it refuses to start and tells you which command to run.
- **Running a copied query in SSMS?** Set the tenant first, or Row-Level Security will return no rows:

  ```sql
  EXEC sp_set_session_context @key = N'TenantId', @value = 1;
  ```

## Code conventions

Mostly style, written down so contributors know what to expect. Enforced by `.editorconfig`, ESLint, and Prettier rather than code review.

- **2-space indentation everywhere**, spaces not tabs: C#, TypeScript, SQL, JSON, YAML. Yes, the .NET default is 4. Two is on purpose.
- **Braces always.** No single-line `if` statements, in C# or TypeScript.
- **Prefer early returns over nesting.** Guard clauses at the top of a method instead of wrapping the body in an `if`. If you're indented 14 levels deep, the indent width isn't the problem.
- **Brace placement** follows each language's default: own line in C#, same line in TypeScript.
- **C#:** a classic `Program` class with `Main` (no top-level statements), implicit usings off so every file lists its own `using` lines, file-scoped namespaces, and nullable reference types on.
- **Front end:** standard Prettier formatting, with ESLint enforcing braces.
- **Logging:** the built-in `Microsoft.Extensions.Logging` with the console provider. Always inject `ILogger<T>`, so every message is tagged with the class that wrote it. Write message templates with named placeholders, `_logger.LogInformation("Applied {Migration}", name)`, never string interpolation: interpolation loses the named values and builds the string even when that log level is off. The build enforces this (rule CA2254). Never log secrets: passwords, connection strings, tokens, cookies. Per-request scopes, JSON output, and a global exception handler come in later steps. The Db runner is a command-line tool, so it prints its results with `Console` directly; that's output, not logging.

## Dependencies

Every package is code someone else wrote that we have to keep up to date and trust, so the default is what's already there: .NET and ASP.NET Core, the browser's own APIs, and React. A library has to earn its place through **necessity** (there's no built-in way to do it), **safety** (it closes a security hole), or a **real gain in correctness** (Dapper: hand-written row mapping is repetitive, error-prone code). Saving a few lines isn't enough on its own.

It's a principle, loosely held: it guides the decision, it doesn't rule out good libraries. Each new dependency is justified in the pull request that adds it and listed here.

| Package | Where | Why it's here |
| --- | --- | --- |
| `Dapper` | API, Db runner | Maps query results to C# objects. Hand-written row mapping (column names as strings, casts, null checks) is repetitive code that breaks quietly. Dapper does only that; the SQL stays ours. |
| `Microsoft.Data.SqlClient` | API, Db runner | The SQL Server driver. .NET doesn't ship one; this is Microsoft's. |
| `System.Security.Cryptography.Pkcs` | Security pin | Not used directly. SqlClient 7.1.1 pulls in 9.0.18, which has two denial-of-service advisories, so `Directory.Packages.props` pins it to the patched 10.0.13. Remove the pin once SqlClient depends on a patched version itself. |
| `react`, `react-dom` | Front end | The UI library the front end is built on. |
| `vite` | Front end, dev | Dev server (including the `/api` proxy) and production bundler. |
| `@vitejs/plugin-react` | Front end, dev | Lets Vite compile JSX and reload components in place while you edit. |
| `typescript` | Front end, dev | Type checking; `npm run build` fails on type errors. Pinned to 6.0.x until `typescript-eslint` supports TypeScript 7. |
| `@types/react`, `@types/react-dom` | Front end, dev | Type definitions for React. |
| `@types/node` | Front end, dev | Types for `vite.config.ts`, which runs in Node, not the browser. |
| `eslint`, `@eslint/js` | Front end, dev | Linting, with ESLint's recommended JavaScript rules. |
| `typescript-eslint` | Front end, dev | Lets ESLint read TypeScript and adds TypeScript-aware rules. |
| `eslint-plugin-react-hooks` | Front end, dev | Catches broken hook usage and missing effect dependencies, which are real bugs, not style. |
| `eslint-plugin-react-refresh` | Front end, dev | Warns when a file's exports would stop Vite's in-place reload from working. |
| `globals` | Front end, dev | Tells ESLint which browser globals exist (`window`, `document`), so they aren't flagged as undefined. |
| `prettier` | Front end, dev | Formatting, so style never comes up in review. Pinned to an exact version, because formatting can change between releases. |
| `eslint-config-prettier` | Front end, dev | Turns off ESLint rules that would fight Prettier's formatting. |

"Dev" packages are build and editor tooling; they aren't shipped to the browser.

## What it will include

The goal is the 80% every dashboard app needs on day one:

- [x] Solution layout: front end, API, and database projects in one repo
- [x] Local dev environment with Docker Compose and seeded data
- [ ] App shell: sidebar navigation, top bar with tenant switcher, responsive layout, light and dark themes
- [ ] Authentication: sign in, sign out, password reset
- [ ] Tenants: one account across tenants, tenant-scoped data, Row-Level Security
- [ ] Users, roles, and permissions, enforced on both API and UI
- [ ] A reusable data grid with server-side paging, sorting, and filtering
- [ ] CRUD example module showing the end-to-end pattern, including edit conflicts
- [ ] Forms with shared validation rules
- [ ] Settings and user profile pages
- [ ] Audit log of who changed what
- [ ] Dashboard home with sample KPI cards and charts
- [ ] Error handling, logging, and health checks
- [ ] Automated tests for API and UI
- [ ] CI pipeline and a documented deployment path

## How we work

This repo uses [GitHub flow](https://docs.github.com/en/get-started/using-github/github-flow):

1. Branch off `main` for every change, including docs. Name branches by area or step, such as `step-2/solution-layout` or `docs/readme-fix`.
2. Open a pull request. The description explains what changed and why, since PRs double as the record behind each blog post.
3. Once CI exists, its checks must pass before merging.
4. Squash merge, so `main` gets one clean commit per PR, then delete the branch.

`main` is protected: no direct pushes, no force pushes.

> **Why the change?** The first few commits went straight to `main`. With one person on the project, branches and PRs aren't strictly necessary. They're still a good habit: a protected `main` keeps you from pushing a half-finished change or a broken build where everything else depends on it, and a PR gives you one last look at the diff before it lands. And if you work on a team, which most of us will, you won't be committing straight to `main`, or at least you shouldn't be. This is also a starter other people will fork, so it should show the workflow worth copying. We noticed early and switched before any real code landed. PR #1 was the first one under the new rules.

## Building in public

Each milestone above gets its own post covering what was built, the tradeoffs, and what I'd do differently.

**Every post gets a release.** When a post's pull requests are merged, `main` is tagged `v0.N` to match the post number, with a GitHub Release that links to the post and lists its PRs. Check out the tag to see exactly the code the post describes.

| # | Post | Release | Status |
| --- | --- | --- | --- |
| 1 | [Why build another dashboard starter?](https://mvmorten.com/projects/dashboard-starter/) | v0.1 | Published |
| 2 | Repo structure and local dev setup | v0.2 | In progress |
| 3 | The app shell | v0.3 | Planned |
| ... | More to come | | |

## Getting started

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) (current LTS), [Node.js](https://nodejs.org/) (current LTS; at least 20.19 or 22.12), and [Docker Desktop](https://www.docker.com/products/docker-desktop/).

From the repo root:

```
cp .env.example .env                       # local dev settings (gitignored)
docker compose up -d --wait                # start SQL Server and wait until it's ready
dotnet run --project src/Db -- migrate     # create the database and schema
dotnet run --project src/Db -- seed        # add demo data (Development only)
dotnet run --project src/Api               # API on http://localhost:5080
```

Then, in a second terminal:

```
cd src/Web
npm install
npm run dev                                # front end on http://localhost:5173
```

Open <http://localhost:5173>. You should see three green checks: the front end, the API, and the database, with the schema version the database is on.

What just happened:

- **Docker** runs SQL Server 2025 only, on port 1433, with its data in a named volume (`sqlserver-data`) so it survives restarts.
- **`migrate`** creates the `DashboardStarter` database if it's missing, runs any new scripts in `sql/migrations`, then reruns everything in `sql/programmable`. It prints `applied`, `skipped`, or `failed` for each file. Run it as often as you like; the second run skips everything.
- **`seed`** adds two demo tenants (Acme Corporation and Globex Industries) and one demo user, `demo@example.com`, who belongs to both. It refuses to run outside Development.
- **The API** checks at startup that no migrations are pending. If one is, it refuses to start and tells you to run `migrate`. It never changes the schema itself.
- **The front end** runs on the Vite dev server, which forwards `/api` to the API, so the browser only ever talks to one origin.

**Changed the password in `.env`?** The API and the Db runner use the password from `.env.example` by default. If you change it, set your connection string as an environment variable in the terminal you run them from.

Bash or zsh (macOS, Linux, Git Bash):

```bash
export ConnectionStrings__Default='Server=127.0.0.1,1433;Database=DashboardStarter;User Id=sa;Password=<your password>;Encrypt=True;TrustServerCertificate=True'
```

PowerShell (Windows):

```powershell
$env:ConnectionStrings__Default = 'Server=127.0.0.1,1433;Database=DashboardStarter;User Id=sa;Password=<your password>;Encrypt=True;TrustServerCertificate=True'
```

Use single quotes, as shown, so characters like `!` and `$` in the password are taken literally. Either way, the variable lasts until you close that terminal.

For the API you can use a user secret instead: `dotnet user-secrets set "ConnectionStrings:Default" "<connection string>" --project src/Api`.

**Already have a SQL Server?** Set `ConnectionStrings__Default` to point at it and skip Docker. That works, but Docker is the supported path. If something else already uses port 1433, change the port on the left of `"1433:1433"` in `docker-compose.yml` and in your connection string.

**On an Apple Silicon Mac?** SQL Server's container image is built for Intel, so turn on "Use Rosetta for x86_64/amd64 emulation" in Docker Desktop's settings before starting it. It runs a little slower than native, which doesn't matter for development.

**Docker Desktop licensing:** free for personal use, education, and small businesses. Larger companies need a paid subscription, so check before installing it on a work machine.

**Start over with an empty database:** `docker compose down -v` deletes the container and its volume. Then run the steps again from `docker compose up`.

### Repo layout

```
/sql                 schema scripts (see "Schema scripts" above)
/src/Db              the migration runner: `migrate` and `seed`
/src/Api             ASP.NET Core Web API (controllers)
/src/Web             React + TypeScript front end (Vite)
docker-compose.yml   SQL Server for local development
```

### Checks

The same checks run in CI on every pull request:

```
dotnet build                 # warnings are errors, including code style from .editorconfig
cd src/Web
npm run lint                 # ESLint
npm run format:check         # Prettier (npm run format to fix)
npm run build                # type-check and build
```

## FAQ

**Why not Entity Framework Core?**
We want every query to be SQL you can read, tune, and run yourself. Dapper maps results to objects and otherwise stays out of the way: no generated SQL, no change tracking to reason about. Schema changes are plain SQL scripts instead of migrations generated from C# models.

**Why not stored procedures?**
This project is for developers who want to understand the whole stack, not a data layer owned by a DBA. Procedures have their place, but putting every query in one splits each feature across two places and two deployments, which is a maintenance headache in a codebase meant to be easy to follow. Two common objections: parameterized queries get their execution plans cached and reused just like procedures do, and it's parameters, not procedures, that prevent SQL injection. If your team has a DBA who owns the database and reviews every query, procedures can be the right call there.

**Why one shared database instead of one per tenant?**
A database per tenant sounds safer, but it multiplies the work: every migration runs N times, schemas drift, a deploy can half-succeed, and reporting across tenants means querying every database. Before accepting that requirement, ask what it's really protecting against. Data leaking between tenants is handled by Row-Level Security. The honest weak spot of a shared database is restoring a single tenant from backup, which takes work. Legal data-residency rules are a real reason to split, but by region, not by tenant.

**Why `int` keys instead of GUIDs?**
They're 4 bytes instead of 16 in every index and foreign key, don't fragment the clustered index, and are easy to read when debugging. "Guessable IDs" are only a problem without authorization: every request here is permission-checked and tenant-scoped, so guessing the next ID gets you a not-found. Gaps in the numbers after a restart are normal; IDs aren't row counts.

**Why controllers instead of minimal APIs?**
Controllers are time-tested and come with a structure everyone recognizes: one class per resource, with routes and authorization visible on the class and its methods. Minimal APIs are leaner, but in a large app every team ends up inventing its own way to organize them. Minimal APIs do support native AOT, which controllers don't, but that matters for serverless and tiny containers, not a dashboard app.

**Why not ASP.NET Core Identity?**
It does a lot you can't easily see, which makes it hard to trace why it behaves the way it does, and its built-in UI is Razor Pages, which a React app doesn't use. We use ASP.NET Core's cookie authentication and authorization directly, with our own user tables. Lockout, password reset tokens, and signing out other sessions after a password change are each a small amount of code you can read.

**Why not JWT?**
Cookies, because the only client is a browser on the same site. An `HttpOnly` cookie can't be read by JavaScript, so a script injection bug can't steal the session, and a session can be revoked immediately. CSRF is handled with `SameSite` cookies and antiforgery tokens. If you add a mobile app or a public API, add token auth for those clients alongside the cookies.

**Why Argon2id for passwords?**
It's OWASP's first recommendation. It's memory-hard, so each guess costs memory as well as CPU, which takes away most of an attacker's GPU advantage. Hashes are stored with their settings, so the settings can be raised later and users are rehashed automatically when they sign in.

**Why doesn't the API run migrations on startup?**
A database that changes because you started the app is hidden behavior. You run the migrate command on purpose, and you see exactly which scripts it applied.

**Why Docker for the database?**
It's the one setup that works the same on Windows, Mac, and Linux, with the same SQL Server version as CI. Docker only runs the database, so you debug the API and front end normally.

## Contributing

Issues and suggestions are welcome. Since the project is opinionated, pull requests that change core stack choices may not be merged, but the discussion is always useful.

## License

[MIT](LICENSE). Use it for anything, including commercial projects.
