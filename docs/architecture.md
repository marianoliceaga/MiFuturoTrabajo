# Architecture

What the system is, and why it is shaped this way. Decisions with a real alternative are recorded as ADRs in [`decisions/`](decisions/); this page is the map.

---

## Context

One person tracking their own job hunt. Everything is scoped to the signed-in user; there is no sharing, no team, no admin. That single fact drives most of what follows — in particular it is why every read can be a single-partition query, and why "tenant isolation" is just a `WHERE` clause on a partition key rather than a subsystem.

---

## Containers

```
                    ┌──────────────────────────────┐
                    │  Blazor WebAssembly          │
                    │  Azure Static Web Apps, Free │
                    └──────────────┬───────────────┘
                                   │ HTTPS + JWT bearer
                                   ▼
                    ┌──────────────────────────────┐
                    │  ASP.NET Core Web API        │
                    │  App Service Linux F1        │
                    └───┬───────────┬───────────┬──┘
                        │           │           │
       managed identity │           │           │ connection string
                        ▼           ▼           ▼
              ┌──────────────┐ ┌─────────┐ ┌──────────────────┐
              │ Blob Storage │ │ Cosmos  │ │  Communication   │
              │ CV documents │ │   DB    │ │  Services (mail) │
              └──────────────┘ └────┬────┘ └──────────────────┘
                                    ▲               ▲
                                    │               │
                    ┌───────────────┴───────────────┴──┐
                    │  Azure Functions, Consumption    │
                    │  TaskReminders  · daily 08:00    │
                    │  InterviewReminders · every 15m  │
                    └──────────────────────────────────┘
```

The Functions app is a **separate deployable** reading the same database through the same EF model. It is not a copy of the data-access code — `MiFuturoTrabajo.Infrastructure` is referenced by both hosts.

## Code

```
Client ──> Shared <── Api ──> Infrastructure ──> Shared
                              ▲
              Functions ──────┘
```

`Shared` holds DTOs and enums, so the wire contract is one definition compiled into both sides. `Infrastructure` holds the EF model, entities, email, and reminder logic.

**The API does not reference the client.** It used to, along with a self-targeting `HttpClient` that would have thrown if anything had ever resolved it. Removing that is what makes the dependency graph a graph rather than a cycle.

**Email and reminder logic live in `Infrastructure`, not `Api`.** They started in the web project, which meant the Functions app could not use them without referencing a web project — the same mistake in a new place. Moving them is what let the two hosts share one implementation.

---

## The decisions worth defending

### Cosmos DB with embedded aggregates

The EF Core Cosmos provider **cannot join documents across containers**. The original model had twelve containers and twenty cross-container `.Include()` calls, none of which could ever have executed against real Cosmos — the app had only ever run on the InMemory provider, because startup silently swapped to it whenever the connection string mentioned `localhost:8081`.

The model is now seven containers with aggregates embedded as owned types. The board renders from **one single-partition query** instead of five cross-partition ones.

The cost is denormalisation: a job application carries a snapshot of its company's name, and renaming a company fans out across that user's applications. That fan-out is bounded by a single-partition query and happens on a rare write, which is the right trade for a read that happens on every page load.

→ [ADR 0001](decisions/0001-cosmos-embedded-aggregates.md)

### Email is the partition key for users

Login is the hot path, and it is a lookup by email. Making email the partition key turns it into a single-partition read.

Two consequences that had to be handled rather than assumed:

- **Cosmos does not enforce unique indexes.** The `HasIndex(...).IsUnique()` on email was a false guarantee and was removed. Uniqueness comes from the partition key plus a pre-insert check.
- **Casing is part of a document address.** `User@x.com` and `user@x.com` are different partitions, so they were two accounts for one person — which OAuth actually hit, because it stored the provider's email verbatim. All email handling now goes through one normalizer.

It also means a lookup *by token* is a cross-partition scan of every account. Password-reset and verification links therefore carry the email alongside the token, so redemption is a single-partition read. Measured, not assumed: `.Where(u => u.Email == x)` targets one partition; `.Where(u => u.Email.ToLower() == x)` fans out.

