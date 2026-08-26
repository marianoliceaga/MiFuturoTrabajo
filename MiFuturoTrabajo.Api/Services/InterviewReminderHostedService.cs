using Microsoft.EntityFrameworkCore;
using MiFuturoTrabajo.Infrastructure;
using MiFuturoTrabajo.Infrastructure.Entities;

namespace MiFuturoTrabajo.Api.Services
{
    public class InterviewReminderHostedService : BackgroundService
    {
        private readonly ILogger<InterviewReminderHostedService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly TimeSpan _pollingInterval = TimeSpan.FromHours(4); // Run every 4 hours

        public InterviewReminderHostedService(ILogger<InterviewReminderHostedService> logger, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Interview Reminder Hosted Service is starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Interview Reminder Hosted Service is running.");
                
                try
                {
                    await ProcessRemindersAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred executing interview reminders.");
                }

                await Task.Delay(_pollingInterval, stoppingToken);
            }
        }

        private async Task ProcessRemindersAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

            var now = DateTime.UtcNow;
            var next48Hours = now.AddHours(48);

            // Get Meetings in next 48 hours
            var upcomingMeetings = await context.Meetings
                .Include(m => m.Applicant)
                .ThenInclude(a => a.User)
                .Include(m => m.JobApplication)
                .ThenInclude(ja => ja.Company)
                .Where(m => m.StartDateTime >= now && m.StartDateTime <= next48Hours)
                .ToListAsync();

            // Also check InterviewSteps (could be merged if they share logic, but we'll do both)
            var upcomingSteps = await context.InterviewSteps
                .Include(s => s.JobApplication)
                .ThenInclude(ja => ja.Applicant)
                .ThenInclude(a => a.User)
                .Include(s => s.JobApplication)
                .ThenInclude(ja => ja.Company)
                .Where(s => s.ScheduledDate >= now && s.ScheduledDate <= next48Hours && !s.IsCompleted)
                .ToListAsync();

            var usersToNotify = upcomingMeetings.Select(m => m.Applicant.User)
                .Union(upcomingSteps.Select(s => s.JobApplication.Applicant.User))
                .DistinctBy(u => u.Id);

            foreach (var user in usersToNotify)
            {
                if (user == null || string.IsNullOrEmpty(user.Email)) continue;

                var userMeetings = upcomingMeetings.Where(m => m.Applicant.UserId == user.Id).ToList();
                var userSteps = upcomingSteps.Where(s => s.JobApplication.Applicant.UserId == user.Id).ToList();

                if (userMeetings.Count == 0 && userSteps.Count == 0) continue;

                var htmlBody = $"<h3>Upcoming Interview Reminders</h3><p>Hi {user.FirstName},</p><p>You have upcoming interviews in the next 48 hours:</p><ul>";
                
                foreach (var meeting in userMeetings)
                {
                    var companyName = meeting.JobApplication?.Company?.Name ?? "Unknown Company";
                    htmlBody += $"<li><b>Meeting</b> - {companyName} at {meeting.StartDateTime:g}</li>";
                }

                foreach (var step in userSteps)
                {
                    var companyName = step.JobApplication?.Company?.Name ?? "Unknown Company";
                    htmlBody += $"<li><b>{step.StepName}</b> - {companyName} at {step.ScheduledDate:g}</li>";
                }
                
                htmlBody += "</ul><p>Good luck!</p>";

                try
                {
                    await emailService.SendEmailAsync(user.Email, "Mi Futuro Trabajo - Interview Reminders", htmlBody);
                    _logger.LogInformation($"Sent interview reminders to {user.Email}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to send interview reminders to {user.Email}");
                }
            }
        }
    }
}
