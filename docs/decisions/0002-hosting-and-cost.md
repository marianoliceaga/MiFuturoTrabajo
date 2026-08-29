# ADR 0002 — Hosting on free tiers, and where that hurts

**Status:** Accepted · **Date:** 2026-08-26

## Context

Two constraints, stated up front and in tension:

- It must be a **portfolio piece**: a public URL a recruiter clicks and forms an opinion about in ninety seconds.
- It must stay at **near-zero cost**.

The first wants an always-warm app on a custom domain. The second wants free tiers, which are free precisely because they are not always warm.

## Decision

| Component | Service | Tier | Cost |
|---|---|---|---|
| Client | Static Web Apps | Free | $0 |
| API | App Service Linux | **F1 Free** | $0 |
| Reminders | Functions | Consumption | $0 |
| Database | Cosmos DB | Serverless | cents |
| CV files | Blob Storage | Standard LRS Hot | cents |
| Email | Communication Services | Pay-as-you-go | ~$0 |
| Telemetry | Log Analytics + App Insights | 5 GB/month free | $0 |

Plus a **$5/month budget alert**. Everything is free-tier or consumption, so any real spend means something changed. It should never fire.

Secrets live in **App Service application settings**, not Key Vault, and **no secret is a Bicep parameter** — parameters are readable by anyone with resource-group access, forever.

## Where this hurts, honestly

**F1 has no Always On.** The site unloads after ~20 minutes idle, so a recruiter's first request takes several seconds. It also has a 60 CPU-minute daily quota, after which the app returns `403` until midnight UTC.

`appServiceSku = 'B1'` removes both for about **USD 13/month**, and is a one-parameter change. The recommendation is to launch on F1 and upgrade if a cold start ever costs an impression — the cost of being wrong that way is one slider, and the cost of guessing wrong in the other direction is $156/year for a demo nobody visits.

**Key Vault would be the textbook answer.** It adds a resource, a managed-identity round trip at startup, and cost. Application settings are encrypted at rest, absent from source control, and absent from deployment history. For two secrets — a JWT signing key and a deliberately-public demo password — that is the right amount of machinery. A reviewer is better served by a reasoned trade-off than by an unexamined default.

**Cosmos runs serverless, not free tier.** Not a preference. The subscription's one free-tier slot was held by a spike account created earlier in this work; deleting it did **not** release the slot, and Azure still reported it as claimed thirty-five minutes later with no live and no soft-deleted free-tier account anywhere in the subscription. Rather than block, billing became a three-way parameter and the deployment uses serverless: cents per month at this volume, and no subscription-wide limit to collide with.

Billing mode **cannot be changed in place**. Moving to free tier later means creating a new account and migrating.

## Constraints discovered by deploying

None of these were visible from the template. They are recorded because they cost real time.

**A Linux Consumption plan cannot share a resource group with a Linux Free/Basic plan.** Azure refuses with *"Requested features 'Dynamic SKU, Linux Worker' not available in resource group"*. The error text suggests using a different resource group — that does not help, because it is the combination, not the group's history. The Function App deploys into a group of its own.

**Communication Services names are reserved globally**, and a reservation outlives the failed deployment that created it, so a retry collides with itself. Both ACS names carry a `uniqueString` suffix.

**`az functionapp deployment source config-zip` reported success while producing a 503 host** logging *"0 functions found... host.json is missing from the most recent deployment"*. The package was fine — `host.json`, `functions.metadata` and `worker.config.json` were all at the zip root, checked rather than assumed. The mechanism is unreliable for Linux Consumption with the isolated worker. `func azure functionapp publish` deployed the identical output and both triggers registered immediately. The CI workflow uses `Azure/functions-action`, which wraps the working path.

## Consequences

- The demo is genuinely free to run, and the one paid upgrade is a single parameter.
- The first request after idle is slow. That is the price, and it is stated in the README rather than hidden.
- Containers are created by the application at startup, not by the template, so partition keys are defined once — in the EF model — rather than duplicated somewhere they could drift.
