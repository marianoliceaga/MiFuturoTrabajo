# ADR 0001 — Model for Cosmos with embedded aggregates

**Status:** Accepted · **Date:** 2026-08-26

## Context

The project advertised Azure Cosmos DB. It had never run on it.

Startup silently substituted the EF InMemory provider whenever the connection string contained `localhost:8081`, so "running against the emulator" had always meant "running against a volatile in-memory store". Every Cosmos-specific assumption in the codebase was unvalidated, and one of them was wrong in a way that made deployment impossible:

**The EF Core Cosmos provider cannot join documents across containers.** The model had twelve containers and twenty cross-container `.Include()` calls. Every read path used at least one. None could have executed.

This was not a configuration problem. It was the largest single item in the roadmap.

## Options

**1. Move to PostgreSQL or Azure SQL.** The existing `.Include()` code would work unchanged. Fastest path to a deployable app, and the escape hatch if the redesign overran.

**2. Keep twelve containers and join in memory.** Fetch each set separately and stitch. No model change, but the board becomes five cross-partition queries per render — the shape most likely to exhaust a shared 1000 RU/s.

**3. Model it the way Cosmos is meant to be used.** Embed aggregates as owned types, partition by the id each aggregate is read by.

## Decision

Option 3.

Seven containers. `CVVersions`, `JobReferences`, `InterviewSteps`, `JobApplicationRecruiters` and `MeetingRecruiters` disappear — their contents are embedded in the documents they belong to.

| Container | Partition key | Embedded |
|---|---|---|
| `Users` | `/Email` | — |
| `Applicants` | `/UserId` | `CVVersions`, `PastJobReferences` |
| `JobApplications` | `/ApplicantId` | `InterviewSteps`, `Company` snapshot, `Notes`, `Labels`, `RecruiterIds` |
| `Meetings` | `/ApplicantId` | `RecruiterIds` |
| `ApplicantTasks` | `/ApplicantId` | — |
| `Companies` | `/UserId` | `Labels` |
| `Recruiters` | `/UserId` | — |

Option 1 was the pragmatic choice and was held in reserve. It was not taken because the data genuinely is document-shaped — a job application is read whole, with its steps and notes, essentially always — and because "we advertised Cosmos and shipped SQL" is exactly the gap between claim and reality this project exists to close.

Option 2 was rejected because it keeps the cost of the wrong model while paying the RU/s bill for it.

## Consequences

**The board renders from one single-partition query.** It was five cross-partition ones.

**Denormalisation, deliberately.** A job application carries a snapshot of its company's name, because the board needs a name on every card and Cosmos cannot join. Renaming a company fans out across that user's applications, bounded by one single-partition query. A rare write pays so that a per-page-load read does not.

**Aggregates cannot be queried independently.** Interview steps are no longer their own container, so nothing can poll "all steps due soon". A step that needs to notify has to be represented by a `Meeting`. This is a real loss, accepted knowingly.

**The unique index on email was a lie and is gone.** Cosmos does not enforce unique indexes. Uniqueness now comes from the partition key plus a pre-insert check.

**`WithPartitionKey()` is not used, and that was measured rather than assumed.** It is Cosmos-only and broke all sixteen CRUD tests, which run on InMemory — the classic bind where making code faithful to production makes it untestable. With sensitive-data logging on, `.Where(x => x.PartitionKeyProp == v)` targets the *same single partition* as `WithPartitionKey(v)`, while a non-key filter logs a null partition. All twenty-four calls became equality filters: production keeps single-partition reads and the tests stay portable.

**The DTOs did not change**, so the Blazor client needed no modifications at all.

## Verification

A nine-step model spike against a real Cosmos account before any controller was touched, then the API itself exercised over HTTP against the same account — thirty-nine Cosmos queries, with the board proving the embedded aggregate round-trips.
