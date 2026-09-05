using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MiFuturoTrabajo.Infrastructure;
using MiFuturoTrabajo.Infrastructure.Email;
using MiFuturoTrabajo.Infrastructure.Entities;
using MiFuturoTrabajo.Infrastructure.Reminders;
using Xunit;

namespace MiFuturoTrabajo.Tests.Unit.Services
{
    /// <summary>
    /// Phase 2 wrote the selection predicates "so the Phase 5 port can unit-test
    /// them" and then never did. This is that debt paid.
    ///
    /// The time under test is a parameter, not the wall clock, so these assert
    /// behaviour at an exact instant with no clock mocking and no sleeping.
    /// </summary>
    public class ReminderServiceTests
    {
        private static readonly DateTime Now = new(2026, 3, 10, 9, 0, 0, DateTimeKind.Utc);

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"reminders-{Guid.NewGuid()}")
                .Options);

        private sealed class RecordingEmailService : IEmailService
        {
            public List<(string To, string Subject, string Body)> Sent { get; } = new();

            public Task SendEmailAsync(string toEmail, string subject, string htmlContent)
            {
                Sent.Add((toEmail, subject, htmlContent));
                return Task.CompletedTask;
            }
        }

        private sealed class ThrowingEmailService : IEmailService
        {
            public int Attempts { get; private set; }

            public Task SendEmailAsync(string toEmail, string subject, string htmlContent)
            {
                Attempts++;
                throw new InvalidOperationException("mail provider is down");
            }
        }

        /// <summary>
        /// Poll interval defaults to zero so most tests read as "is this
        /// meeting's own window open right now", with no cadence mixed in.
        /// Tests that are about the cadence pass it explicitly.
        /// </summary>
        private static ReminderService Build(
            ApplicationDbContext db, IEmailService email, int pollIntervalMinutes = 0) =>
            new(db, email, NullLogger<ReminderService>.Instance,
                new ReminderOptions { InterviewPollIntervalMinutes = pollIntervalMinutes });

        private static Applicant AnApplicant(string email = "person@example.com") => new()
        {
            Id = Guid.NewGuid().ToString(),
            UserId = Guid.NewGuid().ToString(),
            Name = "Ana",
            Lastname = "Lopez",
            ContactEmail = email
        };

        // ==============================================================
        // Task selection
        // ==============================================================

        [Fact]
        public void SelectDueTasks_IncludesATaskInsideTheWindow()
        {
            var tasks = new[]
            {
                new ApplicantTask { Title = "Follow up", Deadline = Now.AddHours(5) }
            }.AsQueryable();

            Assert.Single(ReminderService.SelectDueTasks(tasks, Now));
        }

        [Theory]
        [InlineData(-1, false, "already overdue")]
        [InlineData(25, false, "beyond the 24-hour horizon")]
        [InlineData(5, true, "inside the window")]
        public void SelectDueTasks_RespectsTheWindow(int hoursFromNow, bool expected, string because)
        {
            var tasks = new[]
            {
                new ApplicantTask { Title = "T", Deadline = Now.AddHours(hoursFromNow) }
            }.AsQueryable();

            Assert.True(
                ReminderService.SelectDueTasks(tasks, Now).Any() == expected,
                $"a task {because} should{(expected ? "" : " not")} be selected");
        }

        [Fact]
        public void SelectDueTasks_ExcludesCompletedTasks()
        {
            var tasks = new[]
            {
                new ApplicantTask { Title = "Done", Deadline = Now.AddHours(2), Completed = true }
            }.AsQueryable();

            Assert.Empty(ReminderService.SelectDueTasks(tasks, Now));
        }

        /// <summary>
        /// The guard that stops a reminder being re-sent on every run. Before
        /// Phase 2 this flag was never consulted.
        /// </summary>
        [Fact]
        public void SelectDueTasks_ExcludesTasksAlreadyNotified()
        {
            var tasks = new[]
            {
                new ApplicantTask { Title = "Sent", Deadline = Now.AddHours(2), ReminderSent = true }
            }.AsQueryable();

            Assert.Empty(ReminderService.SelectDueTasks(tasks, Now));
        }

        // ==============================================================
        // Meeting selection and lead time
        // ==============================================================

        [Fact]
        public void SelectDueMeetings_RequiresTheReminderToBeRequested()
        {
            var meetings = new[]
            {
                new Meeting { Title = "Opted out", StartDateTime = Now.AddMinutes(30), SendReminderEmail = false }
            }.AsQueryable();

            Assert.Empty(ReminderService.SelectDueMeetings(meetings, Now));
        }

        [Fact]
        public void SelectDueMeetings_ExcludesMeetingsAlreadyNotified()
        {
            var meetings = new[]
            {
                new Meeting
                {
                    Title = "Sent", StartDateTime = Now.AddMinutes(30),
                    SendReminderEmail = true, ReminderSent = true
                }
            }.AsQueryable();

            Assert.Empty(ReminderService.SelectDueMeetings(meetings, Now));
        }

        /// <summary>
        /// The exit criterion from the roadmap: a meeting 70 minutes out with a
        /// 60-minute lead time is not due yet, and is due ten minutes later.
        /// </summary>
        [Fact]
        public void IsWithinLeadTime_FiresAtTheLeadTimeAndNotBefore()
        {
            var meeting = new Meeting
            {
                Title = "Interview",
                StartDateTime = Now.AddMinutes(70),
                ReminderMinutesBefore = 60,
                SendReminderEmail = true
            };

            Assert.False(ReminderService.IsWithinLeadTime(meeting, Now, TimeSpan.Zero));
            Assert.True(ReminderService.IsWithinLeadTime(meeting, Now.AddMinutes(10), TimeSpan.Zero));
        }

        [Fact]
        public void IsWithinLeadTime_HonoursAPerMeetingLeadTime()
        {
            var tomorrow = new Meeting
            {
                StartDateTime = Now.AddHours(20),
                ReminderMinutesBefore = 24 * 60
            };

            var imminent = new Meeting
            {
                StartDateTime = Now.AddHours(20),
                ReminderMinutesBefore = 15
            };

            Assert.True(ReminderService.IsWithinLeadTime(tomorrow, Now, TimeSpan.Zero));
            Assert.False(ReminderService.IsWithinLeadTime(imminent, Now, TimeSpan.Zero));
        }

        // ==============================================================
        // Cadence - a lead time narrower than the gap between polls
        // ==============================================================

        /// <summary>
        /// The regression the three-hourly schedule would otherwise have
        /// introduced, at the level where it actually bites.
        ///
        /// Meeting at 11:30 with the entity's default 60-minute lead time.
        /// Polls run at 09:00 and 12:00. The window is 10:30-11:30: the 09:00
        /// run is too early, and by 12:00 the meeting has started, so
        /// SelectDueMeetings no longer returns it at all. Nothing is sent and
        /// nothing is logged as missed.
        /// </summary>
        [Fact]
        public async Task SendInterviewReminders_DoesNotStepOverAWindowNarrowerThanThePoll()
        {
            using var db = NewContext();
            var applicant = AnApplicant();
            db.Applicants.Add(applicant);
            db.Meetings.Add(new Meeting
            {
                ApplicantId = applicant.Id,
                Title = "Technical screen",
                StartDateTime = Now.AddHours(2.5),
                ReminderMinutesBefore = 60,
                SendReminderEmail = true
            });
            await db.SaveChangesAsync();

            var email = new RecordingEmailService();
            var service = Build(db, email, pollIntervalMinutes: 180);

            // The 09:00 run knows the next one is not until 12:00, by which
            // time this is unreachable - so it sends now rather than never.
            Assert.Equal(1, (await service.SendInterviewRemindersAsync(Now)).Sent);
            Assert.Single(email.Sent);
            Assert.Contains("Technical screen", email.Sent[0].Body);
        }

        /// <summary>
        /// The same meeting, on a poll fine enough to hit its window, waits.
        /// Firing early is a concession to cadence, not the new default.
        /// </summary>
        [Fact]
        public async Task SendInterviewReminders_DoesNotFireEarlyWhenThePollIsFineEnough()
        {
            using var db = NewContext();
            var applicant = AnApplicant();
            db.Applicants.Add(applicant);
            db.Meetings.Add(new Meeting
            {
                ApplicantId = applicant.Id,
                StartDateTime = Now.AddHours(2.5),
                ReminderMinutesBefore = 60,
                SendReminderEmail = true
            });
            await db.SaveChangesAsync();

            var email = new RecordingEmailService();
            var service = Build(db, email, pollIntervalMinutes: 15);

            Assert.Equal(0, (await service.SendInterviewRemindersAsync(Now)).Sent);
            Assert.Empty(email.Sent);
        }

        /// <summary>
        /// The widening is exactly one poll interval, not an open horizon: a
        /// meeting whose window opens after the next run still waits for it.
        /// </summary>
        [Fact]
        public void IsWithinLeadTime_WidensByOnePollIntervalAndNoMore()
        {
            var meeting = new Meeting
            {
                StartDateTime = Now.AddHours(10),
                ReminderMinutesBefore = 60
            };

            var poll = TimeSpan.FromHours(3);

            // Window opens at 18:00; the next run is at 12:00, which is still
            // early enough to catch it.
            Assert.False(ReminderService.IsWithinLeadTime(meeting, Now, poll));

            // From 15:00 the next run lands inside the window.
            Assert.True(ReminderService.IsWithinLeadTime(meeting, Now.AddHours(6), poll));
        }

        /// <summary>
        /// Firing early must not fire twice: ReminderSent is still the guard,
        /// so the run that follows an early send has nothing to do.
        /// </summary>
        [Fact]
        public async Task SendInterviewReminders_AnEarlySendIsStillSentOnce()
        {
            using var db = NewContext();
            var applicant = AnApplicant();
            db.Applicants.Add(applicant);
            db.Meetings.Add(new Meeting
            {
                ApplicantId = applicant.Id,
                StartDateTime = Now.AddHours(2.5),
                ReminderMinutesBefore = 60,
                SendReminderEmail = true
            });
            await db.SaveChangesAsync();

            var email = new RecordingEmailService();
            var service = Build(db, email, pollIntervalMinutes: 180);

            Assert.Equal(1, (await service.SendInterviewRemindersAsync(Now)).Sent);
            Assert.Equal(ReminderRun.Nothing, await service.SendInterviewRemindersAsync(Now.AddHours(2)));
            Assert.Single(email.Sent);
        }

        // ==============================================================
        // Dispatch
        // ==============================================================

        [Fact]
        public async Task SendTaskReminders_SendsOneDigestPerPerson()
        {
            using var db = NewContext();
            var applicant = AnApplicant();
            db.Applicants.Add(applicant);
            db.ApplicantTasks.AddRange(
                new ApplicantTask { ApplicantId = applicant.Id, Title = "Send thank-you note", Deadline = Now.AddHours(3) },
                new ApplicantTask { ApplicantId = applicant.Id, Title = "Prepare portfolio", Deadline = Now.AddHours(8) });
            await db.SaveChangesAsync();

            var email = new RecordingEmailService();
            var run = await Build(db, email).SendTaskRemindersAsync(Now);

            Assert.Equal(2, run.Considered);
            Assert.Equal(1, run.Recipients);

            // Two tasks, one message - not one message per task.
            var message = Assert.Single(email.Sent);
            Assert.Equal("person@example.com", message.To);
            Assert.Contains("Send thank-you note", message.Body);
            Assert.Contains("Prepare portfolio", message.Body);
        }

        [Fact]
        public async Task SendTaskReminders_MarksTasksSoTheyAreNotRepeated()
        {
            using var db = NewContext();
            var applicant = AnApplicant();
            db.Applicants.Add(applicant);
            db.ApplicantTasks.Add(new ApplicantTask
            {
                ApplicantId = applicant.Id, Title = "Follow up", Deadline = Now.AddHours(3)
            });
            await db.SaveChangesAsync();

            var email = new RecordingEmailService();
            var service = Build(db, email);

            await service.SendTaskRemindersAsync(Now);
            await service.SendTaskRemindersAsync(Now.AddMinutes(15));
            await service.SendTaskRemindersAsync(Now.AddHours(1));

            // Three runs, one email. Before the ReminderSent guard existed this
            // would have produced one per run for the whole day.
            Assert.Single(email.Sent);
            Assert.True(await db.ApplicantTasks.AllAsync(t => t.ReminderSent));
        }

        /// <summary>
        /// A timer trigger can fire twice - overlapping executions, a retry, a
        /// scaled-out host. The flag is committed before the send, so the worst
        /// case is a missed reminder rather than a duplicate one.
        /// </summary>
        [Fact]
        public async Task SendTaskReminders_DoesNotResendWhenTheMailProviderFails()
        {
            using var db = NewContext();
            var applicant = AnApplicant();
            db.Applicants.Add(applicant);
            db.ApplicantTasks.Add(new ApplicantTask
            {
                ApplicantId = applicant.Id, Title = "Follow up", Deadline = Now.AddHours(3)
            });
            await db.SaveChangesAsync();

            var failing = new ThrowingEmailService();
            var service = Build(db, failing);

            var first = await service.SendTaskRemindersAsync(Now);

            Assert.Equal(1, failing.Attempts);
            Assert.Equal(0, first.Sent);

            var second = await service.SendTaskRemindersAsync(Now.AddMinutes(15));

            Assert.Equal(0, second.Considered);
            Assert.Equal(1, failing.Attempts);
        }

        [Fact]
        public async Task SendReminders_SkipsApplicantsWithNoContactAddress()
        {
            using var db = NewContext();
            var applicant = AnApplicant(email: "");
            db.Applicants.Add(applicant);
            db.ApplicantTasks.Add(new ApplicantTask
            {
                ApplicantId = applicant.Id, Title = "Follow up", Deadline = Now.AddHours(3)
            });
            await db.SaveChangesAsync();

            var email = new RecordingEmailService();
            var run = await Build(db, email).SendTaskRemindersAsync(Now);

            Assert.Empty(email.Sent);
            Assert.Equal(0, run.Recipients);
        }

        [Fact]
        public async Task SendReminders_DoesNotLeakOneApplicantsItemsToAnother()
        {
            using var db = NewContext();
            var ana = AnApplicant("ana@example.com");
            var luis = AnApplicant("luis@example.com");
            db.Applicants.AddRange(ana, luis);
            db.ApplicantTasks.AddRange(
                new ApplicantTask { ApplicantId = ana.Id, Title = "Ana secret task", Deadline = Now.AddHours(3) },
                new ApplicantTask { ApplicantId = luis.Id, Title = "Luis secret task", Deadline = Now.AddHours(3) });
            await db.SaveChangesAsync();

            var email = new RecordingEmailService();
            await Build(db, email).SendTaskRemindersAsync(Now);

            Assert.Equal(2, email.Sent.Count);

            var toAna = email.Sent.Single(m => m.To == "ana@example.com");
            Assert.Contains("Ana secret task", toAna.Body);
            Assert.DoesNotContain("Luis secret task", toAna.Body);
        }

        [Fact]
        public async Task SendInterviewReminders_WaitsForTheLeadTime()
        {
            using var db = NewContext();
            var applicant = AnApplicant();
            db.Applicants.Add(applicant);
            db.Meetings.Add(new Meeting
            {
                ApplicantId = applicant.Id,
                Title = "Technical screen",
                StartDateTime = Now.AddMinutes(70),
                ReminderMinutesBefore = 60,
                SendReminderEmail = true
            });
            await db.SaveChangesAsync();

            var email = new RecordingEmailService();
            var service = Build(db, email);

            Assert.Equal(0, (await service.SendInterviewRemindersAsync(Now)).Sent);
            Assert.Empty(email.Sent);

            // Ten minutes later it is exactly 60 minutes out.
            Assert.Equal(1, (await service.SendInterviewRemindersAsync(Now.AddMinutes(10))).Sent);
            Assert.Single(email.Sent);
            Assert.Contains("Technical screen", email.Sent[0].Body);
        }

        [Fact]
        public async Task SendReminders_OnAnEmptyStoreDoesNothing()
        {
            using var db = NewContext();
            var email = new RecordingEmailService();
            var service = Build(db, email);

            Assert.Equal(ReminderRun.Nothing, await service.SendTaskRemindersAsync(Now));
            Assert.Equal(ReminderRun.Nothing, await service.SendInterviewRemindersAsync(Now));
            Assert.Empty(email.Sent);
        }

        // ==============================================================
        // Message content
        // ==============================================================

        /// <summary>
        /// The previous hosted services interpolated task and meeting titles
        /// into HTML raw. A title is user-supplied and the message lands in a
        /// mail client, so that was stored XSS with the same shape as the one
        /// fixed for auth mail in Phase 4.
        /// </summary>
        [Fact]
        public async Task ReminderBody_EncodesUserSuppliedTitles()
        {
            using var db = NewContext();
            var applicant = AnApplicant();
            db.Applicants.Add(applicant);
            db.ApplicantTasks.Add(new ApplicantTask
            {
                ApplicantId = applicant.Id,
                Title = "<script>alert(1)</script>",
                Deadline = Now.AddHours(3)
            });
            await db.SaveChangesAsync();

            var email = new RecordingEmailService();
            await Build(db, email).SendTaskRemindersAsync(Now);

            var body = Assert.Single(email.Sent).Body;
            Assert.DoesNotContain("<script>", body);
            Assert.Contains("&lt;script&gt;", body);
        }

        [Fact]
        public async Task ReminderSubject_NamesASingleItemAndCountsSeveral()
        {
            using var db = NewContext();
            var solo = AnApplicant("solo@example.com");
            var busy = AnApplicant("busy@example.com");
            db.Applicants.AddRange(solo, busy);
            db.ApplicantTasks.Add(new ApplicantTask
            {
                ApplicantId = solo.Id, Title = "Call the recruiter", Deadline = Now.AddHours(3)
            });
            db.ApplicantTasks.AddRange(
                new ApplicantTask { ApplicantId = busy.Id, Title = "A", Deadline = Now.AddHours(3) },
                new ApplicantTask { ApplicantId = busy.Id, Title = "B", Deadline = Now.AddHours(4) });
            await db.SaveChangesAsync();

            var email = new RecordingEmailService();
            await Build(db, email).SendTaskRemindersAsync(Now);

            Assert.Contains("Call the recruiter", email.Sent.Single(m => m.To == "solo@example.com").Subject);
            Assert.Contains("2 tasks", email.Sent.Single(m => m.To == "busy@example.com").Subject);
        }
    }
}
