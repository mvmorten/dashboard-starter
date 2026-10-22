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
- A single default tenant is seeded, so apps that only ever need one tenant can ignore all of this.

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

## What it will include

The goal is the 80% every dashboard app needs on day one:

- [ ] Solution layout: front end, API, and database projects in one repo
- [ ] Local dev environment with Docker Compose and seeded data
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

The first runnable version lands with post 2. The plan:

1. Install the .NET SDK (current LTS), Node.js (current LTS), and Docker Desktop.
2. Start SQL Server: `docker compose up -d`
3. Create the schema: `dotnet run --project src/Db -- migrate`, then `dotnet run --project src/Db -- seed` the first time.
4. Run the API: `dotnet run --project src/Api`
5. Run the front end: `npm install` then `npm run dev` in `src/Web`

**On an Apple Silicon Mac?** SQL Server's container image is built for Intel, so turn on "Use Rosetta for x86_64/amd64 emulation" in Docker Desktop's settings before step 2. It runs a little slower than native, which doesn't matter for development.

**Docker Desktop licensing:** free for personal use, education, and small businesses. Larger companies need a paid subscription, so check before installing it on a work machine.

Already have a SQL Server? Point the connection string at it and skip step 2. That works, but Docker is the supported path.

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
