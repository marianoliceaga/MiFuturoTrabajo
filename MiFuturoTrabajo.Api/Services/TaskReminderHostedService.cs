using Microsoft.EntityFrameworkCore;
using MiFuturoTrabajo.Infrastructure;
using MiFuturoTrabajo.Infrastructure.Entities;

namespace MiFuturoTrabajo.Api.Services
{
    public class TaskReminderHostedService : BackgroundService
    {
        private readonly ILogger<TaskReminderHostedService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly TimeSpan _pollingInterval = TimeSpan.FromHours(1); // Run every hour

        public TaskReminderHostedService(ILogger<TaskReminderHostedService> logger, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Task Reminder Hosted Service is starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Task Reminder Hosted Service is running.");
                
                try
                {
                    await ProcessRemindersAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred executing task reminders.");
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
            var next24Hours = now.AddHours(24);

            // Get tasks due in next 24 hours that are not completed
            var upcomingTasks = await context.ApplicantTasks
                .Include(t => t.Applicant)
                .ThenInclude(a => a.User)
                .Include(t => t.JobApplication)
                .ThenInclude(ja => ja.Company)
                .Where(t => t.Deadline >= now && t.Deadline <= next24Hours && !t.Completed)
                .ToListAsync();

            if (upcomingTasks.Count == 0) return;

            // Group by user to send one email per user if they have multiple tasks
            var userTasksGroup = upcomingTasks.GroupBy(t => t.Applicant.User);

            foreach (var group in userTasksGroup)
            {
                var user = group.Key;
                if (user == null || string.IsNullOrEmpty(user.Email)) continue;

                var htmlBody = $"<h3>Upcoming Task Reminders</h3><p>Hi {user.FirstName},</p><p>You have the following tasks due in the next 24 hours:</p><ul>";
                
                foreach (var task in group)
                {
                    var companyName = task.JobApplication?.Company?.Name ?? "General";
                    htmlBody += $"<li><b>{task.Title}</b> - {companyName} (Due: {task.Deadline:g})</li>";
                }
                
                htmlBody += "</ul><p>Good luck!</p>";

                try
                {
                    await emailService.SendEmailAsync(user.Email, "Mi Futuro Trabajo - Task Reminders", htmlBody);
                    _logger.LogInformation($"Sent task reminders to {user.Email}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to send task reminders to {user.Email}");
                }
            }
        }
    }
}
