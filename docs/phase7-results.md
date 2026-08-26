# Phase 7 — Azure deployment, CI/CD, and demo hardening

**Date:** 26 August 2026
**Branch:** `claude/phase-7-deploy` (main checkout)

---

## Outcome

| | Before | After |
|---|---|---|
| Rate limiting | none | per-IP on auth, per-user elsewhere |
| Per-user quotas | none | six caps, `429` with a useful message |
| CORS origins | localhost hardcoded | configuration |
| `AllowAnyOrigin` policy in Production | **registered** | not registered at all |
| Cosmos containers created in Production | **never** | at startup |
| Infrastructure | none | Bicep, one deployment |
| CI/CD | none | build+test on every push, deploy from `main` |
| Demo data | none | a three-month job hunt |
| Unit tests | 104 | 104 |
| Integration tests | 90 | **92** |

---

## Two deployment blockers found while wiring it up

**Nothing would have created the Cosmos containers.** `EnsureCreatedAsync` ran under `if (app.Environment.IsDevelopment())`. Bicep creates the *database*; the containers and their partition keys come from the EF model. On a fresh account in Production, every query would have thrown on the first request. It now runs unconditionally, is idempotent, and logs rather than crash-loops if it fails.

**CORS could not have worked, and could have been dangerously wrong.** The allowlist hardcoded two localhost origins and a `BlazorClient:Url` that defaulted to the API's own port. Worse, the `AllowAnyOrigin` policy was *registered* in every environment, one `if` away from being selected. Origins now come from configuration, and that policy is not registered at all in Production — a misconfigured environment name cannot reach for what does not exist. Same reasoning as removing the Development token leak in Phase 4.

---

## Rate limiting

Two policies, because the two risks differ.

| Policy | Applies to | Partition | Default |
|---|---|---|---|
| `auth` | `/api/auth/*` | client address | 10/min |
| `per-user` | everything else | user id | 120/min |

Auth is anonymous and is where credential stuffing, mass registration and mail-bombing happen, so it is partitioned by address — reading `X-Forwarded-For` first, because behind App Service every request arrives from the front end and `RemoteIpAddress` would put the entire internet in one bucket. Everything else is behind a token, so it is partitioned per user: one noisy account cannot degrade the demo for anyone else.

`QueueLimit = 0` throughout. A rejected login should be told so immediately; queuing would let an attacker hold server connections for free. Every `429` carries `Retry-After`, because without it a client has no way to back off and a bad retry loop turns a limit into an outage.

---

## Quotas

Rate limiting caps *how fast*. Quotas cap *how much*, which is the one that matters on a shared free-tier database: registration is open, and without a ceiling one script can fill 25 GB and exhaust the 1000 RU/s everybody shares — including the reviewer the demo exists for.

| Applications | Companies | Recruiters | Meetings | Tasks | CV versions |
|---|---|---|---|---|---|
| 200 | 100 | 100 | 300 | 300 | 10 |

Generous on purpose: a real job hunt is dozens of applications, not hundreds, so nobody using the app for its purpose meets one. Exceeding a cap is a `429` naming the limit, not a `500` once the store eventually complains. The CV check runs *before* the bytes are stored — uploading 5 MB to be told there is no room would be rude and would cost the bandwidth anyway.

---

## A mistake worth recording

I hardcoded both sets of limits. That made **the entire integration suite fail**: a `TestServer` has no client address, so every test lands in the same partition and 90 tests blow through 10 requests a minute.

The fix was not to special-case the tests. Limits differ legitimately between environments, so they became configuration — which they should have been from the start. The suite now runs with the ceilings raised out of the way, and two fixtures set deliberately tiny ones so the limiter and the caps are **proven rather than assumed**:

- `RateLimiterTests` — hammer login, get a `429`, and confirm the first few requests were served. A limiter that rejects everything is not a limiter, it is an outage. (Acceptance criterion 10.)
- `QuotaEnforcementTests` — exceed a cap, get a clean `429` naming the limit, and confirm another account is unaffected. (Acceptance criterion 11.)

The first attempt at those fixtures used `ConfigureAppConfiguration`, which is layered *underneath* `appsettings.Testing.json` and lost to it. `UseSetting` wins.

---

## The demo account

