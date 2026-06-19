using System;

namespace MiFuturoTrabajo.Shared.Models
{
    public enum CalendarEventType
    {
        Task,
        Meeting,
        InterviewStep,
        ApplicationDeadline
    }

    public class CalendarEvent
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public CalendarEventType Type { get; set; }
        public string RelatedEntityId { get; set; } = string.Empty;
        public string ColorClass { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public bool IsCompleted { get; set; }
    }
}
