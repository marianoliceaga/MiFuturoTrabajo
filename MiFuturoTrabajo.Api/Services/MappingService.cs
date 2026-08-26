using MiFuturoTrabajo.Infrastructure.Entities;
using MiFuturoTrabajo.Shared.Models;

namespace MiFuturoTrabajo.Api.Services
{
    /// <summary>
    /// Entity &lt;-&gt; DTO translation.
    ///
    /// The DTOs are the wire contract and are unchanged by the Cosmos
    /// redesign. What changed underneath: CV versions, job references and
    /// interview steps are now embedded owned types rather than rows in their
    /// own containers, notes/labels are native JSON arrays rather than
    /// serialised strings, and recruiters are referenced by id.
    ///
    /// Because Cosmos cannot join across containers, anything that needs a
    /// recruiter or task list is passed it explicitly by the caller, which
    /// loads that set once per request with a single-partition query.
    /// </summary>
    public static class MappingService
    {
        private static string NewIdIfBlank(string? id) =>
            string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString() : id;

        /// <summary>
        /// Non-nullable string properties are required in the EF model, so a
        /// null from the wire would make SaveChanges throw. Optional fields
        /// legitimately arrive null, so they land as empty instead.
        /// </summary>
        private static string OrEmpty(string? value) => value ?? string.Empty;

        // ------------------------------------------------------------------
        // Applicant (embeds CV versions + past job references)
        // ------------------------------------------------------------------
        public static ApplicantDto ToDto(this Applicant entity) => new()
        {
            Id = entity.Id,
            Name = entity.Name,
            Lastname = entity.Lastname,
            CurrentJob = entity.CurrentJob,
            LinkedInUrl = entity.LinkedInUrl,
            ContactEmail = entity.ContactEmail,
            Cellphone = entity.Cellphone,
            CreatedOnDate = entity.CreatedOnDate,
            CVVersions = entity.CVVersions.Select(cv => cv.ToDto()).ToList(),
            PastJobReferences = entity.PastJobReferences.Select(jr => jr.ToDto()).ToList()
        };

        public static Applicant ToEntity(this ApplicantDto dto, string userId) => new()
        {
            Id = NewIdIfBlank(dto.Id),
            UserId = userId,
            Name = OrEmpty(dto.Name),
            Lastname = OrEmpty(dto.Lastname),
            CurrentJob = OrEmpty(dto.CurrentJob),
            LinkedInUrl = OrEmpty(dto.LinkedInUrl),
            ContactEmail = OrEmpty(dto.ContactEmail),
            Cellphone = OrEmpty(dto.Cellphone),
            CreatedOnDate = dto.CreatedOnDate == default ? DateTime.UtcNow : dto.CreatedOnDate,
            CVVersions = (dto.CVVersions ?? new()).Select(cv => cv.ToEntity()).ToList(),
            PastJobReferences = (dto.PastJobReferences ?? new()).Select(jr => jr.ToEntity()).ToList()
        };

        // ------------------------------------------------------------------
        // CV version (owned)
        // ------------------------------------------------------------------
        public static CVVersionDto ToDto(this CVVersion entity) => new()
        {
            Id = entity.Id,
            Name = entity.Name,
            FileUrl = entity.FileUrl,
            UploadedDate = entity.UploadedDate,
            IsDefault = entity.IsDefault
        };

        public static CVVersion ToEntity(this CVVersionDto dto) => new()
        {
            Id = NewIdIfBlank(dto.Id),
            Name = OrEmpty(dto.Name),
            FileUrl = OrEmpty(dto.FileUrl),
            UploadedDate = dto.UploadedDate == default ? DateTime.UtcNow : dto.UploadedDate,
            IsDefault = dto.IsDefault
        };

        // ------------------------------------------------------------------
        // Job reference (owned)
        // ------------------------------------------------------------------
        public static JobReferenceDto ToDto(this JobReference entity) => new()
        {
            Id = entity.Id,
            CompanyName = entity.CompanyName,
            JobTitle = entity.JobTitle,
            ContactName = entity.ContactName,
            ContactEmail = entity.ContactEmail,
            ContactPhone = entity.ContactPhone,
            StartDate = entity.StartDate,
            EndDate = entity.EndDate
        };

        public static JobReference ToEntity(this JobReferenceDto dto) => new()
        {
            Id = NewIdIfBlank(dto.Id),
            CompanyName = OrEmpty(dto.CompanyName),
            JobTitle = OrEmpty(dto.JobTitle),
            ContactName = OrEmpty(dto.ContactName),
            ContactEmail = OrEmpty(dto.ContactEmail),
            ContactPhone = OrEmpty(dto.ContactPhone),
            StartDate = dto.StartDate,
            EndDate = dto.EndDate
        };

        // ------------------------------------------------------------------
        // Company
        // ------------------------------------------------------------------
        public static CompanyDto ToDto(this Company entity) => new()
        {
            Id = entity.Id,
            Name = entity.Name,
            Description = entity.Description,
            Website = entity.Website,
            Telephone = entity.Telephone,
            Address = entity.Address,
            Country = entity.Country,
            Labels = new List<string>(entity.Labels)
        };

