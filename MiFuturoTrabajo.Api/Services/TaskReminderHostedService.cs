using Microsoft.EntityFrameworkCore;
using MiFuturoTrabajo.Infrastructure;
using MiFuturoTrabajo.Infrastructure.Entities;

namespace MiFuturoTrabajo.Api.Services
{
    /// <summary>
    /// Sends a digest of tasks falling due in the next 24 hours.
    ///
    /// Interim home: this is scheduled to move to an Azure Functions timer
    /// trigger, because App Service Free has no Always On and a polling
    /// BackgroundService cannot be relied upon there. <see cref="SelectDue"/>
    /// is written to port across unchanged.
    ///
    /// Cosmos note: no cross-container Include. The task set is fetched first,
    /// then the owning applicants are loaded by id and joined in memory.
    /// </summary>
    public class TaskReminderHostedService : BackgroundService
    {
        private readonly ILogger<TaskReminderHostedService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly TimeSpan _pollingInterval = TimeSpan.FromHours(1);

        public TaskReminderHostedService(
            ILogger<TaskReminderHostedService> logger, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Task reminder service starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessRemindersAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing task reminders.");
                }

                await Task.Delay(_pollingInterval, stoppingToken);
            }
        }

        /// <summary>
        /// Pure selection predicate, unit-testable against an InMemory context.
        /// </summary>
        internal static IQueryable<ApplicantTask> SelectDue(IQueryable<ApplicantTask> source, DateTime nowUtc)
        {
            var horizon = nowUtc.AddHours(24);

            // ReminderSent is the idempotency guard. Without it this query
            // re-selects the same task on every poll and mails it once an hour
            // for a whole day.
            return source.Where(t =>
                !t.Completed &&
                !t.ReminderSent &&
                t.Deadline >= nowUtc &&
                t.Deadline <= horizon);
        }

        private async Task ProcessRemindersAsync(CancellationToken ct)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

            var due = await SelectDue(context.ApplicantTasks, DateTime.UtcNow).ToListAsync(ct);
            if (due.Count == 0)
            {
                return;
            }

            var applicantIds = due.Select(t => t.ApplicantId).Distinct().ToList();
            var applicants = await context.Applicants
                .Where(a => applicantIds.Contains(a.Id))
                .ToListAsync(ct);

            foreach (var applicant in applicants)
            {
                var tasks = due.Where(t => t.ApplicantId == applicant.Id).ToList();
                if (tasks.Count == 0 || string.IsNullOrWhiteSpace(applicant.ContactEmail))
                {
                    continue;
                }

                var html =
                    "<h3>Upcoming task reminders</h3>" +
                    $"<p>Hi {applicant.Name},</p>" +
                    "<p>You have the following tasks due in the next 24 hours:</p><ul>" +
                    string.Concat(tasks.Select(t => $"<li><b>{t.Title}</b> (due {t.Deadline:g})</li>")) +
                    "</ul><p>Good luck!</p>";

                // Flag before sending: the timer can fire twice, and a
                // duplicate reminder reads as a bug where a missed one does not.
                foreach (var t in tasks)
                {
                    t.ReminderSent = true;
                }

                await context.SaveChangesAsync(ct);

                try
                {
                    await emailService.SendEmailAsync(
                        applicant.ContactEmail, "Mi Futuro Trabajo - task reminders", html);

                    _logger.LogInformation(
                        "Sent {Count} task reminder(s) to {Email}", tasks.Count, applicant.ContactEmail);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send task reminders to {Email}", applicant.ContactEmail);
                }
            }
        }
    }
}
