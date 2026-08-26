using MiFuturoTrabajo.Shared.Models;

namespace MiFuturoTrabajo.Infrastructure.Entities
{
    /// <summary>
    /// Aggregate root, container "JobApplications", partition key /ApplicantId.
    /// Interview steps, recruiter links, notes, labels and the company snapshot
    /// are embedded, so rendering the board is a single-partition query.
    /// </summary>
    public class JobApplication
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ApplicantId { get; set; } = string.Empty;
        public string CompanyId { get; set; } = string.Empty;
        public string JobTitle { get; set; } = string.Empty;
        public JobSource Source { get; set; }
        public string URL { get; set; } = string.Empty;
        public string JobDescription { get; set; } = string.Empty;
        public decimal Salary { get; set; }
        public string Location { get; set; } = string.Empty;
        public bool IsDirect { get; set; }
        public bool IsConsultancy { get; set; }
        public JobApplicationStatus Status { get; set; } = JobApplicationStatus.NotApplied;
        public DateTime DateApplied { get; set; }
        public string MainRecruiterContactId { get; set; } = string.Empty;
        public string FirstMeetingId { get; set; } = string.Empty;
        public DateTime DeadLine { get; set; }
        public string JobOffer { get; set; } = string.Empty;
        public string CVUsedId { get; set; } = string.Empty;
        public DateTime CreatedOnDate { get; set; } = DateTime.UtcNow;
        public bool IsDeleted { get; set; }

        // Embedded. Cosmos stores these as native JSON arrays / objects, so the
        // old NotesJson / LabelsJson string round-tripping is gone.
        public List<string> Notes { get; set; } = new();
        public List<string> Labels { get; set; } = new();
        public List<string> RecruiterIds { get; set; } = new();
        public List<InterviewStep> InterviewSteps { get; set; } = new();
        public CompanySnapshot? Company { get; set; }
    }
}
