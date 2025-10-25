using Letmework4U.Shared.Models;

namespace Letmework4U.Infrastructure.Entities
{
    public class Applicant
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string UserId { get; set; } // Azure AD B2C User ID
        public string Name { get; set; } = string.Empty;
        public string Lastname { get; set; } = string.Empty;
        public string CurrentJob { get; set; } = string.Empty;
        public string LinkedInUrl { get; set; } = string.Empty;
        public string ContactEmail { get; set; } = string.Empty;
        public string Cellphone { get; set; } = string.Empty;
        public DateTime CreatedOnDate { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public ICollection<CVVersion> CVVersions { get; set; } = new List<CVVersion>();
        public ICollection<JobReference> PastJobReferences { get; set; } = new List<JobReference>();
        public ICollection<JobApplication> JobApplications { get; set; } = new List<JobApplication>();
        public ICollection<Meeting> Meetings { get; set; } = new List<Meeting>();
        public ICollection<ApplicantTask> Tasks { get; set; } = new List<ApplicantTask>();
    }
}
