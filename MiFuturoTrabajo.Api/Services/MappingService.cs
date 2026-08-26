using MiFuturoTrabajo.Infrastructure.Entities;
using MiFuturoTrabajo.Shared.Models;
using Newtonsoft.Json;

namespace MiFuturoTrabajo.Api.Services
{
    public static class MappingService
    {
        // Applicant mappings
        public static ApplicantDto ToDto(this Applicant entity)
        {
            return new ApplicantDto
            {
                Id = entity.Id,
                Name = entity.Name,
                Lastname = entity.Lastname,
                CurrentJob = entity.CurrentJob,
                LinkedInUrl = entity.LinkedInUrl,
                ContactEmail = entity.ContactEmail,
                Cellphone = entity.Cellphone,
                CreatedOnDate = entity.CreatedOnDate,
                CVVersions = entity.CVVersions?.Select(cv => cv.ToDto()).ToList() ?? new(),
                PastJobReferences = entity.PastJobReferences?.Select(jr => jr.ToDto()).ToList() ?? new()
            };
        }

        public static Applicant ToEntity(this ApplicantDto dto, string userId)
        {
            return new Applicant
            {
                Id = string.IsNullOrWhiteSpace(dto.Id) ? Guid.NewGuid().ToString() : dto.Id,
                UserId = userId,
                Name = dto.Name ?? string.Empty,
                Lastname = dto.Lastname ?? string.Empty,
                CurrentJob = dto.CurrentJob ?? string.Empty,
                LinkedInUrl = dto.LinkedInUrl ?? string.Empty,
                ContactEmail = dto.ContactEmail ?? string.Empty,
                Cellphone = dto.Cellphone ?? string.Empty,
                CreatedOnDate = dto.CreatedOnDate
            };
        }

        // CVVersion mappings
        public static CVVersionDto ToDto(this CVVersion entity)
        {
            return new CVVersionDto
            {
                Id = entity.Id,
                Name = entity.Name,
                FileUrl = entity.FileUrl,
                UploadedDate = entity.UploadedDate,
                IsDefault = entity.IsDefault
            };
        }

        public static CVVersion ToEntity(this CVVersionDto dto, string applicantId)
        {
            return new CVVersion
            {
                Id = string.IsNullOrWhiteSpace(dto.Id) ? Guid.NewGuid().ToString() : dto.Id,
                ApplicantId = applicantId,
                Name = dto.Name ?? string.Empty,
                FileUrl = dto.FileUrl ?? string.Empty,
                UploadedDate = dto.UploadedDate,
                IsDefault = dto.IsDefault
            };
        }

        // JobReference mappings
        public static JobReferenceDto ToDto(this JobReference entity)
        {
            return new JobReferenceDto
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
        }

        public static JobReference ToEntity(this JobReferenceDto dto, string applicantId)
        {
            return new JobReference
            {
                Id = string.IsNullOrWhiteSpace(dto.Id) ? Guid.NewGuid().ToString() : dto.Id,
                ApplicantId = applicantId,
                CompanyName = dto.CompanyName ?? string.Empty,
                JobTitle = dto.JobTitle ?? string.Empty,
                ContactName = dto.ContactName ?? string.Empty,
                ContactEmail = dto.ContactEmail ?? string.Empty,
                ContactPhone = dto.ContactPhone ?? string.Empty,
                StartDate = dto.StartDate,
                EndDate = dto.EndDate
            };
        }

        // Company mappings
        public static CompanyDto ToDto(this Company entity)
        {
            return new CompanyDto
            {
                Id = entity.Id,
                Name = entity.Name,
                Description = entity.Description,
                Website = entity.Website,
                Telephone = entity.Telephone,
                Address = entity.Address,
                Country = entity.Country,
                Labels = JsonConvert.DeserializeObject<List<string>>(entity.LabelsJson) ?? new()
            };
        }

        public static Company ToEntity(this CompanyDto dto, string userId)
        {
            return new Company
            {
                Id = string.IsNullOrWhiteSpace(dto.Id) ? Guid.NewGuid().ToString() : dto.Id,
                UserId = userId,
                Name = dto.Name ?? string.Empty,
                Description = dto.Description ?? string.Empty,
                Website = dto.Website ?? string.Empty,
                Telephone = dto.Telephone ?? string.Empty,
                Address = dto.Address ?? string.Empty,
                Country = dto.Country ?? string.Empty,
                LabelsJson = JsonConvert.SerializeObject(dto.Labels ?? new List<string>())
            };
        }

        // Recruiter mappings
        public static RecruiterDto ToDto(this Recruiter entity)
        {
            return new RecruiterDto
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
        }

        public static Recruiter ToEntity(this RecruiterDto dto, string userId)
        {
            return new Recruiter
            {
                Id = string.IsNullOrWhiteSpace(dto.Id) ? Guid.NewGuid().ToString() : dto.Id,
                UserId = userId,
                Name = dto.Name ?? string.Empty,
                Lastname = dto.Lastname ?? string.Empty,
                ContactEmail = dto.ContactEmail ?? string.Empty,
                Cellphone = dto.Cellphone ?? string.Empty,
                Landline = dto.Landline ?? string.Empty,
                LinkedInUrl = dto.LinkedInUrl ?? string.Empty,
                IsFromConsultancy = dto.IsFromConsultancy,
                CompanyName = dto.CompanyName ?? string.Empty
            };
        }

