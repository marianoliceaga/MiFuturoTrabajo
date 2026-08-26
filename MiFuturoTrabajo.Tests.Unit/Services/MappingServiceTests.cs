using MiFuturoTrabajo.Api.Services;
using MiFuturoTrabajo.Infrastructure.Entities;

using MiFuturoTrabajo.Shared.Models;
using Newtonsoft.Json;
using Xunit;

namespace MiFuturoTrabajo.Tests.Unit.Services
{
    public class MappingServiceTests
    {
        [Fact]
        public void Applicant_ToDto_ShouldMapAllProperties()
        {
            // Arrange
            var entity = new Applicant
            {
                Id = Guid.NewGuid().ToString(),
                UserId = Guid.NewGuid().ToString(),
                Name = "John",
                Lastname = "Doe",
                CurrentJob = "Software Engineer",
                LinkedInUrl = "https://linkedin.com/in/johndoe",
                ContactEmail = "john@example.com",
                Cellphone = "1234567890",
                CreatedOnDate = DateTime.UtcNow,
                CVVersions = new List<CVVersion>(),
                PastJobReferences = new List<JobReference>()
            };

            // Act
            var dto = entity.ToDto();

            // Assert
            Assert.Equal(entity.Id, dto.Id);
            Assert.Equal(entity.Name, dto.Name);
            Assert.Equal(entity.Lastname, dto.Lastname);
            Assert.Equal(entity.CurrentJob, dto.CurrentJob);
            Assert.Equal(entity.LinkedInUrl, dto.LinkedInUrl);
            Assert.Equal(entity.ContactEmail, dto.ContactEmail);
            Assert.Equal(entity.Cellphone, dto.Cellphone);
            Assert.Equal(entity.CreatedOnDate, dto.CreatedOnDate);
            Assert.NotNull(dto.CVVersions);
            Assert.NotNull(dto.PastJobReferences);
        }

        [Fact]
        public void Applicant_ToEntity_ShouldMapAllProperties()
        {
            // Arrange
            var userId = Guid.NewGuid().ToString();
            var dto = new ApplicantDto
            {
                Id = Guid.NewGuid().ToString(),
                Name = "John",
                Lastname = "Doe",
                CurrentJob = "Software Engineer",
                LinkedInUrl = "https://linkedin.com/in/johndoe",
                ContactEmail = "john@example.com",
                Cellphone = "1234567890",
                CreatedOnDate = DateTime.UtcNow
            };

            // Act
            var entity = dto.ToEntity(userId);

            // Assert
            Assert.Equal(dto.Id, entity.Id);
            Assert.Equal(userId, entity.UserId);
            Assert.Equal(dto.Name, entity.Name);
            Assert.Equal(dto.Lastname, entity.Lastname);
            Assert.Equal(dto.CurrentJob, entity.CurrentJob);
            Assert.Equal(dto.LinkedInUrl, entity.LinkedInUrl);
            Assert.Equal(dto.ContactEmail, entity.ContactEmail);
            Assert.Equal(dto.Cellphone, entity.Cellphone);
        }

        [Fact]
        public void CVVersion_ToDto_ShouldMapAllProperties()
        {
            // Arrange
            var entity = new CVVersion
            {
                Id = Guid.NewGuid().ToString(),
                Name = "Software Engineer CV",
                FileUrl = "https://storage.example.com/cv.pdf",
                UploadedDate = DateTime.UtcNow,
                IsDefault = true
            };

            // Act
            var dto = entity.ToDto();

            // Assert
            Assert.Equal(entity.Id, dto.Id);
            Assert.Equal(entity.Name, dto.Name);
            Assert.Equal(entity.FileUrl, dto.FileUrl);
            Assert.Equal(entity.UploadedDate, dto.UploadedDate);
            Assert.Equal(entity.IsDefault, dto.IsDefault);
        }

        [Fact]
        public void Company_ToDto_ShouldMapAllPropertiesIncludingLabels()
        {
            // Arrange
            var labels = new List<string> { "Tech", "Startup" };
            var entity = new Company
            {
                Id = Guid.NewGuid().ToString(),
                UserId = Guid.NewGuid().ToString(),
                Name = "Tech Corp",
                Description = "A tech company",
                Website = "https://techcorp.com",
                Telephone = "555-1234",
                Address = "123 Tech St",
                Country = "USA",
                Labels = labels
            };

            // Act
            var dto = entity.ToDto();

            // Assert
            Assert.Equal(entity.Id, dto.Id);
            Assert.Equal(entity.Name, dto.Name);
            Assert.Equal(entity.Description, dto.Description);
            Assert.Equal(entity.Website, dto.Website);
            Assert.Equal(entity.Telephone, dto.Telephone);
            Assert.Equal(entity.Address, dto.Address);
            Assert.Equal(entity.Country, dto.Country);
            Assert.Equal(2, dto.Labels.Count);
            Assert.Contains("Tech", dto.Labels);
            Assert.Contains("Startup", dto.Labels);
        }

