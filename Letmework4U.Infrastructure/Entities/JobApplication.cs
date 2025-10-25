using Letmework4U.Shared.Models;

namespace Letmework4U.Infrastructure.Entities
{
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
        public string NotesJson { get; set; } = "[]"; // Store as JSON string
        public string LabelsJson { get; set; } = "[]"; // Store as JSON string
        public DateTime DeadLine { get; set; }
        public string JobOffer { get; set; } = string.Empty;
        public string CVUsedId { get; set; } = string.Empty;
        public DateTime CreatedOnDate { get; set; } = DateTime.UtcNow;
        public bool IsDeleted { get; set; }

        // Navigation properties
        public Applicant Applicant { get; set; } = null!;
        public Company Company { get; set; } = null!;
        public ICollection<JobApplicationRecruiter> JobApplicationRecruiters { get; set; } = new List<JobApplicationRecruiter>();
        public ICollection<ApplicantTask> ApplicantTasks { get; set; } = new List<ApplicantTask>();
        public ICollection<InterviewStep> InterviewSteps { get; set; } = new List<InterviewStep>();
        public ICollection<Meeting> Meetings { get; set; } = new List<Meeting>();
    }
}
