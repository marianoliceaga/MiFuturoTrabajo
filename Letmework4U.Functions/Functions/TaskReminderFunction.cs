// using Microsoft.Azure.Functions.Worker;
// using Microsoft.EntityFrameworkCore;
// using Microsoft.Extensions.Logging;
// using System.Text;
// using Letmework4U.Infrastructure;
// using Letmework4U.Functions.Services;

// namespace Letmework4U.Functions.Functions
// {
//     public class TaskReminderFunction
//     {
//         private readonly ILogger<TaskReminderFunction> _logger;
//         private readonly ApplicationDbContext _context;
//         private readonly IEmailService _emailService;

//         public TaskReminderFunction(
//             ILogger<TaskReminderFunction> logger,
//             ApplicationDbContext context,
//             IEmailService emailService)
//         {
//             _logger = logger;
//             _context = context;
//             _emailService = emailService;
//         }

//         [Function("TaskReminder")]
//         public async Task Run([TimerTrigger("0 0 8 * * *")] TimerInfo timer) // Daily at 8 AM UTC
//         {
//             _logger.LogInformation("Task reminder function executed at: {time}", DateTime.UtcNow);

//             try
//             {
//                 var today = DateTime.UtcNow.Date;
//                 var tomorrow = today.AddDays(1);

//                 // Find tasks due today that haven't been completed and haven't had reminders sent
//                 var tasksNeedingReminders = await _context.ApplicantTasks
//                     .Where(t => !t.ReminderSent &&
//                                 !t.Completed &&
//                                 t.Deadline >= today &&
//                                 t.Deadline < tomorrow)
//                     .Include(t => t.Applicant)
//                     .Include(t => t.JobApplication).ThenInclude(ja => ja.Company)
//                     .ToListAsync();

//                 _logger.LogInformation($"Found {tasksNeedingReminders.Count} tasks needing reminders");

//                 foreach (var task in tasksNeedingReminders)
//                 {
//                     try
//                     {
//                         var subject = $"Task Due Today: {task.Title}";
//                         var htmlContent = $@"
//                             <!DOCTYPE html>
//                             <html>
//                             <head>
//                                 <style>
//                                     body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
//                                     .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
//                                     .header {{ background-color: #dc3545; color: white; padding: 20px; text-align: center; border-radius: 5px 5px 0 0; }}
//                                     .content {{ background-color: #f8f9fa; padding: 20px; border-radius: 0 0 5px 5px; }}
//                                     .detail {{ margin: 10px 0; }}
//                                     .detail strong {{ display: inline-block; width: 150px; }}
//                                     .badge {{ display: inline-block; padding: 5px 10px; border-radius: 3px; background-color: #ffc107; color: #000; font-weight: bold; }}
//                                     .footer {{ margin-top: 20px; padding-top: 20px; border-top: 1px solid #dee2e6; font-size: 12px; color: #6c757d; }}
//                                 </style>
//                             </head>
//                             <body>
//                                 <div class=""container"">
//                                     <div class=""header"">
//                                         <h1>⏰ Task Due Today</h1>
//                                     </div>
//                                     <div class=""content"">
//                                         <div class=""detail""><strong>Title:</strong> {task.Title}</div>
//                                         <div class=""detail""><strong>Type:</strong> <span class=""badge"">{task.Type}</span></div>
//                                         <div class=""detail""><strong>Deadline:</strong> {task.Deadline:yyyy-MM-dd HH:mm} UTC</div>

//                                         {(task.JobApplication != null ? $@"
//                                         <hr style=""margin: 20px 0;""/>
//                                         <div class=""detail""><strong>Related to:</strong> {task.JobApplication.JobTitle}</div>
//                                         {(task.JobApplication.Company != null ? $"<div class=\"detail\"><strong>Company:</strong> {task.JobApplication.Company.Name}</div>" : "")}
//                                         " : "")}

//                                         {(!string.IsNullOrEmpty(task.Note) ?
//                                             $"<hr style=\"margin: 20px 0;\"/><div class=\"detail\"><strong>Notes:</strong> {task.Note}</div>" : "")}

//                                         <hr style=""margin: 20px 0;""/>
//                                         <div style=""background-color: #fff3cd; border-left: 4px solid #ffc107; padding: 15px; margin: 20px 0;"">
//                                             <strong>Reminder:</strong> This task is due today. Don't forget to complete it!
//                                         </div>
//                                     </div>
//                                     <div class=""footer"">
//                                         <p>This is an automated reminder from your job application tracker. Stay organized and good luck!</p>
//                                     </div>
//                                 </div>
//                             </body>
//                             </html>";

//                         await _emailService.SendEmailAsync(task.Applicant.ContactEmail, subject, htmlContent);

//                         task.ReminderSent = true;
//                         _logger.LogInformation($"Sent task reminder for task {task.Id} to {task.Applicant.ContactEmail}");
//                     }
//                     catch (Exception ex)
//                     {
//                         _logger.LogError(ex, $"Failed to send task reminder for task {task.Id}");
//                         // Continue with other tasks even if one fails
//                     }
//                 }

//                 if (tasksNeedingReminders.Any())
//                 {
//                     await _context.SaveChangesAsync();
//                 }

//                 _logger.LogInformation($"Task reminder function completed. Sent {tasksNeedingReminders.Count} reminders");
//             }
//             catch (Exception ex)
//             {
//                 _logger.LogError(ex, "Error in task reminder function");
//             }
//         }
//     }
// }
