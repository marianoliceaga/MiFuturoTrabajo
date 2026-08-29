# Phase 2 — Cosmos aggregate redesign

**Date:** 26 August 2026
**Branch:** `claude/phase-2-cosmos-aggregates` (main checkout)
**Verified against:** a real Azure Cosmos DB account, not the emulator and not InMemory

---

## Outcome

| | Before | After |
|---|---|---|
| Containers | 12 | **7** |
| Cross-container `.Include()` calls | 20 | **0** |
| Works against real Cosmos | **No** | **Yes** |
| Build warnings / errors | 4 / 0 | 4 / 0 |
| Unit tests | 52 / 52 | **52 / 52** |
| Integration tests | 32 / 32 | **32 / 32** |

The DTOs are unchanged, so the Blazor client needed **no modifications**.

---

## The model

Five containers disappear because their contents are now embedded owned types.

| Container | Partition key | Embedded |
|---|---|---|
| `Users` | `/Email` | — |
| `Applicants` | `/UserId` | `CVVersions`, `PastJobReferences` |
| `JobApplications` | `/ApplicantId` | `InterviewSteps`, `Company` snapshot, `Notes`, `Labels`, `RecruiterIds` |
| `Meetings` | `/ApplicantId` | `RecruiterIds` |
| `ApplicantTasks` | `/ApplicantId` | — |
| `Companies` | `/UserId` | `Labels` |
| `Recruiters` | `/UserId` | — |

Gone: `CVVersions`, `JobReferences`, `InterviewSteps`, `JobApplicationRecruiters`, `MeetingRecruiters`.

Also removed: the unique index on `User.Email` (Cosmos does not enforce unique indexes, so it was a false guarantee) and the `NotesJson` / `LabelsJson` string round-tripping (Cosmos stores JSON arrays natively).

---

## Verified on real Cosmos

A spike ran the model end-to-end before any controller was touched — **9/9 passed**: schema creation, embedded writes, the board query, embedded-child mutation, recruiter resolution, user point read, and company-rename fan-out.

Then the actual API was run against the same account and exercised over HTTP (39 Cosmos queries logged):

```
POST /api/auth/register                       201
POST /api/companies/CompanyAdd                201   (partial payload, F2 guard holds)
POST /api/jobapplication/JobApplicationAdd    201
GET  /api/jobapplication                      200   (was 4 Includes)
GET  /api/jobapplication/board                200
GET  /api/jobapplication/JobApplicationGetById/{id}  200
GET  /api/applicant/current                   200   (embedded CVs)
GET  /api/companies | /Recruiters | /meeting | /applicanttask | /cvversion   200
PATCH /api/jobapplication/{id}/status         200
```

The board response proves the aggregate survives the round trip:

```
column           : FirstInterview
job title        : Senior .NET Engineer
company snapshot : Contoso Ltd     <- only the id was sent on create
interview steps  : ['Tech screen']
notes            : ['phone screen ok']
labels           : ['remote']
```

---

## The partition-key decision

`WithPartitionKey()` is a Cosmos-only operator. Using it made all 16 CRUD integration tests fail, because the suite runs on the InMemory provider — the classic bind where making the code faithful to production makes it untestable.

Rather than assume, this was **measured** with sensitive-data logging on, so the logged partition key shows its real value:

| Query form | Partition targeted |
|---|---|
| `.Where(ja => ja.ApplicantId == id)` | `8435c147-…` — **single partition** |
| `.WithPartitionKey(id)` | `8435c147-…` — **single partition** |
| `.Where(ja => ja.JobTitle == …)` | `(null)` — cross-partition scan |

EF Core extracts the partition key from an equality predicate on the partition-key property, so the portable form is **exactly equivalent**. All 24 `WithPartitionKey` calls were replaced with equality filters: production keeps single-partition reads, and the tests keep running on InMemory.

(The `'?'` that appears in ordinary API logs is redaction, not a missing partition key.)

---

## Denormalisation and its upkeep

The board needs a company name on every card, and Cosmos cannot join. `CompanySnapshot` (id + name) is therefore embedded in each job application, written by `SyncCompanySnapshotAsync` on create and update.

The cost is a fan-out on rename: `FanOutCompanyRenameAsync` rewrites the snapshot across that user's applications, bounded by one single-partition query. Verified working in the spike.

---

## Two bugs found on the way

**A regression I introduced.** The mapper rewrite dropped the `?? string.Empty` guards. Non-nullable CLR strings are *required* in the EF model, so a null from the wire made `SaveChanges` throw — surfacing as a 500. The `Company_CreateWithOnlyName_Succeeds` regression test added in Phase 1 caught it immediately. Fixed with an `OrEmpty` helper across 45 assignments.

**F3, fixed early.** Both reminder services were rewritten to drop cross-container includes, and while there they gained the `ReminderSent` guard they never had. They previously re-sent on every poll: up to 24 duplicate emails per task and 12 per meeting. Selection logic is now isolated in a static `SelectDue` method so the Phase 5 port to Functions carries it across unchanged and can unit-test it.

---

## Also in this phase

- `Persistence:Provider` (`Cosmos` | `InMemory`) replaces the `localhost:8081` string-sniff that silently swapped providers — the thing that hid this entire problem for so long.
- `User.Applicant` and all other cross-container navigations removed; `Applicant.User` (added by the reminder work) removed with them.

---

## Carried forward

- **F4 is resolved** as a side effect: the three-level includes are gone.
- Phase 5 still owns the port from `BackgroundService` to Functions timer triggers.
- Interview-step reminders now ride on `Meeting`, since steps are no longer a queryable container. Worth confirming that matches how you want steps to notify.
- The Cosmos account (`rg-mifuturotrabajo`, free tier, $0) still holds spike and verification data. Phase 7 should provision cleanly or purge it.
