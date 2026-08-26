# MiFuturoTrabajo — Roadmap to Completion

## Context

MiFuturoTrabajo is a job-application tracker: Blazor WebAssembly client, ASP.NET Core Web API, EF Core over Azure Cosmos DB, custom JWT auth with Google/Microsoft OAuth. The code is substantial (~11,800 lines across 8 controllers, 14 Razor pages, 12 entities) and the app builds and runs locally today.

The problem is that **the README describes a finished product that does not exist yet**. Several advertised features are wired to endpoints that cannot serve them, the app has never actually run against Cosmos DB, and nothing is deployed. Because the goal for this project is a **portfolio showcase** — something a reviewer clicks from your CV and forms an opinion about in 90 seconds — the gap between claim and reality is the central risk. A reviewer who finds a broken CV upload or a README promising email that never sends draws a worse conclusion than one who sees a smaller, honest app.

The intended outcome: every feature the project advertises genuinely works, running on a public Azure URL, with green CI, documentation that matches reality, and an architecture that survives a code review.

### Decisions locked during the interview

| Question | Decision |
| --- | --- |
| Purpose | Portfolio / showcase piece |
| Definition of done | Every listed feature really works |
| Current state | Builds and runs, mostly works |
| Hosting | Azure — App Service + Static Web Apps |
| Feature scope | CV upload, real email, reminder services, OAuth end-to-end — **all four in scope** |
| Portfolio needs | Live demo URL, seeded demo account, CI/CD, architecture docs + honest README |
| Refactors | Fix Api→Client project reference; move secrets out of source control |
| Constraints | Azure subscription available; custom domain owned; **must stay near-zero cost** |
| Reminder runtime | Azure Functions timer trigger (Consumption) |
| Test state | Never run — Phase 0 establishes the baseline |
| Demo access | Open registration + rate limits |
| Persistence | **Redesign for Cosmos properly — embed aggregates** |
| Data hygiene | No reset; per-user quotas + cost alerts |

---

---

## ⚠ Phase 0 complete — this roadmap has been rebaselined

**Full results: [docs/phase0-baseline.md](docs/phase0-baseline.md)** · run 26 Aug 2026

The findings below were written against committed `HEAD` (`89a3a9c`). Phase 0 discovered that the main checkout carries **34 uncommitted changes (+580/−142)** that already implement much of Phases 3–5. Read this block before acting on anything further down.

**Measured:** Release build 0 errors / 116 warnings · unit tests 51/52 · **integration tests 0/31** (all die at `JWT SecretKey is not configured`) · **anonymous account takeover reproduced end-to-end**.

**Already done in your working tree:** token leak removed, email sending live, `VerifyEmail.razor`, `IBlobStorageService`/`BlobStorageService`, both reminder services, and both missing `appsettings.json` files.

**Still open, and new since the plan:**

| # | Finding | Severity |
|---|---|---|
| F1 | `RecruiterService` 404s on every call — client calls `api/recruiter`, controller serves `api/Recruiters`. A listed feature is entirely dead. | High |
| F2 | Non-nullable DTO strings are implicitly `[Required]` — a company with only a name returns 400 on five "optional" fields. Affects **every** POST/PUT. The 116 warnings are load-bearing. | High |
| F3 | Reminder services never read or write `ReminderSent` — up to **24 duplicate emails per task**, 12 per meeting. | High |
| F4 | Reminders add three-level cross-container includes joining four containers — Cosmos scope **grew**. | High |
| F5 | Blob container created with `PublicAccessType.Blob` — **every uploaded CV is world-readable by URL**. | Critical |
| F6 | README documents plural routes; actual routes are singular. Plural → 404. | Low |

**Corrections to the original plan:**
- The committed Cosmos key is Microsoft's **public emulator key** — no rotation needed. Only the JWT `SecretKey` is a real committed secret.
- Git history is clean: no live credential ever committed. Two live-format keys sit on disk in gitignored `appsettings.json`, but the resources they point at **no longer exist**.
- **Your Azure subscription contains zero resources.** Phase 7 provisions from scratch; the endpoints in config are dead.
- Reminders will be **ported to Functions** (decision confirmed), fixing F3 and F4 during the port.

**Cosmos spike: ANSWERED.** Ran against a real free-tier Cosmos account (`rg-mifuturotrabajo`, West Europe, 1000 RU/s, $0, left in place for Phase 7).

| Works | Fails |
|---|---|
| `EnsureCreatedAsync` (12 containers) | `Include(j => j.Company)` — reference nav |
| writes, plain `Where()`, `WithPartitionKey`, `FindAsync` | `Include(j => j.InterviewSteps)` — collection nav |
| `.ToLower()` in a predicate | the exact `JobApplicationController.cs:46-49` query |

