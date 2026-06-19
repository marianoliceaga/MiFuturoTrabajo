using MiFuturoTrabajo.Shared.Models;

namespace MiFuturoTrabajo.Client.Services
{
    public class CalendarService : ICalendarService
    {
        private readonly IJobApplicationService _jobApplicationService;
        private readonly IMeetingService _meetingService;
        private readonly IApplicantTaskService _taskService;

        public CalendarService(
            IJobApplicationService jobApplicationService,
            IMeetingService meetingService,
            IApplicantTaskService taskService)
        {
            _jobApplicationService = jobApplicationService;
            _meetingService = meetingService;
            _taskService = taskService;
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
                            Title = $"Deadline: {app.JobTitle} at {app.Company?.Name}",
                            Description = $"Application deadline for {app.JobTitle}",
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
                                Title = $"Interview: {step.StepName} - {app.Company?.Name}",
                                Description = $"{step.StepName} for {app.JobTitle}",
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
                        Title = $"Meeting: {meeting.Title}",
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
                        events.Add(new CalendarEvent
                        {
                            Id = $"task_{task.Id}",
                            Title = $"Task: {task.Title ?? task.Type.ToString()}",
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
