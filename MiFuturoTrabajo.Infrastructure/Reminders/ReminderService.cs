using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MiFuturoTrabajo.Infrastructure.Email;
using MiFuturoTrabajo.Infrastructure.Entities;

namespace MiFuturoTrabajo.Infrastructure.Reminders
{
    /// <param name="Considered">Rows the query returned.</param>
    /// <param name="Sent">Messages actually dispatched.</param>
    /// <param name="Recipients">Distinct people mailed.</param>
    public record ReminderRun(int Considered, int Sent, int Recipients)
    {
        public static readonly ReminderRun Nothing = new(0, 0, 0);
    }

    /// <summary>
    /// Decides who gets a reminder and sends it.
    ///
    /// Deliberately host-agnostic: no BackgroundService, no Functions types, and
    /// the current time arrives as a parameter rather than being read from the
    /// clock. That is what lets an Azure Functions timer trigger, and a unit
    /// test asserting behaviour at a specific instant, drive exactly the same
    /// code path.
    ///
    /// Cosmos notes:
    /// - No cross-container Include. Due rows are fetched first, then the owning
    ///   applicants are loaded by id and joined in memory.
    /// - Both queries are cross-partition by nature: "everything due soon"
    ///   spans all users, which is precisely the shape a scheduled job wants
    ///   and precisely the shape a per-request path must avoid.
    /// </summary>
    public class ReminderService
    {
        private readonly ApplicationDbContext _context;
        private readonly IEmailService _email;
        private readonly ILogger<ReminderService> _logger;

        public ReminderService(
            ApplicationDbContext context, IEmailService email, ILogger<ReminderService> logger)
        {
            _context = context;
            _email = email;
            _logger = logger;
        }

        // ------------------------------------------------------------------
        // Selection - pure, static, and the part worth testing exhaustively
        // ------------------------------------------------------------------

        /// <summary>
        /// Tasks falling due within 24 hours that have not been notified.
        ///
        /// <c>ReminderSent</c> is the idempotency guard. Without it this query
        /// re-selects the same task on every run and mails it once per run for
        /// a whole day.
        /// </summary>
        public static IQueryable<ApplicantTask> SelectDueTasks(IQueryable<ApplicantTask> source, DateTime nowUtc)
        {
            var horizon = nowUtc.AddHours(24);

            return source.Where(t =>
                !t.Completed &&
                !t.ReminderSent &&
                t.Deadline >= nowUtc &&
                t.Deadline <= horizon);
        }

        /// <summary>
        /// Meetings that opted into a reminder and start within the widest lead
        /// time we support. The per-meeting lead time is applied afterwards by
        /// <see cref="IsWithinLeadTime"/> - the store-side filter stays coarse
        /// because <c>ReminderMinutesBefore</c> varies per row and translating
        /// that arithmetic into a Cosmos query buys nothing.
        /// </summary>
        public static IQueryable<Meeting> SelectDueMeetings(IQueryable<Meeting> source, DateTime nowUtc)
        {
            var horizon = nowUtc.AddDays(2);

            return source.Where(m =>
                m.SendReminderEmail &&
                !m.ReminderSent &&
                m.StartDateTime >= nowUtc &&
                m.StartDateTime <= horizon);
        }

        /// <summary>Has this meeting entered its own notification window?</summary>
        public static bool IsWithinLeadTime(Meeting meeting, DateTime nowUtc) =>
            meeting.StartDateTime.AddMinutes(-meeting.ReminderMinutesBefore) <= nowUtc;

        // ------------------------------------------------------------------
        // Dispatch
        // ------------------------------------------------------------------

        public async Task<ReminderRun> SendTaskRemindersAsync(DateTime nowUtc, CancellationToken ct = default)
        {
            var due = await SelectDueTasks(_context.ApplicantTasks, nowUtc).ToListAsync(ct);

            return await DispatchAsync(
                due,
                t => t.ApplicantId,
                (applicant, tasks, language) => EmailTemplates.TaskReminder(
                    language,
                    applicant.Name,
                    tasks.OrderBy(t => t.Deadline)
                         .Select(t => (t.Title, t.Deadline))
                         .ToList()),
                t => t.ReminderSent = true,
                "task",
                ct);
        }