        // InterviewStep mappings
        public static InterviewStepDto ToDto(this InterviewStep entity)
        {
            return new InterviewStepDto
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
        }

        public static InterviewStep ToEntity(this InterviewStepDto dto, string jobApplicationId)
        {
            return new InterviewStep
            {
                Id = string.IsNullOrWhiteSpace(dto.Id) ? Guid.NewGuid().ToString() : dto.Id,
                JobApplicationId = jobApplicationId,
                StepName = dto.StepName ?? string.Empty,
                StepOrder = dto.StepOrder,
                ScheduledDate = dto.ScheduledDate,
                CompletedDate = dto.CompletedDate,
                IsCompleted = dto.IsCompleted,
                Notes = dto.Notes ?? string.Empty,
                MeetingId = dto.MeetingId ?? string.Empty
            };
        }

        // ApplicantTask mappings
        public static ApplicantTaskDto ToDto(this ApplicantTask entity)
        {
            return new ApplicantTaskDto
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
        }

        public static ApplicantTask ToEntity(this ApplicantTaskDto dto, string applicantId)
        {
            return new ApplicantTask
            {
                Id = string.IsNullOrWhiteSpace(dto.Id) ? Guid.NewGuid().ToString() : dto.Id,
                ApplicantId = applicantId,
                JobApplicationId = dto.JobApplicationId ?? string.Empty,
                Type = dto.Type,
                Title = dto.Title ?? string.Empty,
                Note = dto.Note ?? string.Empty,
                StartDate = dto.StartDate,
                Deadline = dto.Deadline,
                Completed = dto.Completed,
                ReminderSent = false
            };
        }

        // Meeting mappings
        public static MeetingDto ToDto(this Meeting entity, List<Recruiter>? recruiters = null)
        {
            return new MeetingDto
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
                Recruiters = recruiters?.Select(r => r.ToDto()).ToList() ?? new()
            };
        }

        public static Meeting ToEntity(this MeetingDto dto, string applicantId)
        {
            return new Meeting
            {
                Id = string.IsNullOrWhiteSpace(dto.Id) ? Guid.NewGuid().ToString() : dto.Id,
                ApplicantId = applicantId,
                JobApplicationId = dto.JobApplicationId ?? string.Empty,
                Title = dto.Title ?? string.Empty,
                Description = dto.Description ?? string.Empty,
                StartDateTime = dto.StartDateTime,
                EndDateTime = dto.EndDateTime,
                IsPhonecall = dto.IsPhonecall,
                OnlineMeetingURL = dto.OnlineMeetingURL ?? string.Empty,
                PhysicalLocation = dto.PhysicalLocation ?? string.Empty,
                MeetingAccessCode = dto.MeetingAccessCode ?? string.Empty,
                Outcome = dto.Outcome ?? string.Empty,
                Note = dto.Note ?? string.Empty,
                SendReminderEmail = dto.SendReminderEmail,
                ReminderMinutesBefore = dto.ReminderMinutesBefore,
                ReminderSent = false
            };
        }

        // JobApplication mappings
        public static JobApplicationDto ToDto(this JobApplication entity, Company? company = null, List<Recruiter>? recruiters = null)
        {
            return new JobApplicationDto
            {
                Id = entity.Id,
                ApplicantId = entity.ApplicantId,
                Company = company?.ToDto(),
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
                Notes = JsonConvert.DeserializeObject<List<string>>(entity.NotesJson) ?? new(),
                Labels = JsonConvert.DeserializeObject<List<string>>(entity.LabelsJson) ?? new(),
                DeadLine = entity.DeadLine,
                JobOffer = entity.JobOffer,
                CVUsedId = entity.CVUsedId,
                CreatedOnDate = entity.CreatedOnDate,
                IsDeleted = entity.IsDeleted,
                Recruiters = recruiters?.Select(r => r.ToDto()).ToList() ?? new(),
                InterviewSteps = entity.InterviewSteps?.Select(s => s.ToDto()).ToList() ?? new(),
                ApplicantTasks = entity.ApplicantTasks?.Select(t => t.ToDto()).ToList() ?? new()
            };
        }

        public static JobApplication ToEntity(this JobApplicationDto dto, string applicantId)
        {
            return new JobApplication
            {
                Id = string.IsNullOrWhiteSpace(dto.Id) ? Guid.NewGuid().ToString() : dto.Id,
                ApplicantId = applicantId,
                CompanyId = dto.Company?.Id ?? string.Empty,
                JobTitle = dto.JobTitle ?? string.Empty,
                Source = dto.Source,
                URL = dto.URL ?? string.Empty,
                JobDescription = dto.JobDescription ?? string.Empty,
                Salary = dto.Salary,
                Location = dto.Location ?? string.Empty,
                IsDirect = dto.IsDirect,
                IsConsultancy = dto.IsConsultancy,
                Status = dto.Status,
                DateApplied = dto.DateApplied,
                MainRecruiterContactId = dto.MainRecruiterContactId ?? string.Empty,
                NotesJson = JsonConvert.SerializeObject(dto.Notes ?? new List<string>()),
                LabelsJson = JsonConvert.SerializeObject(dto.Labels ?? new List<string>()),
                DeadLine = dto.DeadLine,
                JobOffer = dto.JobOffer ?? string.Empty,
                CVUsedId = dto.CVUsedId ?? string.Empty,
                CreatedOnDate = dto.CreatedOnDate,
                IsDeleted = dto.IsDeleted
            };
        }
    }
}