        public static Company ToEntity(this CompanyDto dto, string userId) => new()
        {
            Id = NewIdIfBlank(dto.Id),
            UserId = userId,
            Name = OrEmpty(dto.Name),
            Description = OrEmpty(dto.Description),
            Website = OrEmpty(dto.Website),
            Telephone = OrEmpty(dto.Telephone),
            Address = OrEmpty(dto.Address),
            Country = OrEmpty(dto.Country),
            Labels = new List<string>(dto.Labels ?? new())
        };

        // ------------------------------------------------------------------
        // Recruiter
        // ------------------------------------------------------------------
        public static RecruiterDto ToDto(this Recruiter entity) => new()
        {
            Id = entity.Id,
            Name = entity.Name,
            Lastname = entity.Lastname,
            ContactEmail = entity.ContactEmail,
            Cellphone = entity.Cellphone,
            Landline = entity.Landline,
            LinkedInUrl = entity.LinkedInUrl,
            IsFromConsultancy = entity.IsFromConsultancy,
            CompanyName = entity.CompanyName
        };

        public static Recruiter ToEntity(this RecruiterDto dto, string userId) => new()
        {
            Id = NewIdIfBlank(dto.Id),
            UserId = userId,
            Name = OrEmpty(dto.Name),
            Lastname = OrEmpty(dto.Lastname),
            ContactEmail = OrEmpty(dto.ContactEmail),
            Cellphone = OrEmpty(dto.Cellphone),
            Landline = OrEmpty(dto.Landline),
            LinkedInUrl = OrEmpty(dto.LinkedInUrl),
            IsFromConsultancy = dto.IsFromConsultancy,
            CompanyName = dto.CompanyName
        };

        // ------------------------------------------------------------------
        // Interview step (owned)
        // ------------------------------------------------------------------
        public static InterviewStepDto ToDto(this InterviewStep entity) => new()
        {
            Id = entity.Id,
            StepName = entity.StepName,
            StepOrder = entity.StepOrder,
            ScheduledDate = entity.ScheduledDate,
            CompletedDate = entity.CompletedDate,
            IsCompleted = entity.IsCompleted,
            Notes = entity.Notes,
            MeetingId = entity.MeetingId
        };

        public static InterviewStep ToEntity(this InterviewStepDto dto) => new()
        {
            Id = NewIdIfBlank(dto.Id),
            StepName = OrEmpty(dto.StepName),
            StepOrder = dto.StepOrder,
            ScheduledDate = dto.ScheduledDate,
            CompletedDate = dto.CompletedDate,
            IsCompleted = dto.IsCompleted,
            Notes = OrEmpty(dto.Notes),
            MeetingId = dto.MeetingId
        };

        // ------------------------------------------------------------------
        // Applicant task
        // ------------------------------------------------------------------
        public static ApplicantTaskDto ToDto(this ApplicantTask entity) => new()
        {
            Id = entity.Id,
            ApplicantId = entity.ApplicantId,
            JobApplicationId = entity.JobApplicationId,
            Type = entity.Type,
            Title = entity.Title,
            Note = entity.Note,
            StartDate = entity.StartDate,
            Deadline = entity.Deadline,
            Completed = entity.Completed
        };

        public static ApplicantTask ToEntity(this ApplicantTaskDto dto, string applicantId) => new()
        {
            Id = NewIdIfBlank(dto.Id),
            ApplicantId = applicantId,
            JobApplicationId = OrEmpty(dto.JobApplicationId),
            Type = dto.Type,
            Title = OrEmpty(dto.Title),
            Note = OrEmpty(dto.Note),
            StartDate = dto.StartDate,
            Deadline = dto.Deadline,
            Completed = dto.Completed,
            ReminderSent = false
        };

        // ------------------------------------------------------------------
        // Meeting (recruiters referenced by id, resolved by the caller)
        // ------------------------------------------------------------------
        public static MeetingDto ToDto(this Meeting entity, IReadOnlyCollection<Recruiter>? recruiters = null) => new()
        {
            Id = entity.Id,
            ApplicantId = entity.ApplicantId,
            JobApplicationId = entity.JobApplicationId,
            Title = entity.Title,
            Description = entity.Description,
            StartDateTime = entity.StartDateTime,
            EndDateTime = entity.EndDateTime,
            IsPhonecall = entity.IsPhonecall,
            OnlineMeetingURL = entity.OnlineMeetingURL,
            PhysicalLocation = entity.PhysicalLocation,
            MeetingAccessCode = entity.MeetingAccessCode,
            Outcome = entity.Outcome,
            Note = entity.Note,
            SendReminderEmail = entity.SendReminderEmail,
            ReminderMinutesBefore = entity.ReminderMinutesBefore,
            Recruiters = ResolveRecruiters(entity.RecruiterIds, recruiters)
        };