        public async Task<ReminderRun> SendInterviewRemindersAsync(DateTime nowUtc, CancellationToken ct = default)
        {
            var candidates = await SelectDueMeetings(_context.Meetings, nowUtc).ToListAsync(ct);

            var due = candidates.Where(m => IsWithinLeadTime(m, nowUtc)).ToList();

            return await DispatchAsync(
                due,
                m => m.ApplicantId,
                (applicant, meetings, language) => EmailTemplates.InterviewReminder(
                    language,
                    applicant.Name,
                    meetings.OrderBy(m => m.StartDateTime)
                            .Select(m => (m.Title, m.StartDateTime, LocationOf(m, language)))
                            .ToList()),
                m => m.ReminderSent = true,
                "interview",
                ct);
        }

        /// <summary>
        /// Group by owner, mark as sent, save, then mail - once per recipient
        /// rather than once per row, so someone with four tasks due gets one
        /// message rather than four.
        ///
        /// <paramref name="compose"/> receives the recipient's language, which
        /// is looked up once per run rather than per message.
        /// </summary>
        private async Task<ReminderRun> DispatchAsync<T>(
            List<T> due,
            Func<T, string> applicantIdOf,
            Func<Applicant, List<T>, string, EmailMessageContent> compose,
            Action<T> markSent,
            string kind,
            CancellationToken ct)
        {
            if (due.Count == 0)
            {
                return ReminderRun.Nothing;
            }

            var applicantIds = due.Select(applicantIdOf).Distinct().ToList();

            var applicants = await _context.Applicants
                .Where(a => applicantIds.Contains(a.Id))
                .ToListAsync(ct);

            var languages = await LanguagesByUserAsync(applicants, ct);

            var sent = 0;
            var recipients = 0;

            foreach (var applicant in applicants)
            {
                var items = due.Where(d => applicantIdOf(d) == applicant.Id).ToList();

                if (items.Count == 0 || string.IsNullOrWhiteSpace(applicant.ContactEmail))
                {
                    continue;
                }

                var language = languages.TryGetValue(applicant.UserId, out var stored)
                    ? stored
                    : EmailLanguage.Default;

                var message = compose(applicant, items, language);

                // Marked and committed BEFORE sending. A timer trigger can fire
                // twice - overlapping executions, a retry after a transient
                // failure, a scaled-out host - and a duplicate reminder reads to
                // a user as a bug where a missed one reads as nothing at all.
                // The trade is deliberate: at-most-once, not at-least-once.
                foreach (var item in items)
                {
                    markSent(item);
                }

                await _context.SaveChangesAsync(ct);

                try
                {
                    await _email.SendAsync(applicant.ContactEmail, message);

                    sent += 1;
                    recipients += 1;

                    _logger.LogInformation(
                        "Sent {Kind} reminder covering {Count} item(s) to {Email}",
                        kind, items.Count, applicant.ContactEmail);
                }
                catch (Exception ex)
                {
                    // One bad address must not stop the rest of the run.
                    _logger.LogError(ex, "Failed to send {Kind} reminders to {Email}", kind, applicant.ContactEmail);
                }
            }

            return new ReminderRun(due.Count, sent, recipients);
        }

        /// <summary>
        /// The language each of these applicants' accounts reads in, keyed by
        /// user id.
        ///
        /// One extra query per run, not one per message. It is cross-partition
        /// - Users is partitioned by email and this looks up by id - which is
        /// exactly the shape a scheduled job can afford and a per-request path
        /// cannot. An applicant whose user row is missing, or who never chose a
        /// language, is simply absent from the dictionary and falls back to the
        /// default at the call site.
        /// </summary>
        private async Task<Dictionary<string, string>> LanguagesByUserAsync(
            List<Applicant> applicants, CancellationToken ct)
        {
            var userIds = applicants
                .Select(a => a.UserId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToList();

            if (userIds.Count == 0)
            {
                return new Dictionary<string, string>();
            }

            var users = await _context.Users
                .Where(u => userIds.Contains(u.Id))
                .ToListAsync(ct);

            return users.ToDictionary(u => u.Id, u => EmailLanguage.Resolve(u.PreferredLanguage));
        }

        private static string? LocationOf(Meeting meeting, string language)
        {
            if (!string.IsNullOrWhiteSpace(meeting.OnlineMeetingURL)) return meeting.OnlineMeetingURL;
            if (!string.IsNullOrWhiteSpace(meeting.PhysicalLocation)) return meeting.PhysicalLocation;

            if (!meeting.IsPhonecall) return null;

            return EmailLanguage.Resolve(language) == EmailLanguage.English
                ? "Phone call"
                : "Llamada telefónica";
        }
    }
}