Cosmos names the remedy itself: *"not supported as the navigation is **not embedded in same resource**"* — which is exactly the Phase 2 design. Estimate of 20–30 h stands, plus F4.

Two further corrections: removing `.ToLower()` (Phase 2 step 6) is **optional polish**, not required — it translates fine. `EnsureCreatedAsync` needs no special handling.

**Fixed during Phase 0:** F5 (blob container now `PublicAccessType.None`) and F1 (recruiter routes aligned), both applied to your working tree.

---

### ✅ Phase 1 complete — [docs/phase1-results.md](docs/phase1-results.md)

| | Before | After |
|---|---|---|
| Build warnings | 116 | **0** |
| Unit tests | 51 / 52 | **52 / 52** |
| Integration tests | **0 / 31** | **32 / 32** |

Api→Client reference removed · **F2 fixed** (implicit `[Required]` suppressed, 60 DTO properties defaulted, 9 empty-id mappers corrected) · JWT secret rotated into user-secrets and removed from the committed file · committed `appsettings.Testing.json` makes the integration suite self-contained.

The integration suite had **never passed**, for two reasons: 46 routes were written against the README's fictional plural API rather than the real singular one, and `ForgotPassword_ResetPassword_FullFlow_Success` *asserted the account-takeover leak as expected behaviour* — it is now a regression guard against it.

The restored tests then caught two real bugs, both fixed: **soft-delete was inconsistent** (`GetById` ignored `IsDeleted`, returning 200 for deleted applications) and **`VerifyPassword` would have 500'd on OAuth-only accounts**, whose `PasswordHash` is legitimately null.

---

### ✅ Phase 2 complete — [docs/phase2-results.md](docs/phase2-results.md)

| | Before | After |
|---|---|---|
| Containers | 12 | **7** |
| Cross-container `.Include()` | 20 | **0** |
| Runs on real Cosmos | **No** | **Yes** |
| Tests | 84 / 84 | **84 / 84** |

Aggregates are embedded, so the board renders from **one single-partition query**. DTOs are unchanged, so the Blazor client needed **no modifications**. Verified against a real Cosmos account — a 9-step model spike, then the API itself exercised over HTTP.

**The partition-key call, measured rather than assumed.** `WithPartitionKey` is Cosmos-only and broke all 16 CRUD tests on InMemory. With sensitive-data logging on, `.Where(x => x.PartitionKeyProp == v)` targets the *same single partition* as `WithPartitionKey(v)`, while a non-key filter logs a null partition. All 24 calls became equality filters: production keeps single-partition reads, tests stay portable.

**F3 and F4 resolved early.** The reminder rewrite dropped the three-level includes (F4) and finally honours `ReminderSent` (F3) — previously up to 24 duplicate emails per task. Selection logic sits in a static `SelectDue` so the Phase 5 port to Functions carries it across unchanged.

Also: `Persistence:Provider` replaces the `localhost:8081` sniff that hid the whole problem; the false unique index on `User.Email` is gone; `NotesJson`/`LabelsJson` are native JSON arrays.

---

## Current state — what I verified in the code

These are findings from reading the source, not speculation. They define the work.