        [Fact]
        public void Company_ToEntity_ShouldMapAllPropertiesIncludingLabels()
        {
            // Arrange
            var userId = Guid.NewGuid().ToString();
            var dto = new CompanyDto
            {
                Id = Guid.NewGuid().ToString(),
                Name = "Tech Corp",
                Description = "A tech company",
                Website = "https://techcorp.com",
                Telephone = "555-1234",
                Address = "123 Tech St",
                Country = "USA",
                Labels = new List<string> { "Tech", "Startup" }
            };

            // Act
            var entity = dto.ToEntity(userId);

            // Assert
            Assert.Equal(dto.Id, entity.Id);
            Assert.Equal(userId, entity.UserId);
            Assert.Equal(dto.Name, entity.Name);
            Assert.Equal(dto.Description, entity.Description);
            Assert.Equal(dto.Website, entity.Website);
            Assert.Equal(dto.Telephone, entity.Telephone);
            Assert.Equal(dto.Address, entity.Address);
            Assert.Equal(dto.Country, entity.Country);

            Assert.NotNull(entity.Labels);
            Assert.Equal(2, entity.Labels.Count);
            Assert.Equal(entity.Labels, dto.Labels);
        }

        [Fact]
        public void Recruiter_ToDto_ShouldMapAllProperties()
        {
            // Arrange
            var entity = new Recruiter
            {
                Id = Guid.NewGuid().ToString(),
                UserId = Guid.NewGuid().ToString(),
                Name = "Jane",
                Lastname = "Smith",
                ContactEmail = "jane@recruiting.com",
                Cellphone = "555-1234",
                Landline = "555-5678",
                LinkedInUrl = "https://linkedin.com/in/janesmith",
                IsFromConsultancy = true,
                CompanyName = "TechRecruit Inc"
            };

            // Act
            var dto = entity.ToDto();

            // Assert
            Assert.Equal(entity.Id, dto.Id);
            Assert.Equal(entity.Name, dto.Name);
            Assert.Equal(entity.Lastname, dto.Lastname);
            Assert.Equal(entity.ContactEmail, dto.ContactEmail);
            Assert.Equal(entity.Cellphone, dto.Cellphone);
            Assert.Equal(entity.Landline, dto.Landline);
            Assert.Equal(entity.LinkedInUrl, dto.LinkedInUrl);
            Assert.Equal(entity.IsFromConsultancy, dto.IsFromConsultancy);
            Assert.Equal(entity.CompanyName, dto.CompanyName);
        }

        [Fact]
        public void ApplicantTask_ToDto_ShouldMapAllProperties()
        {
            // Arrange
            var entity = new ApplicantTask
            {
                Id = Guid.NewGuid().ToString(),
                ApplicantId = Guid.NewGuid().ToString(),
                JobApplicationId = Guid.NewGuid().ToString(),
                Type = TaskType.FollowUp,
                Title = "Follow up with recruiter",
                Note = "Call next week",
                StartDate = DateTime.UtcNow,
                Deadline = DateTime.UtcNow.AddDays(7),
                Completed = false,
                ReminderSent = false
            };

            // Act
            var dto = entity.ToDto();

            // Assert
            Assert.Equal(entity.Id, dto.Id);
            Assert.Equal(entity.ApplicantId, dto.ApplicantId);
            Assert.Equal(entity.JobApplicationId, dto.JobApplicationId);
            Assert.Equal(entity.Type, dto.Type);
            Assert.Equal(entity.Title, dto.Title);
            Assert.Equal(entity.Note, dto.Note);
            Assert.Equal(entity.StartDate, dto.StartDate);
            Assert.Equal(entity.Deadline, dto.Deadline);
            Assert.Equal(entity.Completed, dto.Completed);
        }

        [Fact]
        public void Meeting_ToDto_ShouldMapAllProperties()
        {
            // Arrange
            var entity = new Meeting
            {
                Id = Guid.NewGuid().ToString(),
                ApplicantId = Guid.NewGuid().ToString(),
                JobApplicationId = Guid.NewGuid().ToString(),
                Title = "Technical Interview",
                Description = "Coding interview",
                StartDateTime = DateTime.UtcNow.AddDays(1),
                EndDateTime = DateTime.UtcNow.AddDays(1).AddHours(1),
                IsPhonecall = false,
                OnlineMeetingURL = "https://zoom.us/j/123456",
                PhysicalLocation = "",
                MeetingAccessCode = "123456",
                Outcome = "",
                Note = "Prepare coding questions",
                SendReminderEmail = true,
                ReminderMinutesBefore = 30,
                ReminderSent = false
            };

            // Act
            var dto = entity.ToDto();

            // Assert
            Assert.Equal(entity.Id, dto.Id);
            Assert.Equal(entity.ApplicantId, dto.ApplicantId);
            Assert.Equal(entity.JobApplicationId, dto.JobApplicationId);
            Assert.Equal(entity.Title, dto.Title);
            Assert.Equal(entity.Description, dto.Description);
            Assert.Equal(entity.StartDateTime, dto.StartDateTime);
            Assert.Equal(entity.EndDateTime, dto.EndDateTime);
            Assert.Equal(entity.IsPhonecall, dto.IsPhonecall);
            Assert.Equal(entity.OnlineMeetingURL, dto.OnlineMeetingURL);
            Assert.Equal(entity.SendReminderEmail, dto.SendReminderEmail);
        }

