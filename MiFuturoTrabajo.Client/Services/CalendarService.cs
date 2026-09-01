using Microsoft.Extensions.Localization;
using MiFuturoTrabajo.Client.Resources;
using MiFuturoTrabajo.Shared.Models;

namespace MiFuturoTrabajo.Client.Services
{
    /// <summary>
    /// Folds applications, meetings and tasks into one list of dated events.
    ///
    /// The event titles are composed here rather than in the page, so this
    /// service needs the string table too - the alternative would be returning
    /// English fragments for a Razor file to translate back, which is how
    /// half-translated screens happen.
    /// </summary>
    public class CalendarService : ICalendarService
    {
        private readonly IJobApplicationService _jobApplicationService;
        private readonly IMeetingService _meetingService;
        private readonly IApplicantTaskService _taskService;
        private readonly IStringLocalizer<Strings> _localizer;

        public CalendarService(
            IJobApplicationService jobApplicationService,
            IMeetingService meetingService,
            IApplicantTaskService taskService,
            IStringLocalizer<Strings> localizer)
        {
            _jobApplicationService = jobApplicationService;
            _meetingService = meetingService;
            _taskService = taskService;
            _localizer = localizer;
        }

        public async Task<List<CalendarEvent>> GetEventsAsync()
        {
            var events = new List<CalendarEvent>();

            try
            {
                // Fetch all data in parallel
                var applicationsTask = _jobApplicationService.GetAllAsync();
                var meetingsTask = _meetingService.GetAllAsync();
                var tasksTask = _taskService.GetAllAsync();

                await Task.WhenAll(applicationsTask, meetingsTask, tasksTask);

                var applications = applicationsTask.Result;
                var meetings = meetingsTask.Result;
                var tasks = tasksTask.Result;

                // Process Applications (Deadlines and Interview Steps)
                foreach (var app in applications)
                {
                    // Application Deadline
                    if (app.DeadLine != DateTime.MinValue && app.Status != JobApplicationStatus.Completed && app.Status != JobApplicationStatus.Rejected)
                    {
                        events.Add(new CalendarEvent
                        {
                            Id = $"app_deadline_{app.Id}",
                            Title = _localizer["Calendar_DeadlineTitle", app.JobTitle, app.Company?.Name ?? string.Empty],
                            Description = _localizer["Calendar_DeadlineDescription", app.JobTitle],
                            Start = app.DeadLine,
                            End = app.DeadLine,
                            Type = CalendarEventType.ApplicationDeadline,
                            RelatedEntityId = app.Id,
                            ColorClass = "bg-danger",
                            Url = $"/applications/{app.Id}"
                        });
                    }

                    // Interview Steps
                    foreach (var step in app.InterviewSteps)
                    {
                        if (step.ScheduledDate.HasValue)
                        {
                            events.Add(new CalendarEvent
                            {
                                Id = $"interview_{step.Id}",
                                Title = _localizer["Calendar_InterviewStepTitle", step.StepName, app.Company?.Name ?? string.Empty],
                                Description = _localizer["Calendar_InterviewStepDescription", step.StepName, app.JobTitle],
                                Start = step.ScheduledDate.Value,
                                End = step.ScheduledDate.Value.AddHours(1), // Default duration 1h
                                Type = CalendarEventType.InterviewStep,
                                RelatedEntityId = app.Id,
                                ColorClass = step.IsCompleted ? "bg-secondary" : "bg-warning text-dark",
                                Url = $"/applications/{app.Id}",
                                IsCompleted = step.IsCompleted
                            });
                        }
                    }
                }

                // Process Meetings
                foreach (var meeting in meetings)
                {
                    events.Add(new CalendarEvent
                    {
                        Id = $"meeting_{meeting.Id}",
                        Title = _localizer["Calendar_MeetingTitle", meeting.Title],
                        Description = meeting.Description,
                        Start = meeting.StartDateTime,
                        End = meeting.EndDateTime,
                        Type = CalendarEventType.Meeting,
                        RelatedEntityId = meeting.Id,
                        ColorClass = "bg-info text-dark",
                        Url = "/meetings"
                    });
                }

                // Process Tasks
                foreach (var task in tasks)
                {
                    if (task.Deadline != DateTime.MinValue)
                    {
                        // A task with no title of its own falls back to its
                        // type, which is an enum and so needs looking up rather
                        // than calling ToString().
                        var label = string.IsNullOrWhiteSpace(task.Title)
                            ? _localizer.Enum(task.Type)
                            : task.Title;

                        events.Add(new CalendarEvent
                        {
                            Id = $"task_{task.Id}",
                            Title = _localizer["Calendar_TaskTitle", label],
                            Description = task.Note,
                            Start = task.Deadline,
                            End = task.Deadline,
                            Type = CalendarEventType.Task,
                            RelatedEntityId = task.Id,
                            ColorClass = task.Completed ? "bg-success" : "bg-primary",
                            Url = "/tasks",
                            IsCompleted = task.Completed
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching calendar events: {ex.Message}");
            }

            return events;
        }
    }
}
