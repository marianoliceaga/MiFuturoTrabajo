# Phase 5 — Reminders as Azure Functions

**Date:** 26 August 2026
**Branch:** `claude/phase-5-functions-reminders` (main checkout)
**Verified against:** the real Azure Functions host (`func` 4.12.1) reading real Cosmos

---

## Outcome

| | Before | After |
|---|---|---|
| Runtime | `BackgroundService` inside the API | **Azure Functions timer triggers** |
| Survives an idle App Service | **No** | Yes |
| Schedules | hardcoded `TimeSpan` | app settings, tunable without redeploy |
| Reminder titles in HTML | **unencoded** | encoded |
| Messages per person | one per item | **one digest** |
| Reminder tests | **none** | 20 |
| Unit tests | 85 | **104** |
| Integration tests | 62 | 62 |

---

## Why the move was necessary, not cosmetic

The hosted services polled on a `Task.Delay` loop inside the API process. On **App Service Free there is no Always On**: the site unloads after about 20 minutes idle and does not come back until an HTTP request arrives. A polling loop in that process simply stops — silently, with no error and no missed-run signal.

So on the tier this project is deploying to, the reminders would have looked implemented and sent nothing. A Consumption-plan timer has its own host and its own schedule, and 1M executions + 400,000 GB-s per month are free, which these two jobs sit far inside.

---

## Shape

Both functions are deliberately thin — bind a schedule, call a service, log the result:

```csharp
[Function(nameof(TaskReminders))]
public async Task TaskReminders(
    [TimerTrigger("%TaskReminderSchedule%")] TimerInfo timer,
    CancellationToken cancellationToken)
{
    var run = await _reminders.SendTaskRemindersAsync(DateTime.UtcNow, cancellationToken);
    Report("task", run, timer);
}
```

Everything worth testing lives in `MiFuturoTrabajo.Infrastructure/Reminders/ReminderService.cs`, which knows nothing about Functions and takes **the current time as a parameter**. That is what lets a timer trigger and a unit test asserting behaviour at an exact instant drive the same code path, with no clock abstraction and no sleeping in tests.

Schedules come from settings (`%TaskReminderSchedule%`, `%InterviewReminderSchedule%`) rather than string literals, so cadence is tunable — and a job can be paused by pointing it at a far-future expression — without a redeploy.

| Function | Default | Why |
|---|---|---|
| `TaskReminders` | `0 0 8 * * *` | Tasks have day-scale deadlines; a morning digest is the useful shape. |
| `InterviewReminders` | `0 */15 * * * *` | Per-meeting lead times can be minutes, so the poll must be finer than the reminder. |

---

## Email had to move out of the API first

`IEmailService` and `EmailTemplates` lived in `MiFuturoTrabajo.Api`. A Functions project cannot sensibly reference a web project — that is exactly the mistake Phase 1 removed when it broke the Api→Client reference — so both moved to `MiFuturoTrabajo.Infrastructure/Email/`, which the API and the Functions app already reference. `Azure.Communication.Email` moved with them.

Project graph now:

```
Functions ─┐
           ├─> Infrastructure ─> Shared
Api       ─┘
```

---

## A stored-XSS bug the move exposed

Both hosted services built their HTML by interpolation:

```csharp
$"<li><b>{t.Title}</b> (due {t.Deadline:g})</li>"
```

A task title is user-supplied and the message is rendered by a mail client. That is the same defect class fixed for auth mail in Phase 4, in code written at the same time — it was simply out of scope then. Routing reminders through `EmailTemplates`, where every interpolated value passes through `HtmlEncoder`, closes it. A test asserts a `<script>` title arrives encoded.

The seed data carried `Send <b>thank-you</b> note` deliberately, and it came back escaped.

---

## Digests, not one message per row

The old code sent one email per applicant per run but composed it inline. The templates now take a list, so someone with four tasks due gets one message listing four, with the subject naming a single item or counting several:

