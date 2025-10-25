namespace Letmework4U.Infrastructure.Entities
{
    public class InterviewStep
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string JobApplicationId { get; set; } = string.Empty;
        public string StepName { get; set; } = string.Empty;
        public int StepOrder { get; set; }
        public DateTime? ScheduledDate { get; set; }
        public DateTime? CompletedDate { get; set; }
        public bool IsCompleted { get; set; }
        public string Notes { get; set; } = string.Empty;
        public string MeetingId { get; set; } = string.Empty;

        // Navigation property
        public JobApplication JobApplication { get; set; } = null!;
    }
}
