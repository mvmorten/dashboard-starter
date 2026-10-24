# Security policy

## Reporting a vulnerability

Please report security problems privately, not in a public issue.

Use GitHub's private vulnerability reporting: go to the **Security** tab of this repository and choose **Report a vulnerability**. The report is visible only to the maintainer, and we can discuss and fix it in a private advisory before anything is made public.

Helpful things to include:

- What the problem is and where it is (file, endpoint, or page).
- Steps to reproduce it, or a small proof of concept.
- What an attacker could do with it.

## What to expect

This is a one-person project built in public, so there's no formal SLA. I aim to acknowledge a report within a week, and to fix confirmed problems before the next release. If you'd like credit, say how you'd like to be named and you'll be listed in the advisory.

## Scope

In scope: the code in this repository (the API, the migration runner, the front end, the SQL scripts, the workflows, and the Bicep file) and the live demo at https://dashboard.mvmorten.com.

Out of scope:

- Vulnerabilities in dependencies that are already publicly known. Dependabot tracks those; a report is still welcome if this project uses the dependency in a way that makes it worse.
- Denial of service against the demo by sending a lot of traffic, and automated scanning that floods it. The demo runs on a small plan.
- Missing hardening that's planned for a later step and noted in the README (for example, authentication arrives in step 5).

## Supported versions

Only `main` (and the live demo, which always runs `main`) gets security fixes. The `v0.N` tags match the blog posts and are not patched after release.

## If you forked this starter

Your fork is your app. Fixes made here won't reach it automatically, so watch this repository's security advisories and apply what's relevant.
