namespace MiFuturoTrabajo.Shared.Models
{
    public enum JobApplicationStatus
    {
        NotApplied = 1,
        Applied = 2,
        FirstInterview = 3,
        Meetings = 4,
        Rejected = 5,
        Offer = 6,
        Completed = 7,
        NoResponse = 8
    }

    public enum TaskType
    {
        FollowUp = 1,
        ReachOut = 2,
        SendAvailability = 3,
        PrepareCoverLetter = 4,
        PrepareResume = 5,
        PrepareCVDetailed = 6,
        PrepareForInterview = 7,
        Apply = 8,
        CompleteExercise = 9,
        EnglishTest = 10,
        VideoIntroduction = 11
    }

    public enum JobSource
    {
        LinkedIn = 1,
        Indeed = 2,
        PersonalConnection = 3,
        CompanyWebsite = 4,
        Recruiter = 5,
        Referral = 6,
        Other = 7
    }

    public class ApplicantDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Lastname { get; set; } = string.Empty;
        public string CurrentJob { get; set; } = string.Empty;
        public string LinkedInUrl { get; set; } = string.Empty;
        public string ContactEmail { get; set; } = string.Empty;
        public string Cellphone { get; set; } = string.Empty;
        public List<CVVersionDto> CVVersions { get; set; } = new();
        public List<JobReferenceDto> PastJobReferences { get; set; } = new();
        public DateTime CreatedOnDate { get; set; }
    }

    public class CVVersionDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string FileUrl { get; set; } = string.Empty;
        public DateTime UploadedDate { get; set; }
        public bool IsDefault { get; set; }
    }

    public class JobReferenceDto
    {
        public string Id { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public string JobTitle { get; set; } = string.Empty;
        public string ContactName { get; set; } = string.Empty;
        public string ContactEmail { get; set; } = string.Empty;
        public string ContactPhone { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }
    }

    public class JobApplicationDto
    {
        public string Id { get; set; } = string.Empty;
        public CompanyDto Company { get; set; }
        public string JobTitle { get; set; } = string.Empty;
        public JobSource Source { get; set; }
        public string URL { get; set; } = string.Empty;
        public string JobDescription { get; set; } = string.Empty;
        public decimal Salary { get; set; }
        public string Location { get; set; } = string.Empty;
        public bool IsDirect { get; set; }
        public bool IsConsultancy { get; set; }
        public JobApplicationStatus Status { get; set; }
        public string ApplicantId { get; set; } = string.Empty;
        public DateTime DateApplied { get; set; }
        public List<RecruiterDto> Recruiters { get; set; } = new();
        public string MainRecruiterContactId { get; set; } = string.Empty;
        public MeetingDto FirstMeeting { get; set; }
        public List<string> Notes { get; set; } = new();
        public List<ApplicantTaskDto> ApplicantTasks { get; set; } = new();
        public List<string> Labels { get; set; } = new();
        public DateTime DeadLine { get; set; }
        public string JobOffer { get; set; } = string.Empty;
        public string CVUsedId { get; set; } = string.Empty;
        public List<InterviewStepDto> InterviewSteps { get; set; } = new();
        public DateTime CreatedOnDate { get; set; }
        public bool IsDeleted { get; set; }
    }

    public class InterviewStepDto
    {
        public string Id { get; set; } = string.Empty;
        public string StepName { get; set; } = string.Empty;
        public int StepOrder { get; set; }
        public DateTime? ScheduledDate { get; set; }
        public DateTime? CompletedDate { get; set; }
        public bool IsCompleted { get; set; }
        public string Notes { get; set; } = string.Empty;
        public string MeetingId { get; set; } = string.Empty;
    }

    public class CompanyDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Website { get; set; } = string.Empty;
        public string Telephone { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public List<string> Labels { get; set; } = new();
    }

    public class RecruiterDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Lastname { get; set; } = string.Empty;
        public string ContactEmail { get; set; } = string.Empty;
        public string Cellphone { get; set; } = string.Empty;
        public string Landline { get; set; } = string.Empty;
        public string LinkedInUrl { get; set; } = string.Empty;
        public bool IsFromConsultancy { get; set; }
        public string CompanyName { get; set; } = string.Empty;
    }

    public class MeetingDto
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime StartDateTime { get; set; }
        public DateTime EndDateTime { get; set; }
        public bool IsPhonecall { get; set; }
        public string OnlineMeetingURL { get; set; } = string.Empty;
        public string PhysicalLocation { get; set; } = string.Empty;
        public string MeetingAccessCode { get; set; } = string.Empty;
        public List<RecruiterDto> Recruiters { get; set; } = new();
        public string ApplicantId { get; set; } = string.Empty;
        public string JobApplicationId { get; set; } = string.Empty;
        public string Outcome { get; set; } = string.Empty;
        public List<MeetingDto> FollowUpMeetings { get; set; } = new();
        public string Note { get; set; } = string.Empty;
        public bool SendReminderEmail { get; set; }
        public int ReminderMinutesBefore { get; set; } = 60;
    }

    public class ApplicantTaskDto
    {
        public string Id { get; set; } = string.Empty;
        public string ApplicantId { get; set; } = string.Empty;
        public string JobApplicationId { get; set; } = string.Empty;
        public TaskType Type { get; set; }
        public string Note { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime Deadline { get; set; }
        public bool Completed { get; set; }
        public string Title { get; set; } = string.Empty;
    }

    public class BoardColumnDto
    {
        public JobApplicationStatus Status { get; set; }
        public string Title { get; set; } = string.Empty;
        public List<JobApplicationDto> Applications { get; set; } = new();
    }
}