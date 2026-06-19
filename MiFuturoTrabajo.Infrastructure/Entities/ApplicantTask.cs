using MiFuturoTrabajo.Shared.Models;

namespace MiFuturoTrabajo.Infrastructure.Entities
{
    public class ApplicantTask
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ApplicantId { get; set; } = string.Empty;
        public string JobApplicationId { get; set; } = string.Empty;
        public TaskType Type { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime Deadline { get; set; }
        public bool Completed { get; set; }
        public bool ReminderSent { get; set; } // Track if reminder has been sent

        // Navigation properties
        public Applicant Applicant { get; set; } = null!;
        public JobApplication JobApplication { get; set; } = null!;
    }
}
