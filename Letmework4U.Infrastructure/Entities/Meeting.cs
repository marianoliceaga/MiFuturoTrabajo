namespace Letmework4U.Infrastructure.Entities
{
    public class Meeting
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ApplicantId { get; set; } = string.Empty;
        public string JobApplicationId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime StartDateTime { get; set; }
        public DateTime EndDateTime { get; set; }
        public bool IsPhonecall { get; set; }
        public string OnlineMeetingURL { get; set; } = string.Empty;
        public string PhysicalLocation { get; set; } = string.Empty;
        public string MeetingAccessCode { get; set; } = string.Empty;
        public string Outcome { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
        public bool SendReminderEmail { get; set; }
        public int ReminderMinutesBefore { get; set; } = 60;
        public bool ReminderSent { get; set; } // Track if reminder has been sent

        // Navigation properties
        public Applicant Applicant { get; set; } = null!;
        public JobApplication JobApplication { get; set; } = null!;
        public ICollection<MeetingRecruiter> MeetingRecruiters { get; set; } = new List<MeetingRecruiter>();
    }
}