The single most-seen code path in the project. A reviewer clicks the link, logs in, and forms an opinion in about ninety seconds — and an empty board says the app does not work even when it does.

So the seed is a plausible three-month job hunt: eleven applications spread across **every** board column, eight companies, three recruiters, past and upcoming meetings, open and completed tasks, two CV versions and a past-employment reference. Everything is dated relative to now, so the demo never looks abandoned — there are always interviews next week and tasks due tomorrow.

It is idempotent, runs at startup on every instance, and does nothing at all unless `Demo:Enabled` is true and a password is set.

---

## Infrastructure

`infra/main.bicep`, one deployment.

| Component | Tier | Cost |
|---|---|---|
| Cosmos DB | Free — 1000 RU/s, 25 GB, database-level shared throughput | $0 |
| App Service (Linux) | F1 Free | $0 |
| Functions | Consumption | $0 |
| Static Web Apps | Free — custom domain and TLS included | $0 |
| Blob Storage | Standard LRS Hot | cents/month |
| Communication Services | Pay-as-you-go | ~$0 |
| Log Analytics + App Insights | 5 GB/month free | $0 |

Plus a **$5/month budget alert**. Everything is free-tier or consumption, so any real spend means something changed — it should never fire.

Two decisions the template makes deliberately:

- **No secret is a parameter.** Parameters land in deployment history, readable by anyone with resource-group access. The JWT key, demo password and OAuth client IDs are set separately with `az webapp config appsettings set`.
- **Containers are not declared here.** They come from `EnsureCreatedAsync`, so partition keys are defined once — in the EF model — rather than duplicated in a template where they could drift.

The API gets a **system-assigned identity** with *Storage Blob Data Contributor*, which activates the managed-identity path written in Phase 3 and means no storage key appears in configuration at all.

**The free-tier Cosmos slot was already taken** by the spike account from Phase 0. One per subscription, so it was deleted (its only contents were my own verification data) before deploying.

---

## CI/CD

`build-test.yml` runs on every push and pull request: restore, build with **`-warnaserror`**, both test suites, and a Bicep compile. Warnings-as-errors is deliberate — Phase 1 cleared 116 warnings, and without a gate they come back one pull request at a time. Compiling the template in CI costs seconds; discovering a malformed one mid-deployment costs a half-created resource group.

`deploy.yml` runs only from `main`, and **calls the same gate first** rather than trusting the push event. It authenticates with **OIDC federated credentials**, so there is no publish profile or long-lived service-principal secret in the repository to leak. It writes the client's `appsettings.json` from repository variables at build time, so one source tree deploys to any environment. It ends with a **smoke test against `/health`**, with retries because F1 has to wake up — a deploy that reports success while the site returns 500 is worse than a failed one, because nobody looks.

---

## Two Azure constraints the first deployment hit

Neither was a template bug; both are subscription- and resource-group-level facts that only surface when you actually deploy.

**`Dynamic SKU, Linux Worker not available in resource group rg-mifuturotrabajo`.** A resource group is pinned to a scale unit the first time it hosts a web workload, and that unit may not offer Linux Consumption. Azure’s own remedy is in the error text: use a different resource group. The deployment moved to a clean `rg-mifuturotrabajo-prod`.

**`NameReservationTaken`** on Communication Services. Those names are reserved *globally*, and the reservation outlives the failed deployment that made it — so the retry collided with its own first attempt. Both ACS names now carry the resource group's unique suffix.

**`Free tier has already been applied to another Azure Cosmos DB account`** — raised *after* the spike account was deleted, with no live and no soft-deleted free-tier account anywhere in the subscription. The subscription-level claim lags the deletion and can take hours to clear; it was still held 35 minutes later.

Rather than block on it, Cosmos billing became a three-way parameter (`free` / `serverless` / `provisioned`) and this deployment uses **serverless**: cents per month at demo volume, no subscription-wide limit to collide with, and it keeps the near-zero-cost constraint. The parameter file records that billing mode **cannot be changed in place** — moving to free tier later means creating a new account and moving the data.

The first attempt at the Consumption-plan fix was wrong, and worth recording: I moved the whole deployment to a fresh resource group, because Azure'''s error text suggests exactly that. It failed identically. The constraint is the *combination* of a Linux Consumption plan and a Linux Free/Basic plan in one group, not anything about the group'''s history.

