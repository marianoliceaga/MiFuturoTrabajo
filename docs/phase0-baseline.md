# Phase 0 — Baseline

**Date:** 26 August 2026
**Committed HEAD audited:** `89a3a9c`
**Working tree audited:** main checkout at `D:\Projects\MiFuturoTrabajo` — 34 uncommitted changes (+580/−142), read-only
**Method:** Release build, both test suites, live API exercised over HTTP, git history scanned, Azure subscription enumerated

> The working tree is significantly ahead of `HEAD`. Everything below distinguishes the two, because the roadmap was originally written against `HEAD` and several findings are now stale.

---

## 1. Build

`dotnet build MiFuturoTrabajo.sln -c Release`

| | |
|---|---|
| Errors | **0** |
| Warnings | **116** |
| Time | 21.9 s |

All 116 warnings are nullable-reference: `CS8618` (non-nullable never assigned), `CS8603` (possible null return), `CS8625`, `CS8601`, `CS8604`.

**These warnings are not cosmetic — see finding F2.**

---

## 2. Tests

| Suite | Passed | Failed | Total |
|---|---|---|---|
| `MiFuturoTrabajo.Tests.Unit` | 51 | **1** | 52 |
| `MiFuturoTrabajo.Tests.Integration` | **0** | **31** | 31 |

### Unit failure
`PasswordHasherTests.VerifyPassword_ShouldHandleNullHash` — expects `ArgumentNullException`, none thrown. BCrypt returns `false` rather than throwing. The test encodes an assumption the implementation does not honour; decide which is correct rather than deleting the test.

### Integration: all 31 fail identically
```
System.InvalidOperationException : JWT SecretKey is not configured in appsettings.json
   at Program.<Main>$(String[] args) in ...\MiFuturoTrabajo.Api\Program.cs:line 18
```
`TestWebApplicationFactory` sets `UseEnvironment("Testing")`; at `HEAD` the API has no `appsettings.json`, so `Jwt:SecretKey` resolves null and the host throws before any test body runs.

**Status in working tree:** `MiFuturoTrabajo.Api/appsettings.json` now exists and carries a `Jwt:SecretKey`, so this is likely already resolved there. **Not yet re-verified** — the working tree was audited read-only and its tests were not run.

The README's claim of "30+ integration tests ✅" has never been true at any committed commit.

---

## 3. Confirmed exploitable: anonymous account takeover

Reproduced end-to-end against the running instance. **No authentication at any step.**

```
POST /api/auth/forgot-password   {"email":"<victim>"}
  200 {"message":"If the email exists...","resetToken":"891515a842794817bafb80ce2157bd24"}

POST /api/auth/reset-password    {"email":"<victim>","resetToken":"891515a8...","newPassword":"..."}
  200 {"message":"Password reset successfully"}

POST /api/auth/login             {"email":"<victim>","password":"<attacker-chosen>"}
  200 {"accessToken":"eyJ..."}
```

`POST /api/auth/send-verification` leaks `verificationToken` the same way.

Knowing only an email address yields full account control. The anti-enumeration message is defeated by the token in the same payload.

**Status in working tree: FIXED.** No token is returned; `IEmailService` is injected and both send sites are live ([AuthController.cs:423, :527](../MiFuturoTrabajo.Api/Controllers/AuthController.cs)).

---

## 4. New findings not visible from static reading

### F1 — `RecruiterService` 404s on every call *(still unfixed in working tree)*

| | |
|---|---|
| Client requests | `api/recruiter`, `api/recruiter/RecruiterAdd`, … |
| Controller serves | `api/Recruiters/…` |

`RecruitersController` + `[Route("api/[controller]")]` → the token resolves to the **plural class name**. Every recruiter operation in the UI fails.

```
GET /api/recruiter    404
GET /api/Recruiters   200  []
```

The README lists "Recruiter Contacts ✅". The feature cannot work. Companies is unaffected only because `CompaniesController` and the client's `"companies"` happen to agree.

### F2 — Non-nullable DTO strings are implicitly `[Required]` *(still unfixed)*

`<Nullable>enable</Nullable>` + non-nullable `string` properties ⇒ ASP.NET Core model validation treats every one as required. Submitting a company with only a name:

```
POST /api/companies/CompanyAdd
{"id":"...","name":"OnlyName Inc","description":null,"website":null,
 "telephone":null,"address":null,"country":null,"labels":[]}

400 {"errors":{"Address":["The Address field is required."],
                "Country":[...],"Website":[...],
                "Telephone":[...],"Description":[...]}}
```

This affects **every POST/PUT in the application**. The client compounds it: `CompanyService.CreateAsync` calls `EnsureSuccessStatusCode()`, so a 400 surfaces as an unhandled exception rather than a field error.

Related: `MappingService.ToEntity` uses `Id = dto.Id ?? Guid.NewGuid().ToString()`, which catches `null` but **not** empty string — posting `"id":""` persists an entity with an empty key. The client works around all of this by minting `Guid.NewGuid()` client-side ([CompaniesList.razor:199](../MiFuturoTrabajo.Client/Pages/CompaniesList.razor)).

### F3 — Reminder services send duplicate mail *(working-tree code)*

Both new `BackgroundService` implementations re-query and re-send on every poll. Neither reads or writes `ReminderSent`, which exists on both entities and is only ever assigned `false` in `MappingService` (lines 229, 276).

| Service | Poll | Window | Duplicate emails per item |
|---|---|---|---|
| `TaskReminderHostedService` | 1 h | 24 h | **up to 24** |
| `InterviewReminderHostedService` | 4 h | 48 h | **up to 12** |

### F4 — Reminders deepen the Cosmos problem *(working-tree code)*

