namespace Letmework4U.Shared.Models
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
        public string Id { get; set; }
        public string Name { get; set; }
        public string Lastname { get; set; }
        public string CurrentJob { get; set; }
        public string LinkedInUrl { get; set; }
        public string ContactEmail { get; set; }
        public string Cellphone { get; set; }
        public List<CVVersionDto> CVVersions { get; set; } = new();
        public List<JobReferenceDto> PastJobReferences { get; set; } = new();
        public DateTime CreatedOnDate { get; set; }
    }

    public class CVVersionDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string FileUrl { get; set; }
        public DateTime UploadedDate { get; set; }
        public bool IsDefault { get; set; }
    }

    public class JobReferenceDto
    {
        public string Id { get; set; }
        public string CompanyName { get; set; }
        public string JobTitle { get; set; }
        public string ContactName { get; set; }
        public string ContactEmail { get; set; }
        public string ContactPhone { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }
    }

    public class JobApplicationDto
    {
        public string Id { get; set; }
        public CompanyDto Company { get; set; }
        public string JobTitle { get; set; }
        public JobSource Source { get; set; }
        public string URL { get; set; }
        public string JobDescription { get; set; }
        public decimal Salary { get; set; }
        public string Location { get; set; }
        public bool IsDirect { get; set; }
        public bool IsConsultancy { get; set; }
        public JobApplicationStatus Status { get; set; }
        public string ApplicantId { get; set; }
        public DateTime DateApplied { get; set; }
        public List<RecruiterDto> Recruiters { get; set; } = new();
        public string MainRecruiterContactId { get; set; }
        public MeetingDto FirstMeeting { get; set; }
        public List<string> Notes { get; set; } = new();
        public List<ApplicantTaskDto> ApplicantTasks { get; set; } = new();
        public List<string> Labels { get; set; } = new();
        public DateTime DeadLine { get; set; }
        public string JobOffer { get; set; }
        public string CVUsedId { get; set; }
        public List<InterviewStepDto> InterviewSteps { get; set; } = new();
        public DateTime CreatedOnDate { get; set; }
        public bool IsDeleted { get; set; }
    }

    public class InterviewStepDto
    {
        public string Id { get; set; }
        public string StepName { get; set; }
        public int StepOrder { get; set; }
        public DateTime? ScheduledDate { get; set; }
        public DateTime? CompletedDate { get; set; }
        public bool IsCompleted { get; set; }
        public string Notes { get; set; }
        public string MeetingId { get; set; }
    }

    public class CompanyDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Website { get; set; }
        public string Telephone { get; set; }
        public string Address { get; set; }
        public string Country { get; set; }
        public List<string> Labels { get; set; } = new();
    }

    public class RecruiterDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Lastname { get; set; }
        public string ContactEmail { get; set; }
        public string Cellphone { get; set; }
        public string Landline { get; set; }
        public string LinkedInUrl { get; set; }
        public bool IsFromConsultancy { get; set; }
        public string CompanyName { get; set; }
    }

    public class MeetingDto
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public DateTime StartDateTime { get; set; }
        public DateTime EndDateTime { get; set; }
        public bool IsPhonecall { get; set; }
        public string OnlineMeetingURL { get; set; }
        public string PhysicalLocation { get; set; }
        public string MeetingAccessCode { get; set; }
        public List<RecruiterDto> Recruiters { get; set; } = new();
        public string ApplicantId { get; set; }
        public string JobApplicationId { get; set; }
        public string Outcome { get; set; }
        public List<MeetingDto> FollowUpMeetings { get; set; } = new();
        public string Note { get; set; }
        public bool SendReminderEmail { get; set; }
        public int ReminderMinutesBefore { get; set; } = 60;
    }

    public class ApplicantTaskDto
    {
        public string Id { get; set; }
        public string ApplicantId { get; set; }
        public string JobApplicationId { get; set; }
        public TaskType Type { get; set; }
        public string Note { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime Deadline { get; set; }
        public bool Completed { get; set; }
        public string Title { get; set; }
    }

    public class BoardColumnDto
    {
        public JobApplicationStatus Status { get; set; }
        public string Title { get; set; }
        public List<JobApplicationDto> Applications { get; set; } = new();
    }
}