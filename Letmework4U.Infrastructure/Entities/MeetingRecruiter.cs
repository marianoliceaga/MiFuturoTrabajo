namespace Letmework4U.Infrastructure.Entities
{
    // Join table for many-to-many relationship between Meetings and Recruiters
    public class MeetingRecruiter
    {
        public string MeetingId { get; set; } = string.Empty;
        public string RecruiterId { get; set; } = string.Empty;

        // Navigation properties
        public Meeting Meeting { get; set; } = null!;
        public Recruiter Recruiter { get; set; } = null!;
    }
}
