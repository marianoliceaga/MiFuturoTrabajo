# Phase 8 — Documentation that matches reality

**Date:** 28 August 2026
**Branch:** `claude/phase-8-docs` (main checkout)

---

## The problem, restated

The original brief named this as the central risk: *"the README describes a finished product that does not exist yet."* Eight phases later the product mostly does exist — but the README had drifted further, not less, because seven phases of change had landed underneath it.

Nearly every factual claim in it was wrong.

| Claimed | Actually |
|---|---|
| 12 entities, 12 containers | 7 containers, five sets embedded |
| `Users` partitioned by `id` | by `/Email` |
| `PUT /api/jobapplications/{id}` | `PATCH /api/JobApplication/{id}` |
| API on `https://localhost:5011` | `7071` — 5011 is the *client* |
| "CV upload accepts URL strings only" | real Blob Storage, expiring links |
| "Email verification implemented as skeleton" | real, via ACS |
| "Reminder services disabled (commented out)" | Azure Functions timer triggers |
| "50+ unit, 30+ integration" with per-file counts | **196**, counted |

The route table was wrong for **every single controller** — it described a plural REST API that does not exist.

---

## Verified, not restated

The plan said to verify test counts against real output rather than restating old numbers. That principle got applied to everything.

Every table in the README was generated from the code: routes by parsing `[Http*]` attributes, containers and partition keys from `OnModelCreating`, ports from `launchSettings.json`, quotas and rate limits from their options classes, test counts from the `.trx`.

Then a script checks the README against the code:

```
  advertised routes: 24
  not found in any controller: none
  28/28 claims verified
```

That covers all seven containers, all seven partition keys, all six quotas, both rate limits, the API port, and that the two deleted join entities appear neither in the README nor on disk.

**A clean clone was built and tested from scratch** — 196 passing — because `docs/local-setup.md` claims a fresh clone works, and that is not a claim to make untested.

It also surfaced a real gotcha worth documenting: `git clone` fails with `Filename too long` on Windows when the target path is deeply nested, because of the Bootstrap source-map filenames in `wwwroot/lib`.

---

## OAUTH_SETUP.md was causing the bug

Phase 6 found that Microsoft sign-in fails because the Entra redirect URIs are registered as `https://localhost:5001/authentication/login-callback`.

This file is where that came from. It instructed exactly that — the wrong port (5001 is the client's HTTP fallback; HTTPS is 5011) and the wrong path (`/authentication/login-callback` is the Blazor MSAL *template's* path; this app uses `/login-callback`).

So the misconfiguration was not carelessness. The documentation told you to do it.

Rewritten with the correct URIs, the SPA-versus-Web distinction and the exact `AADSTS9002326` error it produces, and the nOAuth reasoning behind the account-linking rules. It opens with a note telling anyone who read the old version to re-read that section.

`MiFuturoTrabajo.Api/README.md` also claimed the timer functions were "commented out in Program.cs" and linked to a `CLAUDE.md` that does not exist.

---

## Known Limitations, made honest

The old section listed three limitations, all of which had since been fixed. The new one lists five that are true:

- Email does not send — the ACS DNS records are not at the registrar, so every check reads `NotStarted`.
- Microsoft sign-in fails in the browser — redirect URIs on the wrong platform.
- Google sign-in untested against the deployed origin.
- Cosmos is serverless, not free tier.
- CI/CD is written but not connected; this deployment was manual.

A README that hides these is worse than one that admits them — and a reviewer who finds an admitted limitation thinks better of the author than one who finds a hidden one.

---

## Added

**`docs/architecture.md`** — containers and code diagrams, then the decisions worth defending: embedded aggregates, email as a partition key, reminders as Functions, custom JWT, blob paths rather than URLs, app settings rather than Key Vault. Plus a security posture table and the two things deliberately not solved.

**Three ADRs**, each with the options that were rejected and why:

- [0001](decisions/0001-cosmos-embedded-aggregates.md) — Cosmos with embedded aggregates. Records that PostgreSQL was the pragmatic alternative and was held in reserve, and that `WithPartitionKey` was dropped on a measurement rather than a hunch.
- [0002](decisions/0002-hosting-and-cost.md) — free tiers, and exactly where that hurts. F1's cold start, Key Vault's absence, and the three Azure constraints that only surfaced when deploying.
- [0003](decisions/0003-reminders-as-functions.md) — reminders as Functions. Why a `BackgroundService` on F1 fails invisibly, and why at-most-once is the deliberate choice.

**`docs/local-setup.md`** — reproducible from a clean clone, including that verification links go to the API log rather than an inbox, and troubleshooting for the failures actually hit.

---

## Carried forward

Nothing in Phase 8. The three things still outstanding belong to earlier phases and are now written down in the README where they cannot be forgotten: the DNS records, the Entra redirect URIs, and connecting CI.