```
"Due soon: Call the recruiter"
"2 tasks due in the next 24 hours"
```

---

## At-most-once, deliberately

`ReminderSent` is committed **before** the send. A timer trigger can fire twice — overlapping executions, a retry after a transient failure, a scaled-out host — and the trade is stated plainly in the code: a duplicate reminder reads to a user as a bug, a missed one reads as nothing at all. A test drives a failing mail provider and asserts the item is not retried.

---

## Verified on the real host

Not a unit test and not a mock: the actual `func` host, reading the real Cosmos account, with seeded rows covering every branch.

```
Functions:
    InterviewReminders: timerTrigger
    TaskReminders: timerTrigger

Executing 'Functions.TaskReminders' (Reason='Timer fired at ...')
  [no email configured] Would have sent "2 tasks due in the next 24 hours" to ana-p5@example.com.
  task reminders: 2 item(s) due, 1 message(s) to 1 recipient(s).
Executed 'Functions.TaskReminders' (Succeeded, Duration=11365ms)

Executing 'Functions.InterviewReminders' (Reason='Timer fired at ...')
  [no email configured] Would have sent "Reminder: Technical screen" to ana-p5@example.com.
  interview reminders: 1 item(s) due, 1 message(s) to 1 recipient(s).
Executed 'Functions.InterviewReminders' (Succeeded, Duration=13660ms)
```

The next tick, twenty seconds later:

```
No task reminders due.
No interview reminders due.
```

Which is the whole point — before Phase 2 fixed `ReminderSent`, that second tick would have re-sent everything.

Final state read back out of Cosmos:

```
task     not sent  Completed task
task     SENT      Send <b>thank-you</b> note
task     SENT      Prepare portfolio
task     not sent  Luis far future
task     not sent  Far future task

meeting  not sent  Opted out
meeting  SENT      Technical screen        <- 30 min out, 60 min lead time
meeting  not sent  Later interview         <- 6 h out, 60 min lead time
```

Every selection rule — the 24-hour horizon, `Completed`, `SendReminderEmail`, per-meeting lead time, and per-applicant grouping — behaved correctly against the real store.

---

## The tests Phase 2 promised

Phase 2 extracted `SelectDue` "so the Phase 5 port can unit-test it" and then never wrote the tests. That debt is paid: **20 tests**, including the roadmap's own exit criterion as an assertion —

> a meeting created 70 minutes out with `SendReminderEmail = true` produces exactly one email at the 60-minute mark

— plus cross-applicant isolation (one person's task titles must not appear in another's email), the failing-provider case, and the empty-store case.

---

## Also in this phase

- `local.settings.json` was **not gitignored**. It holds the Cosmos and ACS connection strings, and committing it once puts them in history permanently. Added to `.gitignore` *before* creating one; `local.settings.json.example` is the tracked template.
- The Functions app has **no InMemory provider option**, unlike the API. A reminder host pointed at a throwaway in-process database reads an empty set and mails nobody — which looks exactly like working correctly. Cosmos or a startup failure.
- It also fails fast on missing Cosmos configuration, for the same reason.

---

## Carried forward

- **The Phase 4 timing oracle is still open.** I noted then that closing it means moving auth mail off the request path. This phase built scheduled infrastructure, not a queue, so `/forgot-password` still does more work for a known address than an unknown one. Closing it properly needs a queue trigger — a small addition to this project, but a different piece of work.
- **Nothing is deployed.** The Function App itself is Phase 7: Consumption plan, managed identity to Cosmos and ACS, and the two schedule settings.
- **ACS is still unconfigured**, so the verification above proves selection and dispatch, not delivery. Phase 7 does the real-inbox test for both these and the auth mail.
- **Interview-step reminders ride on `Meeting`** — flagged at the end of Phase 2 and still worth your confirmation. Steps are embedded in the job-application document, so there is no separate container to poll; a step that needs to notify has to be represented by a meeting.
