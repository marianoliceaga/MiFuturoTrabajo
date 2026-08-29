# ADR 0003 — Reminders as Azure Functions timer triggers

**Status:** Accepted · **Date:** 2026-08-26

## Context

Two reminder jobs: a daily digest of tasks falling due, and a poll for interviews entering their notification window.

They existed as `BackgroundService` implementations inside the API, polling on a `Task.Delay` loop.

## The problem

**App Service Free has no Always On.** The site unloads after roughly twenty minutes idle and does not come back until an HTTP request arrives. A polling loop in that process simply stops — silently, with no error and no missed-run signal.

So on the exact tier this project deploys to, the reminders would have looked implemented and sent nothing. The failure mode is invisible: no exception, no alert, just mail that never arrives.

## Options

**1. Upgrade to B1 for Always On.** About USD 13/month, and it makes reminder delivery depend on the web tier never being downgraded — a coupling that is invisible until it breaks.

**2. External cron hitting an HTTP endpoint.** Needs a scheduler, and an endpoint that must be authenticated yet callable by a machine. More moving parts and a new public surface.

**3. Azure Functions timer triggers on Consumption.** Own host, own schedule, and 1M executions plus 400,000 GB-s free per month — these two jobs sit far inside that.

## Decision

Option 3.

| Function | Default schedule | Why |
|---|---|---|
| `TaskReminders` | `0 0 8 * * *` | Tasks have day-scale deadlines; a morning digest is the useful shape |
| `InterviewReminders` | `0 */15 * * * *` | Per-meeting lead times can be minutes, so the poll must be finer than the reminder |

Schedules come from app settings (`%TaskReminderSchedule%`), not string literals, so cadence is tunable — and a job pausable, by pointing it at a far-future expression — without a redeploy.

## Consequences

**A new deployable, and a package move.** `IEmailService` and `EmailTemplates` lived in the API. A Functions project cannot sensibly reference a web project — the same mistake that was removed when the API stopped referencing the Blazor client — so both moved to `Infrastructure`, which the API and Functions already reference.

That move exposed a **stored XSS**: both hosted services interpolated task and meeting titles into HTML raw, and a title is user-supplied text rendered by a mail client. Routing reminders through `EmailTemplates`, where every value is HTML-encoded, closed it.

**The functions are thin on purpose.** Bind a schedule, call `ReminderService`, log the result. Everything worth testing lives in `Infrastructure` and knows nothing about Functions.

**Time is a parameter, not the clock.** `SendTaskRemindersAsync(DateTime nowUtc, ...)` is what lets a timer trigger and a unit test asserting behaviour at an exact instant drive the same code path — no clock abstraction, no sleeping in tests.

**At-most-once, deliberately.** `ReminderSent` is committed *before* the send. A timer can fire twice — overlapping executions, a retry after a transient failure, a scaled-out host — and a duplicate reminder reads to a user as a bug where a missed one reads as nothing at all.

**Interview steps cannot notify independently.** They are embedded in the job-application document (ADR 0001), so there is no container to poll. A step that needs a reminder has to be represented by a `Meeting`. This is a real consequence of the persistence decision, and worth confirming against how steps are expected to behave.

**The API must not host them too.** The registrations were removed; leaving them would mean two schedulers racing for the same rows.

## Verification

The real `func` host reading a real Cosmos account, with seeded rows covering every branch. Both timers fired and selected exactly the right ones — honouring the 24-hour horizon, `Completed`, opt-out, and per-meeting lead time. The next tick twenty seconds later reported *"No reminders due"*, which is precisely the behaviour that was broken before `ReminderSent` was honoured.