### Reminders as Functions, not a hosted service

App Service Free has **no Always On**: the site unloads when idle and a `Task.Delay` polling loop inside the API silently stops firing — no error, no missed-run signal. On the tier this deploys to, reminders would have looked implemented and sent nothing.

A Consumption timer has its own host and schedule, and the free grant covers these two jobs many times over.

The selection logic takes the current time as a **parameter**, not from the clock, so a timer trigger and a unit test asserting behaviour at an exact instant drive the same code path with no clock abstraction and no sleeping in tests.

Reminders are marked sent **before** dispatch. A timer can fire twice; the trade is deliberate at-most-once, because a duplicate reminder reads to a user as a bug where a missed one reads as nothing at all.

→ [ADR 0003](decisions/0003-reminders-as-functions.md)

### Custom JWT rather than Entra

Inherited, and kept. Entra would be the better answer for a product; for a portfolio piece the custom implementation is the thing being demonstrated, and swapping it would delete the evidence. What it demands in return is that the auth code actually be correct — hence tokens hashed at rest, single-partition redemption, and a test suite that reproduces the attacks the old code permitted.

### Blob paths, never blob URLs

`CVVersion` stores a **path**, not a URL. The container is private, so a stored URL is not something a browser can open — which is exactly why CV download could never have worked before: uploads stored `blobClient.Uri` and the client opened it directly, getting a 403 every time.

A path is meaningless without a credential. So a leaked database row is not a leaked document, and access is revoked by rotating a key rather than by migrating data. Downloads go through an endpoint that mints a 15-minute SAS after confirming the CV belongs to the caller.

In production the API reaches Storage with a **managed identity**, so no account key exists in configuration and links are user-delegation SAS — removing the role assignment revokes every link already handed out. Locally it falls back to a shared key against Azurite, because user-delegation SAS needs Entra.

### App settings rather than Key Vault

Key Vault is the textbook answer. It also adds a resource, a managed-identity round trip on startup, and cost.

App Service application settings are encrypted at rest, are not in source control, and are not in deployment history — which is why **no secret is a Bicep parameter**: parameters are readable by anyone with resource-group access, forever.

For a demo whose secrets are one JWT signing key and one deliberately-public demo password, that is the right amount of machinery. A reviewer is better served by a reasoned trade-off than by an unexamined default.

→ [ADR 0002](decisions/0002-hosting-and-cost.md)

---

## Security posture

| | |
|---|---|
| Passwords | BCrypt |
| Email-borne tokens | 256 bits from a CSPRNG, stored as SHA-256, constant-time compare |
| Token in a response body | Never, in any environment |
| Reset side effects | Clears the refresh token and confirms the address |
| OAuth | Signature verified against the provider's published keys |
| Provider linking | Refused onto an existing account unless the provider verified the address |
| Enumeration | `/forgot-password` and `/verify-email` return byte-identical responses for known and unknown addresses |
| CORS | Allowlist from configuration; the permissive policy is not registered at all in Production |
| Abuse | 10/min per IP on auth, 120/min per user elsewhere, plus per-user storage quotas |

Two things deliberately **not** solved:

- **A timing oracle on `/forgot-password`.** A known address does database and mail work; an unknown one returns immediately. The bodies are identical, the timing is not. Closing it properly means moving the send off the request path onto a queue — real work, not a patch.
- **Email verification is not enforced.** Unverified users can sign in and see a banner. A reviewer testing signup should not hit a wall waiting for mail that, right now, does not arrive at all.

---

## What a reviewer should look at

- `MiFuturoTrabajo.Infrastructure/ApplicationDbContext.cs` — the whole persistence design in one file
- `MiFuturoTrabajo.Api/Services/OAuthService.cs` — the security boundary of social sign-in
- `MiFuturoTrabajo.Infrastructure/Reminders/ReminderService.cs` — host-agnostic, time-as-a-parameter
- `MiFuturoTrabajo.Tests.Integration/Tests/OAuthSecurityTests.cs` — forged tokens, written before the fix
- `infra/main.bicep` — the cost argument, in code
