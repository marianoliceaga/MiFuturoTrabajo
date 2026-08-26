using MiFuturoTrabajo.Shared.Models;

namespace MiFuturoTrabajo.Infrastructure.Entities
{
    /// <summary>
    /// Aggregate root, container "ApplicantTasks", partition key /ApplicantId.
    /// Kept separate from JobApplication because tasks exist independently of
    /// an application and are queried standalone by the tasks page and the
    /// reminder job.
    /// </summary>
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
        public bool ReminderSent { get; set; }
    }
}