Worth stating plainly, because it is the phase’s real lesson: five phases of local verification could not have surfaced either of these. Some things are only true in the place you deploy to.

---

## It is live

| | |
|---|---|
| **Client** | https://ambitious-coast-084594d03.7.azurestaticapps.net |
| **API** | https://mifuturotrabajo-api.azurewebsites.net |
| **Demo login** | `demo@mifuturotrabajo.app` / `DemoAccount2026!` |

Verified against the deployed site, not locally:

```
GET  /health                     200   {"status":"ok"}
POST /api/auth/login  (demo)     200   token issued
GET  /api/jobapplication/board   200   8 columns populated
GET  /api/companies              200   8
GET  /api/Recruiters             200   3
GET  /api/meeting                200   3
GET  /api/applicanttask          200   5
GET  /api/cvversion              200   2
GET  /api/applicant/current      200
```

The demo account seeded itself on first start, against real Cosmos, and every list endpoint returns real data. The client is serving its production `appsettings.json` with the right API base URL.

**The rate limiter, on the live site** — ten requests served, then refused, exactly as configured:

```
401 401 401 401 401 401 401 401 401 401 429 429 429 429
```

**CORS**: a request from `https://evil.example.com` comes back with **no** `Access-Control-Allow-Origin` header. The permissive policy genuinely does not exist in Production.

**Both timer triggers are registered and the host reports no errors:**

```
InterviewReminders  timerTrigger  %InterviewReminderSchedule%
TaskReminders       timerTrigger  %TaskReminderSchedule%
host status: Running | errors: none
```

---

## One more thing only deploying could find

`az functionapp deployment source config-zip` reported success and produced a host that returned **503** with *"0 functions found... host.json is missing from the most recent deployment"*.

The package was fine — `host.json`, `functions.metadata` and `worker.config.json` were all present at the zip root, which I checked rather than assumed. The mechanism is simply unreliable for Linux Consumption with the isolated worker. `func azure functionapp publish` deployed the identical output and both triggers registered immediately.

Worth noting because the deploy workflow uses `Azure/functions-action`, which wraps the working path.

---

## Also in this phase

- A `/health` endpoint that touches nothing, used by the smoke test and by App Service's health check.
- The rate limiter sits **after** authentication in the pipeline, so the per-user policy can actually see the user.

---

## What is left, and who has to do it

**DNS for email.** The ACS domain `ideapixel.com.ar` is created but every check reads `NotStarted` — nothing sends until these exist at your registrar:

| Type | Name | Value |
|---|---|---|
| TXT | `ideapixel.com.ar` | `ms-domain-verification=e3fec192-0f4a-4122-8f0f-51407cdee390` |
| TXT | `ideapixel.com.ar` | `v=spf1 include:spf.protection.outlook.com -all` |
| CNAME | `selector1-azurecomm-prod-net._domainkey` | `selector1-azurecomm-prod-net._domainkey.azurecomm.net` |
| CNAME | `selector2-azurecomm-prod-net._domainkey` | `selector2-azurecomm-prod-net._domainkey.azurecomm.net` |

Registration currently succeeds and simply logs that it could not send. That is the Phase 4 non-fatal path working as designed, but it means **the real-inbox test still has not happened** — for verification mail, password reset, or reminders.

**The Entra redirect URIs**, blocked since Phase 6 and now unblocked because the hostname exists:

```bash
az ad app update --id 017e3aea-92a7-4724-94ed-fae35fba5131 --set spa.redirectUris="['http://localhost:5011/login-callback','https://ambitious-coast-084594d03.7.azurestaticapps.net/login-callback']"
```

They must be **SPA**, not **Web** — MSAL.js v2 redeems cross-origin with PKCE, and a Web URI fails with `AADSTS9002326`. I have not run it: changing an app registration is a change to your account configuration.

Google's console needs the same origin added as an authorised JavaScript origin. I cannot reach it at all.

**CI/CD is committed but not connected.** The workflows need the repository variables and secrets listed in `infra/README.md`, plus a federated credential for OIDC. Until then deployment is manual, as it was here.

**Cosmos is serverless, not free tier.** Revisit when the free-tier slot releases; changing it means a new account and moving the data.