        public static Meeting ToEntity(this MeetingDto dto, string applicantId) => new()
        {
            Id = NewIdIfBlank(dto.Id),
            ApplicantId = applicantId,
            JobApplicationId = OrEmpty(dto.JobApplicationId),
            Title = OrEmpty(dto.Title),
            Description = OrEmpty(dto.Description),
            StartDateTime = dto.StartDateTime,
            EndDateTime = dto.EndDateTime,
            IsPhonecall = dto.IsPhonecall,
            OnlineMeetingURL = OrEmpty(dto.OnlineMeetingURL),
            PhysicalLocation = OrEmpty(dto.PhysicalLocation),
            MeetingAccessCode = OrEmpty(dto.MeetingAccessCode),
            Outcome = OrEmpty(dto.Outcome),
            Note = OrEmpty(dto.Note),
            SendReminderEmail = dto.SendReminderEmail,
            ReminderMinutesBefore = dto.ReminderMinutesBefore,
            ReminderSent = false,
            RecruiterIds = (dto.Recruiters ?? new()).Select(r => r.Id).Where(id => !string.IsNullOrWhiteSpace(id)).ToList()
        };

        // ------------------------------------------------------------------
        // Job application (embeds interview steps + company snapshot)
        // ------------------------------------------------------------------
        public static JobApplicationDto ToDto(
            this JobApplication entity,
            IReadOnlyCollection<Recruiter>? recruiters = null,
            IReadOnlyCollection<ApplicantTask>? tasks = null) => new()
        {
            Id = entity.Id,
            ApplicantId = entity.ApplicantId,
            JobTitle = entity.JobTitle,
            Source = entity.Source,
            URL = entity.URL,
            JobDescription = entity.JobDescription,
            Salary = entity.Salary,
            Location = entity.Location,
            IsDirect = entity.IsDirect,
            IsConsultancy = entity.IsConsultancy,
            Status = entity.Status,
            DateApplied = entity.DateApplied,
            MainRecruiterContactId = entity.MainRecruiterContactId,
            DeadLine = entity.DeadLine,
            JobOffer = entity.JobOffer,
            CVUsedId = entity.CVUsedId,
            CreatedOnDate = entity.CreatedOnDate,
            IsDeleted = entity.IsDeleted,
            Notes = new List<string>(entity.Notes),
            Labels = new List<string>(entity.Labels),
            InterviewSteps = entity.InterviewSteps
                .OrderBy(s => s.StepOrder)
                .Select(s => s.ToDto())
                .ToList(),
            // The snapshot is what makes the board a single query: the company
            // name travels inside the application document.
            Company = entity.Company is null
                ? new CompanyDto { Id = entity.CompanyId }
                : new CompanyDto { Id = entity.Company.Id, Name = entity.Company.Name },
            Recruiters = ResolveRecruiters(entity.RecruiterIds, recruiters),
            ApplicantTasks = tasks is null
                ? new List<ApplicantTaskDto>()
                : tasks.Where(t => t.JobApplicationId == entity.Id).Select(t => t.ToDto()).ToList()
        };

        public static JobApplication ToEntity(this JobApplicationDto dto, string applicantId) => new()
        {
            Id = NewIdIfBlank(dto.Id),
            ApplicantId = applicantId,
            CompanyId = dto.Company?.Id ?? string.Empty,
            Company = dto.Company is null
                ? null
                : new CompanySnapshot { Id = dto.Company.Id, Name = dto.Company.Name },
            JobTitle = OrEmpty(dto.JobTitle),
            Source = dto.Source,
            URL = OrEmpty(dto.URL),
            JobDescription = OrEmpty(dto.JobDescription),
            Salary = dto.Salary,
            Location = OrEmpty(dto.Location),
            IsDirect = dto.IsDirect,
            IsConsultancy = dto.IsConsultancy,
            Status = dto.Status,
            DateApplied = dto.DateApplied,
            MainRecruiterContactId = OrEmpty(dto.MainRecruiterContactId),
            DeadLine = dto.DeadLine,
            JobOffer = OrEmpty(dto.JobOffer),
            CVUsedId = OrEmpty(dto.CVUsedId),
            CreatedOnDate = dto.CreatedOnDate == default ? DateTime.UtcNow : dto.CreatedOnDate,
            IsDeleted = dto.IsDeleted,
            Notes = new List<string>(dto.Notes ?? new()),
            Labels = new List<string>(dto.Labels ?? new()),
            RecruiterIds = (dto.Recruiters ?? new()).Select(r => r.Id).Where(id => !string.IsNullOrWhiteSpace(id)).ToList(),
            InterviewSteps = (dto.InterviewSteps ?? new()).Select(s => s.ToEntity()).ToList()
        };

        // ------------------------------------------------------------------
        private static List<RecruiterDto> ResolveRecruiters(
            List<string> ids, IReadOnlyCollection<Recruiter>? pool)
        {
            if (pool is null || ids.Count == 0)
            {
                return new List<RecruiterDto>();
            }

            return pool.Where(r => ids.Contains(r.Id)).Select(r => r.ToDto()).ToList();
        }
    }
}
