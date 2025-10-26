// using Microsoft.Azure.Functions.Worker;
// using Microsoft.EntityFrameworkCore;
// using Microsoft.Extensions.Logging;
// using System.Text;
// using Letmework4U.Infrastructure;
// using Letmework4U.Functions.Services;

// namespace Letmework4U.Functions.Functions
// {
//     public class InterviewReminderFunction
//     {
//         private readonly ILogger<InterviewReminderFunction> _logger;
//         private readonly ApplicationDbContext _context;
//         private readonly IEmailService _emailService;

//         public InterviewReminderFunction(
//             ILogger<InterviewReminderFunction> logger,
//             ApplicationDbContext context,
//             IEmailService emailService)
//         {
//             _logger = logger;
//             _context = context;
//             _emailService = emailService;
//         }

//         [Function("InterviewReminder")]
//         public async Task Run([TimerTrigger("0 0 * * * *")] TimerInfo timer) // Every hour
//         {
//             _logger.LogInformation("Interview reminder function executed at: {time}", DateTime.UtcNow);

//             try
//             {
//                 var now = DateTime.UtcNow;

//                 // Find meetings that need reminders
//                 var meetingsNeedingReminders = await _context.Meetings
//                     .Where(m => !m.ReminderSent &&
//                                 m.SendReminderEmail &&
//                                 m.StartDateTime > now)
//                     .Include(m => m.Applicant)
//                     .Include(m => m.MeetingRecruiters).ThenInclude(mr => mr.Recruiter)
//                     .Include(m => m.JobApplication)
//                     .ToListAsync();

//                 // Filter to meetings within reminder window (can't use TimeSpan in EF query for Cosmos)
//                 meetingsNeedingReminders = meetingsNeedingReminders
//                     .Where(m => (m.StartDateTime - now).TotalMinutes <= m.ReminderMinutesBefore)
//                     .ToList();

//                 _logger.LogInformation($"Found {meetingsNeedingReminders.Count} meetings needing reminders");

//                 foreach (var meeting in meetingsNeedingReminders)
//                 {
//                     try
//                     {
//                         var subject = $"Interview Reminder: {meeting.Title}";
//                         var htmlContent = BuildInterviewReminderEmail(meeting);

//                         await _emailService.SendEmailAsync(meeting.Applicant.ContactEmail, subject, htmlContent);

//                         meeting.ReminderSent = true;
//                         _logger.LogInformation($"Sent interview reminder for meeting {meeting.Id} to {meeting.Applicant.ContactEmail}");
//                     }
//                     catch (Exception ex)
//                     {
//                         _logger.LogError(ex, $"Failed to send interview reminder for meeting {meeting.Id}");
//                     }
//                 }

//                 if (meetingsNeedingReminders.Any())
//                 {
//                     await _context.SaveChangesAsync();
//                 }

//                 _logger.LogInformation($"Interview reminder function completed. Sent {meetingsNeedingReminders.Count} reminders");
//             }
//             catch (Exception ex)
//             {
//                 _logger.LogError(ex, "Error in interview reminder function");
//             }
//         }

//         private string BuildInterviewReminderEmail(Infrastructure.Entities.Meeting meeting)
//         {
//             var sb = new StringBuilder();
//             sb.AppendLine("<!DOCTYPE html>");
//             sb.AppendLine("<html>");
//             sb.AppendLine("<body style='font-family: Arial, sans-serif;'>");
//             sb.AppendLine("<div style='max-width: 600px; margin: 0 auto;'>");
//             sb.AppendLine("<div style='background-color: #007bff; color: white; padding: 20px;'>");
//             sb.AppendLine("<h1>Upcoming Interview</h1>");
//             sb.AppendLine("</div>");
//             sb.AppendLine("<div style='padding: 20px; background-color: #f8f9fa;'>");
//             sb.AppendLine($"<p><strong>Title:</strong> {meeting.Title}</p>");
//             sb.AppendLine($"<p><strong>Start Time:</strong> {meeting.StartDateTime:yyyy-MM-dd HH:mm} UTC</p>");
//             sb.AppendLine($"<p><strong>End Time:</strong> {meeting.EndDateTime:yyyy-MM-dd HH:mm} UTC</p>");

//             if (meeting.JobApplication != null)
//                 sb.AppendLine($"<p><strong>Position:</strong> {meeting.JobApplication.JobTitle}</p>");

//             if (!string.IsNullOrEmpty(meeting.Description))
//                 sb.AppendLine($"<p><strong>Description:</strong> {meeting.Description}</p>");

//             if (meeting.MeetingRecruiters.Any())
//             {
//                 var recruiterNames = string.Join(", ",
//                     meeting.MeetingRecruiters.Select(mr => $"{mr.Recruiter.Name} {mr.Recruiter.Lastname}"));
//                 sb.AppendLine($"<p><strong>Interviewers:</strong> {recruiterNames}</p>");
//             }

//             sb.AppendLine("<hr/>");

//             if (meeting.IsPhonecall)
//                 sb.AppendLine("<p><strong>Type:</strong> Phone Call</p>");

//             if (!string.IsNullOrEmpty(meeting.OnlineMeetingURL))
//                 sb.AppendLine($"<p><strong>Meeting Link:</strong> <a href='{meeting.OnlineMeetingURL}'>{meeting.OnlineMeetingURL}</a></p>");

//             if (!string.IsNullOrEmpty(meeting.PhysicalLocation))
//                 sb.AppendLine($"<p><strong>Location:</strong> {meeting.PhysicalLocation}</p>");

//             if (!string.IsNullOrEmpty(meeting.MeetingAccessCode))
//                 sb.AppendLine($"<p><strong>Access Code:</strong> {meeting.MeetingAccessCode}</p>");

//             if (!string.IsNullOrEmpty(meeting.Note))
//             {
//                 sb.AppendLine("<hr/>");
//                 sb.AppendLine($"<p><strong>Notes:</strong> {meeting.Note}</p>");
//             }

//             sb.AppendLine("</div>");
//             sb.AppendLine("<div style='padding: 20px; font-size: 12px; color: #6c757d;'>");
//             sb.AppendLine("<p>This is an automated reminder. Good luck with your interview!</p>");
//             sb.AppendLine("</div>");
//             sb.AppendLine("</div>");
//             sb.AppendLine("</body>");
//             sb.AppendLine("</html>");

//             return sb.ToString();
//         }
//     }
// }
