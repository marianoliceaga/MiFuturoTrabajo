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

### Amendment, 2026-08-30 — `InterviewReminders` moved to `0 0 */3 * * *`

The fifteen-minute poll was the largest single line on the Azure bill: blob operations against `AzureWebJobsStorage` — lease renewals and schedule bookkeeping, not CV traffic — accounted for roughly 95% of a run rate of about €0.57/month. Three hours cuts the per-execution share of that.

It also breaks the reasoning in the table above, and the cost is not a delay. A meeting is selectable only between the moment its window opens and the moment it starts, so a lead time shorter than the gap between runs is not delivered late — it is **skipped silently**, and the next run reports nothing due. At three hours, only `ReminderMinutesBefore >= 180` is reliably notified. The entity default is 60, which lands roughly one time in three.

So the selection now sends early rather than not at all. `IsWithinLeadTime` takes the poll interval and fires at the last run before the window opens, which makes a 60-minute reminder on a three-hourly poll arrive up to three hours ahead of the meeting. Early is a visible, harmless failure and missing is an invisible one — the same trade that already put the `ReminderSent` commit before the send rather than after it.

The interval is configuration (`Reminders:InterviewPollIntervalMinutes`), not a constant, because it is only correct while it matches the cron. Two settings that must agree is a real cost; deriving one from the other means parsing NCRONTAB inside `Infrastructure`, which is host knowledge in the one project deliberately kept free of it. The asymmetry makes the drift survivable: set it too low and short-lead reminders are dropped again, set it too high and reminders merely arrive earlier than asked.

Cadence is now visible to the tests that care — `Build(db, email, pollIntervalMinutes)` — and invisible to the ones that do not, which pass zero and keep reading as pure lead-time assertions. Three of them fail if the widening is removed.

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
