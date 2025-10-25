namespace Letmework4U.Infrastructure.Entities
{
    // Join table for many-to-many relationship between JobApplications and Recruiters
    public class JobApplicationRecruiter
    {
        public string JobApplicationId { get; set; } = string.Empty;
        public string RecruiterId { get; set; } = string.Empty;

        // Navigation properties
        public JobApplication JobApplication { get; set; } = null!;
        public Recruiter Recruiter { get; set; } = null!;
    }
}
