namespace MiFuturoTrabajo.Infrastructure.Entities
{
    /// <summary>Owned type, embedded in the JobApplication document.</summary>
    public class InterviewStep
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string StepName { get; set; } = string.Empty;
        public int StepOrder { get; set; }
        public DateTime? ScheduledDate { get; set; }
        public DateTime? CompletedDate { get; set; }
        public bool IsCompleted { get; set; }
        public string Notes { get; set; } = string.Empty;
        public string MeetingId { get; set; } = string.Empty;
    }
}