        [Fact]
        public void JobApplication_ToDto_ShouldMapAllPropertiesIncludingNotesAndLabels()
        {
            // Arrange
            var notes = new List<string> { "Great opportunity", "Remote work" };
            var labels = new List<string> { "Priority", "Remote" };
            var entity = new JobApplication
            {
                Id = Guid.NewGuid().ToString(),
                ApplicantId = Guid.NewGuid().ToString(),
                CompanyId = Guid.NewGuid().ToString(),
                JobTitle = "Senior Developer",
                Source = JobSource.LinkedIn,
                URL = "https://linkedin.com/jobs/123",
                JobDescription = "Great role",
                Salary = 100000,
                Location = "Remote",
                IsDirect = true,
                IsConsultancy = false,
                Status = JobApplicationStatus.Applied,
                DateApplied = DateTime.UtcNow,
                MainRecruiterContactId = Guid.NewGuid().ToString(),
                Notes = notes,
                Labels = labels,
                DeadLine = DateTime.UtcNow.AddDays(30),
                JobOffer = "",
                CVUsedId = Guid.NewGuid().ToString(),
                CreatedOnDate = DateTime.UtcNow,
                IsDeleted = false,
                InterviewSteps = new List<InterviewStep>()
            };

            // Act
            var dto = entity.ToDto();

            // Assert
            Assert.Equal(entity.Id, dto.Id);
            Assert.Equal(entity.JobTitle, dto.JobTitle);
            Assert.Equal(entity.Source, dto.Source);
            Assert.Equal(entity.Status, dto.Status);
            Assert.Equal(entity.Salary, dto.Salary);
            Assert.Equal(2, dto.Notes.Count);
            Assert.Contains("Great opportunity", dto.Notes);
            Assert.Equal(2, dto.Labels.Count);
            Assert.Contains("Priority", dto.Labels);
        }

        [Fact]
        public void JobApplication_ToEntity_ShouldHandleNullCompany()
        {
            // Arrange
            var applicantId = Guid.NewGuid().ToString();
            var dto = new JobApplicationDto
            {
                Id = Guid.NewGuid().ToString(),
                JobTitle = "Developer",
                Source = JobSource.LinkedIn,
                Status = JobApplicationStatus.NotApplied,
                Company = null // No company
            };

            // Act
            var entity = dto.ToEntity(applicantId);

            // Assert
            Assert.Equal(string.Empty, entity.CompanyId);
        }

        [Fact]
        public void ApplicantDto_ToEntity_ShouldGenerateGuidWhenIdIsNull()
        {
            // Arrange
            var userId = Guid.NewGuid().ToString();
            var dto = new ApplicantDto
            {
                Id = null, // No ID provided
                Name = "Test",
                Lastname = "User"
            };

            // Act
            var entity = dto.ToEntity(userId);

            // Assert
            Assert.NotNull(entity.Id);
            Assert.NotEmpty(entity.Id);
            Assert.True(Guid.TryParse(entity.Id, out _));
        }

        [Fact]
        public void Company_ToDto_ShouldHandleEmptyLabelsJson()
        {
            // Arrange
            var entity = new Company
            {
                Id = Guid.NewGuid().ToString(),
                UserId = Guid.NewGuid().ToString(),
                Name = "Test Company",
                Labels = new List<string>()
            };

            // Act
            var dto = entity.ToDto();

            // Assert
            Assert.NotNull(dto.Labels);
            Assert.Empty(dto.Labels);
        }

        [Fact]
        public void JobApplication_ToEntity_ShouldHandleNullNotesAndLabels()
        {
            // Arrange
            var applicantId = Guid.NewGuid().ToString();
            var dto = new JobApplicationDto
            {
                JobTitle = "Developer",
                Notes = null,
                Labels = null
            };

            // Act
            var entity = dto.ToEntity(applicantId);

            // Assert
            // A null collection on the wire must map to an empty one, not blow up.
            Assert.NotNull(entity.Notes);
            Assert.Empty(entity.Notes);
            Assert.NotNull(entity.Labels);
            Assert.Empty(entity.Labels);
        }
    }
}