**1. The app has never run against Cosmos DB.** [`Program.cs:45-48`](MiFuturoTrabajo.Api/Program.cs#L45) silently substitutes `UseInMemoryDatabase` whenever the connection string contains `localhost:8081`. So "running on the Cosmos emulator" has always meant "running on a volatile in-memory store". Every Cosmos-specific assumption in the codebase is unvalidated.

**2. Cross-container `.Include()` will fail against real Cosmos.** The EF Core Cosmos provider cannot join documents across containers. Every read path uses it:
- [`JobApplicationController.cs:46-49`](MiFuturoTrabajo.Api/Controllers/JobApplicationController.cs#L46) — `Company`, `JobApplicationRecruiters.Recruiter`, `InterviewSteps`, `ApplicantTasks`
- [`MeetingController.cs:46`](MiFuturoTrabajo.Api/Controllers/MeetingController.cs#L46) — `MeetingRecruiters.Recruiter`
- [`ApplicantController.cs:41-42`](MiFuturoTrabajo.Api/Controllers/ApplicantController.cs#L41) — `CVVersions`, `PastJobReferences`

This is the single largest item in the roadmap. Deploying is not a configuration change.

**3. CV upload is broken end-to-end.** The client sends a real file as `MultipartFormDataContent` ([`ApplicantService.cs:32-36`](MiFuturoTrabajo.Client/Services/ApplicantService.cs#L32)); the API endpoint expects `[FromBody] Dictionary<string, string>` and reads `cvData["fileUrl"]` ([`ApplicantController.cs:153-203`](MiFuturoTrabajo.Api/Controllers/ApplicantController.cs#L153)). There is no file storage anywhere in the solution. The UI at [`MyProfile.razor:167`](MiFuturoTrabajo.Client/Pages/MyProfile.razor#L167) offers a working file picker for a feature that cannot succeed.

**4. No email is ever sent.** `IEmailService` is registered in [`Program.cs:61-69`](MiFuturoTrabajo.Api/Program.cs#L61) and injected into **zero** controllers. The send sites are commented out at [`AuthController.cs:406`](MiFuturoTrabajo.Api/Controllers/AuthController.cs#L406) and [`AuthController.cs:493`](MiFuturoTrabajo.Api/Controllers/AuthController.cs#L493).

**5. Auth leaks reset and verification tokens in HTTP responses.** `/forgot-password` returns `resetToken` and `/send-verification` returns `verificationToken` in the response body, marked `// Remove in production!` ([`AuthController.cs:493`](MiFuturoTrabajo.Api/Controllers/AuthController.cs#L493), [`AuthController.cs:407`](MiFuturoTrabajo.Api/Controllers/AuthController.cs#L407)). `/forgot-password` is `[AllowAnonymous]`, so **anyone can take over any account by email address alone.** The careful anti-enumeration message directly above it is defeated by the token in the same payload. This is the most serious defect in the codebase and it must not reach a public URL.

**6. The reminder services do not exist.** [`Program.cs:73-74`](MiFuturoTrabajo.Api/Program.cs#L73) references `TaskReminderHostedService` and `InterviewReminderHostedService`. Neither type exists anywhere in the repository — they are commented-out references to unwritten code. The entities are ready for them (`Meeting.ReminderSent`, `ApplicantTask.ReminderSent`, `Meeting.ReminderMinutesBefore`).

**7. The API references the Blazor WASM client project.** [`MiFuturoTrabajo.Api.csproj:24`](MiFuturoTrabajo.Api/MiFuturoTrabajo.Api.csproj#L24) plus `using MiFuturoTrabajo.Client.Services;` at [`Program.cs:7`](MiFuturoTrabajo.Api/Program.cs#L7). The API registers an HttpClient pointed at itself using the client's `AuthenticationHttpMessageHandler` ([`Program.cs:110-114`](MiFuturoTrabajo.Api/Program.cs#L110)) — but never registers that handler type in its own container, so resolving that client throws. It is dormant only because nothing injects it.

**8. Deployment blocker: no production client config.** `MiFuturoTrabajo.Client/wwwroot/` contains only `appsettings.Development.json`. In a published WASM build, `ApiBaseUrl` falls back to `https://localhost:7071/api/` ([`Program.cs:18`](MiFuturoTrabajo.Client/Program.cs#L18)). The deployed site would call the reviewer's own machine.

**9. Integration tests are very likely all failing.** `TestWebApplicationFactory` sets `UseEnvironment("Testing")` ([`TestWebApplicationFactory.cs:42`](MiFuturoTrabajo.Tests.Integration/TestWebApplicationFactory.cs#L42)), but the API project has **no `appsettings.json`** — only `appsettings.Development.json`. Under `Testing`, `Jwt:SecretKey` resolves to null and [`Program.cs:16-19`](MiFuturoTrabajo.Api/Program.cs#L16) throws `InvalidOperationException` during host startup, failing every test in the class. Phase 0 confirms this by running them.

**10. Secrets are committed.** `MiFuturoTrabajo.Api/appsettings.Development.json` contains a JWT signing key and a Cosmos account key. (The Google client ID in the client's config is public by design and is not a concern.)

### Explicit non-goals

- **The InMemory-provider fallback stays as-is** — you chose not to change it. Note the consequence: after Phase 2 it becomes the only reason local development works without an emulator, so Phase 2 makes the switch explicit via configuration rather than string-sniffing, but does not remove the capability.
- No billing, no teams/sharing, no mobile app, no job-board scraping.

---

## Phase 0 — Baseline and the Cosmos spike

**Objective:** Replace assumptions with measurements before committing to the redesign. Nothing here ships; everything here de-risks what follows.

**Steps**
1. `dotnet build MiFuturoTrabajo.sln -c Release` — record warnings, especially nullable ones across `DTO.cs`.
2. `dotnet test` on both test projects. Capture the failure list. Confirm or refute finding #9.
3. Run API + client together; walk every page and record what works: board drag-and-drop, list views, forms, calendar, profile, CV upload, meetings, tasks, login, register, forgot-password.
4. **The spike that decides Phase 2's size:** provision a Cosmos DB account on the free tier, point `CosmosDb:ConnectionString` at it (bypassing the localhost sniff), run `EnsureCreatedAsync`, and execute one `GET /api/jobapplications`. Record the exact exception from the first `.Include()`.
5. Write findings to `docs/phase0-baseline.md` — this becomes evidence for the architecture doc in Phase 8.

**Technical solutions**
- Cosmos DB **free tier**: 1000 RU/s shared + 25 GB, one account per subscription. Use **database-level shared throughput** so all containers share the 1000 RU/s.
- Use `dotnet user-secrets` for the spike connection string — do not put it in a file.

**Exit criteria:** A written baseline; the Cosmos failure mode observed firsthand rather than predicted.

---

## Phase 1 — Structural cleanup and configuration

**Objective:** Remove the two structural defects you flagged, and make configuration deployable. Small, fast, unblocks everything downstream.

**Steps**
1. **Break the Api→Client reference.** Delete the `ProjectReference` at [`MiFuturoTrabajo.Api.csproj:24`](MiFuturoTrabajo.Api/MiFuturoTrabajo.Api.csproj#L24), the `using` at [`Program.cs:7`](MiFuturoTrabajo.Api/Program.cs#L7), and the self-targeting HttpClient block at [`Program.cs:110-114`](MiFuturoTrabajo.Api/Program.cs#L110) — the API has no reason to call itself. Verify `Api` → `Infrastructure` + `Shared` only.
2. **Add `MiFuturoTrabajo.Api/appsettings.json`** with non-secret defaults and every configuration key present (empty values). This fixes finding #9 and gives App Service a schema to override.
3. **Add `MiFuturoTrabajo.Client/wwwroot/appsettings.json`** with the production `ApiBaseUrl` and Google client ID. Fixes finding #8.
4. **Add `appsettings.Testing.json`** to the API with a deterministic test JWT key, or set it via `builder.UseSetting` in `TestWebApplicationFactory`.
5. **Secrets out of git.** Strip the JWT key and Cosmos key from `appsettings.Development.json`; move to `dotnet user-secrets`. Add a `.env.example`-style `appsettings.Development.json.template`. Rotate the committed Cosmos key in the portal — it is in git history.
6. Re-run `dotnet test`; expect the integration suite to start executing.

**Technical solutions**
- Local secrets: `dotnet user-secrets` (already supported, zero cost, no new dependency).
- Production secrets: App Service **Application Settings** with `__` hierarchy separators (e.g. `Jwt__SecretKey`). Key Vault is the "correct" answer but adds cost and complexity; App Settings are encrypted at rest and adequate here. Document the choice — a reviewer respects a reasoned tradeoff more than an unexamined default.

**Exit criteria:** `Api` no longer references `Client`; `dotnet test` runs; no secrets in the working tree.

---

## Phase 2 — Cosmos aggregate redesign ⭐ largest phase

**Objective:** Model the data the way Cosmos DB is meant to be used — aggregates embedded in documents, one partition per read — so the app runs on real Cosmos and reads cost a single request unit. This is the phase that makes the deployment possible, and it is the strongest engineering story in the project.

**The design.** Twelve containers collapse to six. Five containers disappear entirely (`CVVersions`, `JobReferences`, `InterviewSteps`, `JobApplicationRecruiters`, `MeetingRecruiters`) because their contents become embedded owned types.

| Container | Partition key | Embedded (owned types) | Rationale |
| --- | --- | --- | --- |
| `Users` | `/email` (normalized lowercase) | — | Login is the hot path and becomes a point read. The JWT already carries an `email` claim ([`JwtService.cs:44-46`](MiFuturoTrabajo.Api/Services/JwtService.cs#L44)), so authenticated lookups are point reads too. |
| `Applicants` | `/userId` | `CVVersions`, `PastJobReferences` | Both are small, bounded, and only ever read with their applicant. |
| `JobApplications` | `/applicantId` | `InterviewSteps`, `RecruiterIds`, `Notes`, `Labels`, `CompanySnapshot` | The board view becomes one single-partition query returning fully-formed cards. |
| `Meetings` | `/applicantId` | `RecruiterIds` | Queried by date range for calendar and reminders. |
| `Companies` | `/userId` | `Labels` | Small reference set per user. |
| `Recruiters` | `/userId` | — | Small reference set per user. |
| `ApplicantTasks` | `/applicantId` | — | Stays separate: tasks exist independently of applications and are queried standalone by the Tasks page and the reminder job. |

**Steps**
1. Rewrite `OnModelCreating` in [`ApplicationDbContext.cs`](MiFuturoTrabajo.Infrastructure/ApplicationDbContext.cs) using `OwnsMany`/`OwnsOne` instead of `HasMany`/`WithOne`/`HasForeignKey`. Remove the `HasIndex(e => e.Email).IsUnique()` at line 39 — the Cosmos provider does not enforce unique indexes, so this is a false guarantee today. Enforce email uniqueness by making it the partition key plus a pre-insert point-read check.
2. Reshape entities: `InterviewStep`, `CVVersion`, `JobReference` lose their `Id`-as-document identity and their back-navigations, becoming owned types. Delete `JobApplicationRecruiter` and `MeetingRecruiter` join entities; replace with `List<string> RecruiterIds` on `JobApplication` and `Meeting`.
3. Replace `NotesJson` / `LabelsJson` string hacks with real `List<string>` properties — Cosmos stores JSON arrays natively, so the manual serialization is pure friction.
4. Add `CompanySnapshot` (owned: `Id`, `Name`) to `JobApplication`. This is the deliberate denormalization that removes the `Company` include. Accept the staleness tradeoff; add a fan-out update when a company is renamed (bounded: one single-partition query per user).
5. Rewrite every controller query: delete all `.Include()` calls; add `.WithPartitionKey(...)` to every query that can use it. Recruiters are resolved by loading the user's recruiter set once per request and joining in memory — a single-partition query over a set that is realistically dozens of items.
6. Normalize emails at write time so `.ToLower()` disappears from all four query sites in `AuthController` ([lines 79, 179, 476, 538](MiFuturoTrabajo.Api/Controllers/AuthController.cs#L79)) — the Cosmos provider's translation of `ToLower()` in predicates is a trap, and case-normalizing on write is correct regardless of provider.
7. Make provider selection explicit: replace the `localhost:8081` string-sniff with a `Persistence:Provider` setting (`Cosmos` | `InMemory`). Keeps the InMemory capability you asked to retain, but stops it from being a surprise.
8. Rewrite `MappingService` for the new shapes.
9. Update unit and integration tests to the new model.

**Technical solutions**
- EF Core Cosmos **owned entity types** for embedding — `builder.OwnsMany(x => x.InterviewSteps)`.
- `WithPartitionKey()` for single-partition queries; `ReadItemAsync`-equivalent point reads via `FindAsync(id, partitionKey)`.
- Password-reset and verification links carry `email` alongside the token, converting token lookups from cross-partition scans into point reads.
- **Cost note:** embedding is what keeps this inside the 1000 RU/s free tier. A board render goes from ~5 cross-partition queries to 1 single-partition query.

**Risk:** this is the phase most likely to overrun. If Phase 0's spike reveals the redesign is larger than expected, the escape hatch is switching to PostgreSQL/Azure SQL, where the existing `.Include()` code works unchanged. Decide at the end of Phase 0, not mid-Phase-2.

**Exit criteria:** All CRUD, the board, and the calendar work against a real Cosmos account. Zero `.Include()` calls remain. Tests green.

---

## Phase 3 — CV upload with real file storage

**Objective:** Make the file picker in `MyProfile.razor` actually store and retrieve a document.

**Steps**
1. Replace `UploadCV` in [`ApplicantController.cs:153`](MiFuturoTrabajo.Api/Controllers/ApplicantController.cs#L153) with an `IFormFile`-based endpoint (`[FromForm]`, `multipart/form-data`), matching what the client already sends.
2. Validate: extension in `.pdf/.doc/.docx`, size ≤ 5 MB, content-type sniffing on the first bytes (do not trust the client's content type), and a per-user CV count cap (see Phase 7 quotas).
3. Add `IFileStorageService` in `MiFuturoTrabajo.Api/Services/` with a `BlobFileStorageService` implementation. Blob path: `cvs/{userId}/{cvVersionId}{ext}`.
4. Store the blob name — **not** a URL — in `CVVersion.FileUrl` (rename to `BlobPath`). Generate download URLs on read.
5. Add `GET /api/applicant/CV/{cvId}/download` returning a short-lived (15-minute) user-delegation SAS URL. Fix [`CVVersionsList.razor:264`](MiFuturoTrabajo.Client/Pages/CVVersionsList.razor#L264) to call it instead of opening a stored URL directly.
6. Delete the blob when the CV version is deleted ([`ApplicantController.cs:274`](MiFuturoTrabajo.Api/Controllers/ApplicantController.cs#L274)).

**Technical solutions**
- `Azure.Storage.Blobs` against a **Standard LRS, Hot** account, private container. Cost at demo volume is cents per month.
- **Managed identity** from App Service to Storage (`Storage Blob Data Contributor`) — no connection string in configuration, and user-delegation SAS requires it. This is a genuinely reviewer-impressive detail.
- Blazor WASM already streams correctly via `OpenReadStream(maxAllowedSize: 10MB)` ([`MyProfile.razor:313`](MiFuturoTrabajo.Client/Pages/MyProfile.razor#L313)) — align that limit with the server's 5 MB.

**Exit criteria:** Upload a PDF, see it listed, download it, delete it, confirm the blob is gone.

---

## Phase 4 — Transactional email and auth hardening

**Objective:** Send real email, and close the account-takeover hole before anything is public. **This phase is a hard gate on Phase 6 deployment.**

**Steps**
1. **Remove `resetToken` from the `/forgot-password` response and `verificationToken` from `/send-verification`.** Non-negotiable — this is finding #5.
2. Inject `IEmailService` into `AuthController` and send at the two commented sites ([line 406](MiFuturoTrabajo.Api/Controllers/AuthController.cs#L406), [line 493](MiFuturoTrabajo.Api/Controllers/AuthController.cs#L493)).
3. Build the links against a configured `Client:BaseUrl`, not the hardcoded `https://your-domain.com` placeholders. Include `email` in the query string (Phase 2 point-read design).
4. Add HTML email templates: verification, password reset, task reminder, interview reminder (the last two feed Phase 5).
5. Add expiry to `EmailConfirmationToken` — `User` has `PasswordResetTokenExpiry` but no equivalent for verification ([`User.cs:40`](MiFuturoTrabajo.Infrastructure/Entities/User.cs#L40)), so verification tokens currently live forever.
6. Hash reset and verification tokens at rest, comparing hashes on redemption — a database read should not yield a usable credential.
7. Make send failures non-fatal to the request: log and return the neutral message, so a mail outage cannot enumerate accounts by timing or error.
8. Decide and implement whether unverified users can log in. Recommendation: **allow login, restrict nothing, show a persistent banner** — a reviewer testing signup should not hit a wall waiting for mail.

**Technical solutions**
- **Azure Communication Services Email** with your custom domain (you own one) — add the DKIM/SPF/DMARC DNS records ACS generates. Pricing is roughly $0.25 per 1,000 emails: effectively free here, and a custom domain lands in the inbox where an Azure-managed domain often does not.
- `EmailService` currently constructs `EmailClient` from a connection string in a `Singleton` factory ([`Program.cs:61-69`](MiFuturoTrabajo.Api/Program.cs#L61)) and falls back to the literal string `"dummy"` when unconfigured — which constructs successfully and fails at send time. Replace with managed identity and fail fast at startup if unconfigured in Production.
- Add a `NoOpEmailService` selected by configuration for local dev and tests.

**Exit criteria:** Full password-reset round-trip through a real inbox. No token appears in any HTTP response. Verified by an integration test asserting the response body contains no token field.

---

## Phase 5 — Reminders as Azure Functions

**Objective:** Write the two reminder services that `Program.cs` has been advertising, on infrastructure that costs nothing.

**Steps**
1. New project `MiFuturoTrabajo.Functions` (isolated worker, .NET 8) referencing `Infrastructure` and `Shared`. Add to the solution.
2. `InterviewReminderFunction` — timer trigger every 15 minutes. Cross-partition query for meetings where `SendReminderEmail && !ReminderSent && StartDateTime - ReminderMinutesBefore <= now`. Send, then set `ReminderSent = true`.
3. `TaskReminderFunction` — timer trigger daily at 08:00. Query tasks where `!Completed && !ReminderSent && Deadline` is within 24 hours. Send, then flag.
4. Set `ReminderSent` **before** dispatching, or make sends idempotent — a timer trigger can fire twice, and duplicate reminder mail reads as a bug.
5. Delete the misleading commented-out lines at [`Program.cs:72-74`](MiFuturoTrabajo.Api/Program.cs#L72).
6. Unit-test the selection predicates against an InMemory context; they are pure logic and cheap to cover.

**Technical solutions**
- **Consumption plan**: 1M executions + 400,000 GB-s free per month. Two timers at this cadence sit far inside the grant.
- `[TimerTrigger("0 */15 * * * *")]` and `[TimerTrigger("0 0 8 * * *")]`. Store cron expressions in app settings so they are tunable without redeploying.
- Share `ApplicationDbContext` and `IEmailService` with the API via project reference — no duplicated data access.
- Managed identity to Cosmos and ACS, same as the API.
- **README correction:** the current README asserts "ASP.NET Core Web API (NOT Azure Functions)". That was a statement about the API's hosting model and remains true; the reminders are a separate scheduled workload. Reword rather than delete, and say why.

**Exit criteria:** A meeting created 70 minutes out with `SendReminderEmail = true` produces exactly one email at the 60-minute mark.

---

## Phase 6 — OAuth verified end-to-end

**Objective:** Prove Google and Microsoft sign-in work in the deployed environment, not just in theory.

**Steps**
1. Register the Google OAuth client and the Microsoft Entra app registration properly; add redirect URIs for **both** localhost and the production Static Web Apps domain.
2. Move client IDs into the client's `appsettings.json` / `appsettings.Development.json` (Phase 1) and read them at runtime instead of relying on the values baked into [`index.html`](MiFuturoTrabajo.Client/wwwroot/index.html).
3. Review `OAuthService` token validation — confirm audience and issuer are actually checked, not merely decoded.
4. Handle account linking: an OAuth sign-in whose email matches an existing password account must link, not create a duplicate or silently fail. `User` already has `OAuthProvider` / `OAuthProviderId` fields for this.
5. Test the full matrix: new user via Google; existing password user signing in with Google; new user via Microsoft; each on localhost and production.

**Technical solutions**
- Keep the existing `Google.Apis.Auth` / MSAL.js approach — it works and is already integrated. The gap is configuration and verification, not implementation.
- Both external scripts load from CDNs in `index.html`; confirm they are permitted by whatever CSP the Static Web App serves.

**Exit criteria:** All six matrix cases pass against the deployed site.

---

## Phase 7 — Azure deployment, CI/CD, and demo hardening

**Objective:** A public URL a recruiter can click, that stays up, that cannot be trivially abused, and that never looks empty.

### Hosting — resolving your two constraints

You chose **App Service + Static Web Apps**, and also **near-zero cost**. These are compatible, with one caveat worth stating plainly:

| Component | Service | Tier | Cost |
| --- | --- | --- | --- |
| Blazor WASM client | Static Web Apps | **Free** | $0 — custom domain + managed TLS included |
| API | App Service (Linux) | **F1 Free** | $0 |
| Reminders | Functions | Consumption | $0 within grant |
| Database | Cosmos DB | Free tier | $0 (1000 RU/s, 25 GB) |
| CV files | Blob Storage | Standard LRS Hot | cents/month |
| Email | Communication Services | Pay-as-you-go | ~$0 at demo volume |

**The F1 caveat:** 60 CPU-minutes/day and no Always On, so the API cold-starts after ~20 minutes idle — a recruiter's first request may take several seconds — and sustained traffic can exhaust the daily quota, after which the app returns 403 until midnight UTC. At demo volume this is unlikely but not impossible. **B1 Basic (~US$13/month) removes both limits.** My recommendation: launch on F1, and if a cold start ever costs you an impression, the upgrade is one slider. The custom domain goes on the Static Web App (free); the API stays on `*.azurewebsites.net`, which has TLS and needs only a CORS entry.

**Steps**
1. Provision with **Bicep** in `infra/` — Cosmos, Storage, App Service, Static Web App, Functions, ACS. Infrastructure-as-code is itself portfolio evidence.
2. Configure managed identity and RBAC for the API and Functions against Cosmos, Storage, and ACS.
3. Fix CORS: [`Program.cs:81-98`](MiFuturoTrabajo.Api/Program.cs#L81) hardcodes localhost origins and defaults `BlazorClient:Url` to the API's own port. Point it at the Static Web App domain, and confirm `AllowDevelopment` (`AllowAnyOrigin`) cannot activate in Production.
4. **Rate limiting** — ASP.NET Core's built-in `AddRateLimiter`: a strict fixed-window on `/api/auth/*` (registration and login abuse), a looser per-user limit elsewhere.
5. **Per-user quotas** — max applications, companies, recruiters, and CV versions per user; enforced in controllers, configurable, returning `429`. Plus an Azure Monitor budget alert on the subscription.
6. **Seed a demo account** — an idempotent seeder producing a realistic 3-month job hunt: ~15 applications across all 8 statuses, companies, recruiters, past and upcoming meetings, open and completed tasks, a CV version. Advertise the credentials on the login page. This is what a reviewer actually sees.
7. **GitHub Actions**: `build-test.yml` (build + both test projects on every push/PR) and `deploy.yml` (on `main` — publish API to App Service, client to Static Web Apps, Functions app). Add status badges.
8. Smoke-test the deployed site against the Phase 0 page-by-page checklist.

**Exit criteria:** Public URL works from a machine that has never seen the project. CI green. Demo login shows a populated, believable board.

---

## Phase 8 — Documentation that matches reality

**Objective:** The README is the first thing a reviewer reads. Right now it claims things that are not true, which is worse than claiming less.

**Steps**
1. Rewrite `README.md`: live demo URL and credentials at the top; accurate feature list; the "Known Limitations" section either emptied or made honest.
2. Correct the specific false claims: the Cosmos schema table (now six containers, not twelve), the "NOT Azure Functions" line, the CV-upload note, the email-skeleton note, and the test counts (verify against actual `dotnet test` output rather than restating the old numbers).
3. `docs/architecture.md` — a C4-ish diagram plus the reasoning behind the decisions a reviewer will ask about: why embedded aggregates, why the `CompanySnapshot` denormalization, why custom JWT over Entra, why Functions for reminders, why App Settings over Key Vault.
4. `docs/decisions/` — short ADRs for the Phase 2 persistence redesign and the Phase 7 hosting tradeoff. Reference the Phase 0 baseline as evidence.
5. `docs/local-setup.md` — genuinely reproducible from a clean clone, including user-secrets setup.

**Exit criteria:** Someone who has only read the README and clicked the demo has an accurate picture of the system.

---

## Sequencing and dependencies

```
Phase 0 (baseline + Cosmos spike)
   └─> Phase 1 (structure + config)
          └─> Phase 2 (Cosmos aggregates) ◄── the long pole
                 ├─> Phase 3 (CV + Blob)
                 ├─> Phase 4 (email + auth hardening) ── hard gate ──┐
                 │      └─> Phase 5 (Functions reminders)            │
                 └─────────────────────────────────────────────────> Phase 7 (deploy + CI + demo)
                                                Phase 6 (OAuth) ────>┘
                                                                      └─> Phase 8 (docs)
```

Phases 3, 4, and 6 are independent of one another once Phase 2 lands, so order them by appetite. **Phase 4 must complete before Phase 7** — the token-leak defect cannot go public.

Rough effort, given an open-ended timeline: Phase 0 ~3h · Phase 1 ~3h · **Phase 2 ~20-30h** · Phase 3 ~6h · Phase 4 ~8h · Phase 5 ~6h · Phase 6 ~5h · Phase 7 ~12h · Phase 8 ~5h. Call it 70-80 hours, dominated by the Cosmos redesign.

---

## Risks

| Risk | Likelihood | Response |
| --- | --- | --- |
| Phase 2 is larger than estimated | Medium-high | Decision gate at end of Phase 0. Escape hatch: PostgreSQL/Azure SQL, where existing `.Include()` code works unchanged. |
| Cosmos free tier already used in your subscription | Medium | One free-tier account per subscription. Fall back to serverless (~$0.28/M RU) — still cents at demo volume. |
| F1 quota exhaustion or cold starts hurt the demo | Low-medium | Upgrade to B1 (~$13/mo). Budget alert gives early warning. |
| ACS domain verification blocked by DNS propagation | Low | Start Phase 4 DNS records early; they can propagate while other phases proceed. |
| Test suite needs substantial rewriting after Phase 2 | High | Expected and budgeted — treat test updates as part of Phase 2, not a follow-up. |
| Git history contains the leaked Cosmos key | Certain | Rotate the key in the portal. History rewriting is not worth it for a rotated key. |

---

## Verification

**Per phase:** `dotnet build` clean, `dotnet test` green, and the phase's own exit criteria exercised manually.

**End-to-end acceptance,** run against the deployed site from a clean browser profile:
1. Register a new account → verification email arrives → link verifies.
2. Forgot password → email arrives → reset works → old password rejected. **Confirm no token in any response body.**
3. Sign in with Google; sign in with Microsoft.
4. Create a company, recruiter, and application; drag it across all 8 board columns.
5. Upload a PDF CV; download it; set default; delete it; confirm the blob is gone.
6. Schedule a meeting 70 minutes out with reminders on → exactly one email at the 60-minute mark.
7. Create a task due tomorrow → reminder arrives at 08:00.
8. Calendar shows deadlines, interview steps, meetings, and tasks together.
9. Log in as the demo account → board is populated and realistic.
10. Hammer `/api/auth/login` → rate limiter returns 429.
11. Exceed a per-user quota → clean 429 with a useful message, not a 500.
12. Register a second account → confirm it cannot see the first account's data (tenant isolation).
