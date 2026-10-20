# dashboard-starter

An opinionated, open-source dashboard starter for building real business apps with **React**, **C# / ASP.NET Core**, and **SQL Server**.

Clone it, rename it, and start on your actual product instead of rebuilding auth, layout, and plumbing for the tenth time.

> **Status:** Just getting started. This project is being built in public, and every step is documented in a blog series. Expect the code to change quickly while the foundation takes shape.

---

## Why this exists

Most starter templates fall into one of two camps: a pretty UI with no real backend, or a backend sample with no usable front end. Very few show how a full production app fits together end to end, and fewer still explain *why* each decision was made.

This repo aims to be:

- **Usable.** A working dashboard you can fork and ship on.
- **Opinionated.** One clear way to do things, not a menu of every option.
- **Explained.** Every major decision is written up in the accompanying blog posts, so you can disagree with it knowingly.

## The stack

| Layer | Choice |
| --- | --- |
| Front end | React + TypeScript, built with Vite |
| API | ASP.NET Core Web API (C#, current .NET LTS) |
| Database | SQL Server |
| Data access | EF Core, with migrations checked into source |
| Auth | Cookie / token based auth with users, roles, and permissions |
| Local dev | Docker Compose (SQL Server in a container), one command to run |
| CI | GitHub Actions for build, test, and lint |

## What it will include

The goal is the 80% every dashboard app needs on day one:

- [ ] Solution layout: front end, API, and database projects in one repo
- [ ] Local dev environment with Docker Compose and seeded data
- [ ] App shell: sidebar navigation, top bar, responsive layout, light and dark themes
- [ ] Authentication: sign in, sign out, password reset
- [ ] Users, roles, and permissions, enforced on both API and UI
- [ ] A reusable data grid with server-side paging, sorting, and filtering
- [ ] CRUD example module showing the end-to-end pattern
- [ ] Forms with shared validation rules
- [ ] Settings and user profile pages
- [ ] Audit log of who changed what
- [ ] Dashboard home with sample KPI cards and charts
- [ ] Error handling, logging, and health checks
- [ ] Automated tests for API and UI
- [ ] CI pipeline and a documented deployment path

## Building in public

Each milestone above gets its own post covering what was built, the tradeoffs, and what I'd do differently. Links will be added here as posts go live.

| # | Post | Status |
| --- | --- | --- |
| 1 | Why build another dashboard starter? | Planned |
| 2 | Repo structure and local dev setup | Planned |
| 3 | The app shell | Planned |
| ... | More to come | |

## Getting started

Setup instructions will land here once the first runnable version is in place.

## Contributing

Issues and suggestions are welcome. Since the project is opinionated, pull requests that change core stack choices may not be merged, but the discussion is always useful.

## License

[MIT](LICENSE). Use it for anything, including commercial projects.