Both services use three-level cross-container includes, newly enabled by an added `Applicant.User` navigation:

```csharp
context.ApplicantTasks
    .Include(t => t.Applicant).ThenInclude(a => a.User)
    .Include(t => t.JobApplication).ThenInclude(ja => ja.Company)
```

Four containers joined in one query. Under the Cosmos provider this cannot execute.

### F5 — Route documentation is wrong throughout the README

README documents plural routes (`/api/jobapplications`, `/api/meetings`, `/api/applicanttasks`, `/api/cvversions`). Actual routes are singular (`/api/jobapplication`, …). Verified: plural → 404, singular → 200.

---

## 5. Credentials and Azure

### On disk (not in git)
`MiFuturoTrabajo.Api/appsettings.json` contains a live-format Cosmos DB account key and a **full Azure Storage account key**. The file is gitignored ([.gitignore:366](../.gitignore)) and has never been committed.

### Git history is clean
Scanned every blob across all refs:
- No commit contains the storage account key or the Cosmos key.
- A deleted `IMPLEMENTATION_GUIDE.md` references the resource *names* and an already-redacted `AccountKey=***`.
- Committed `appsettings.Development.json` files contain only `AccountEndpoint=https://localhost:8081/` — Microsoft's **public emulator key**, not a secret.

**Correction to the original roadmap:** the committed Cosmos key needs no rotation. Only the JWT `SecretKey` (committed at `appsettings.Development.json`) is a genuine secret in git.

### Azure subscription is empty
```
Subscription 1 (036f7b39-…)  →  Total resources: 0
```
Resource groups exist; none relate to this project. `rgCap`, `letmework4udb`, and `mifuturotrabajostg` are gone.

Deployed endpoints referenced in config are dead:

| Endpoint | Result |
|---|---|
| `https://letmework4uapi.azurewebsites.net` | does not resolve |
| `https://ambitious-sand-077de7b03.3.azurestaticapps.net` | 404 |

Consequences: the on-disk keys are **stale**, and Phase 7 provisions from scratch rather than adopting existing infrastructure.

---

## 6. The Cosmos spike — ANSWERED

Provisioned a free-tier Cosmos DB account (`mifuturotrabajo-spike-14923`, West Europe, `rg-mifuturotrabajo`, 1000 RU/s shared, **$0**) and ran the real data-access paths against it. Prediction confirmed.

| Operation | Result |
|---|---|
| `EnsureCreatedAsync` — 12 containers | **OK** |
| `SaveChangesAsync` — 4 documents | **OK** |
| `Where(j => j.ApplicantId == …)` | **OK** |
| `WithPartitionKey(applicantId)` | **OK** |
| `FindAsync(id)` point read | **OK** |
| `Where(u => u.Email.ToLower() == …)` | **OK** — translates fine |
| `Include(j => j.Company)` reference nav | **FAIL** |
| `Include(j => j.InterviewSteps)` collection nav | **FAIL** |
| Exact `JobApplicationController.cs:46-49` query | **FAIL** |

Reference navigation:
```
InvalidOperationException: The LINQ expression 'DbSet<JobApplication>()
  .Where(...).Join(inner: DbSet<Company>(), ...)' could not be translated.
```

Collection navigation — Cosmos names the remedy itself:
```
InvalidOperationException: Including navigation
'JobApplication.InterviewSteps (ICollection<InterviewStep>)'
is not supported as the navigation is not embedded in same resource.
```

**Conclusion.** Every read path in the API fails against real Cosmos. Phase 2's embedded-aggregate redesign is confirmed as necessary and is precisely what the provider asks for. The 20–30 h estimate stands, plus F4.

**Corrections to the roadmap:**
- Removing `.ToLower()` (Phase 2 step 6) is **optional polish**, not required — it translates.
- `EnsureCreatedAsync` works normally; no special provisioning path needed.

The account is **left in place** for Phase 7. Free tier, $0/month.

## 7. Roadmap status after rebaselining

| Phase | Original assumption | Actual |
|---|---|---|
| 1 — Structure | Api→Client ref; secrets; missing config | **Partly done.** Both `appsettings.json` files added. `ProjectReference` to Client **still present**. |
| 2 — Cosmos | Not started | Not started. `localhost:8081` sniff still at [Program.cs:44](../MiFuturoTrabajo.Api/Program.cs). Scope **grew** (F4). |
| 3 — CV storage | Not started | **Mostly done.** `IBlobStorageService`/`BlobStorageService` written and registered; `UploadCV` takes `[FromForm] IFormFile`. Gaps: connection-string auth not managed identity, container created `PublicAccessType.Blob` (**publicly readable CVs**), no SAS, no size/content validation. |
| 4 — Email & auth | Not started | **Mostly done.** Token leak fixed, email sending live, `VerifyEmail.razor` added. Gaps: no `EmailConfirmationTokenExpiry`, tokens not hashed at rest. |
| 5 — Reminders | Not started | **Written, as HostedServices.** Decision taken to port to Functions. Must fix F3 and F4 during the port. |
| 6 — OAuth | Not started | Not started. |
| 7 — Deploy/CI | Adopt existing | **From scratch** — subscription empty. |
| 8 — Docs | Rewrite | Unchanged, plus F5. |

---

## 8. Immediate next actions

1. ~~Unblock the Cosmos spike~~ — **done**, see section 6.
2. ~~**Fix F1**~~ — **done** — one-line route correction; a listed feature is entirely dead.
3. ~~**Fix the public CV container**~~ — **done**, now `PublicAccessType.None`.
4. **Re-run the test suites against the working tree** to confirm the integration suite now starts.
5. Commit or branch the 34 working-tree changes so this baseline has a stable reference point.
