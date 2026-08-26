using Microsoft.EntityFrameworkCore;
using MiFuturoTrabajo.Infrastructure;
using MiFuturoTrabajo.Infrastructure.Entities;

namespace MiFuturoTrabajo.Api.Services
{
    /// <summary>
    /// Reminds applicants about meetings coming up inside their configured
    /// lead time.
    ///
    /// Interim home: scheduled to move to an Azure Functions timer trigger.
    /// <see cref="SelectDue"/> is written to port across unchanged.
    ///
    /// Cosmos notes:
    /// - No cross-container Include. Meetings are fetched first, then the
    ///   owning applicants are loaded by id and joined in memory.
    /// - Interview steps are no longer their own container: they are embedded
    ///   in the job application document, so there is nothing separate to poll.
    ///   A step that needs a reminder is represented by a Meeting.
    /// </summary>
    public class InterviewReminderHostedService : BackgroundService
    {
        private readonly ILogger<InterviewReminderHostedService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly TimeSpan _pollingInterval = TimeSpan.FromMinutes(15);

        public InterviewReminderHostedService(
            ILogger<InterviewReminderHostedService> logger, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Interview reminder service starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessRemindersAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing interview reminders.");
                }

                await Task.Delay(_pollingInterval, stoppingToken);
            }
        }

        /// <summary>
        /// Meetings whose lead time has arrived and which have not been
        /// notified yet. ReminderSent is the idempotency guard.
        /// </summary>
        internal static IQueryable<Meeting> SelectDue(IQueryable<Meeting> source, DateTime nowUtc)
        {
            // Widest lead time we support, used to keep the server-side filter
            // simple; the exact per-meeting lead time is applied in memory.
            var horizon = nowUtc.AddDays(2);

            return source.Where(m =>
                m.SendReminderEmail &&
                !m.ReminderSent &&
                m.StartDateTime >= nowUtc &&
                m.StartDateTime <= horizon);
        }

        private async Task ProcessRemindersAsync(CancellationToken ct)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

            var now = DateTime.UtcNow;

            var candidates = await SelectDue(context.Meetings, now).ToListAsync(ct);

            // Apply each meeting's own lead time.
            var due = candidates
                .Where(m => m.StartDateTime.AddMinutes(-m.ReminderMinutesBefore) <= now)
                .ToList();

            if (due.Count == 0)
            {
                return;
            }

            var applicantIds = due.Select(m => m.ApplicantId).Distinct().ToList();
            var applicants = await context.Applicants
                .Where(a => applicantIds.Contains(a.Id))
                .ToListAsync(ct);

            foreach (var applicant in applicants)
            {
                var meetings = due
                    .Where(m => m.ApplicantId == applicant.Id)
                    .OrderBy(m => m.StartDateTime)
                    .ToList();

                if (meetings.Count == 0 || string.IsNullOrWhiteSpace(applicant.ContactEmail))
                {
                    continue;
                }

                var html =
                    "<h3>Upcoming interview reminders</h3>" +
                    $"<p>Hi {applicant.Name},</p>" +
                    "<p>You have the following coming up:</p><ul>" +
                    string.Concat(meetings.Select(m =>
                        $"<li><b>{m.Title}</b> at {m.StartDateTime:g}</li>")) +
                    "</ul><p>Good luck!</p>";

                // Flag before sending, for the same reason as the task job.
                foreach (var m in meetings)
                {
                    m.ReminderSent = true;
                }

                await context.SaveChangesAsync(ct);

                try
                {
                    await emailService.SendEmailAsync(
                        applicant.ContactEmail, "Mi Futuro Trabajo - interview reminders", html);

                    _logger.LogInformation(
                        "Sent {Count} interview reminder(s) to {Email}", meetings.Count, applicant.ContactEmail);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send interview reminders to {Email}", applicant.ContactEmail);
                }
            }
        }
    }
}
