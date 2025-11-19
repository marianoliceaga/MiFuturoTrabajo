using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Letmework4U.Shared.Enums;
using Letmework4U.Shared.Models;
using Xunit;

namespace Letmework4U.Tests.Integration.Tests
{
    public class JobApplicationCrudTests : IClassFixture<TestWebApplicationFactory>
    {
        private readonly HttpClient _client;

        public JobApplicationCrudTests(TestWebApplicationFactory factory)
        {
            _client = factory.CreateClient();
        }

        private async Task<string> GetAuthTokenAsync()
        {
            var registerRequest = new RegisterRequest
            {
                Email = $"test{Guid.NewGuid()}@example.com",
                Password = "TestPassword123!",
                ConfirmPassword = "TestPassword123!",
                FirstName = "Test",
                LastName = "User"
            };

            var response = await _client.PostAsJsonAsync("/api/auth/register", registerRequest);
            var authResponse = await response.Content.ReadFromJsonAsync<AuthResponse>();
            return authResponse!.AccessToken;
        }

        [Fact]
        public async Task JobApplication_FullCrudFlow_Success()
        {
            // Arrange - Get auth token
            var token = await GetAuthTokenAsync();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Step 1: Create a company first
            var companyDto = new CompanyDto
            {
                Name = "Test Company",
                Description = "A test company",
                Website = "https://testcompany.com",
                Country = "USA",
                Labels = new List<string> { "Tech", "Startup" }
            };

            var companyResponse = await _client.PostAsJsonAsync("/api/companies", companyDto);
            Assert.Equal(HttpStatusCode.Created, companyResponse.StatusCode);
            var createdCompany = await companyResponse.Content.ReadFromJsonAsync<CompanyDto>();
            Assert.NotNull(createdCompany);

            // Step 2: Create job application
            var jobAppDto = new JobApplicationDto
            {
                Company = createdCompany,
                JobTitle = "Senior Software Engineer",
                Source = JobSource.LinkedIn,
                URL = "https://linkedin.com/jobs/123",
                JobDescription = "Great opportunity",
                Salary = 120000,
                Location = "Remote",
                IsDirect = true,
                IsConsultancy = false,
                Status = JobApplicationStatus.NotApplied,
                Notes = new List<string> { "Interesting role" },
                Labels = new List<string> { "Remote", "Priority" }
            };

            var createResponse = await _client.PostAsJsonAsync("/api/jobapplications", jobAppDto);
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

            var createdApp = await createResponse.Content.ReadFromJsonAsync<JobApplicationDto>();
            Assert.NotNull(createdApp);
            Assert.NotNull(createdApp.Id);
            Assert.Equal(jobAppDto.JobTitle, createdApp.JobTitle);
            Assert.Equal(jobAppDto.Salary, createdApp.Salary);
            Assert.Equal(2, createdApp.Labels.Count);

            // Step 3: Get by ID
            var getResponse = await _client.GetAsync($"/api/jobapplications/{createdApp.Id}");
            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

            var retrievedApp = await getResponse.Content.ReadFromJsonAsync<JobApplicationDto>();
            Assert.NotNull(retrievedApp);
            Assert.Equal(createdApp.Id, retrievedApp.Id);
            Assert.Equal(createdApp.JobTitle, retrievedApp.JobTitle);

            // Step 4: Update
            retrievedApp.JobTitle = "Lead Software Engineer";
            retrievedApp.Status = JobApplicationStatus.Applied;
            retrievedApp.Salary = 130000;

            var updateResponse = await _client.PutAsJsonAsync($"/api/jobapplications/{retrievedApp.Id}", retrievedApp);
            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

            var updatedApp = await updateResponse.Content.ReadFromJsonAsync<JobApplicationDto>();
            Assert.NotNull(updatedApp);
            Assert.Equal("Lead Software Engineer", updatedApp.JobTitle);
            Assert.Equal(JobApplicationStatus.Applied, updatedApp.Status);
            Assert.Equal(130000, updatedApp.Salary);

            // Step 5: Get all
            var getAllResponse = await _client.GetAsync("/api/jobapplications");
            Assert.Equal(HttpStatusCode.OK, getAllResponse.StatusCode);

            var allApps = await getAllResponse.Content.ReadFromJsonAsync<List<JobApplicationDto>>();
            Assert.NotNull(allApps);
            Assert.Contains(allApps, a => a.Id == createdApp.Id);

            // Step 6: Delete
            var deleteResponse = await _client.DeleteAsync($"/api/jobapplications/{createdApp.Id}");
            Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

            // Step 7: Verify deletion
            var getDeletedResponse = await _client.GetAsync($"/api/jobapplications/{createdApp.Id}");
            Assert.Equal(HttpStatusCode.NotFound, getDeletedResponse.StatusCode);
        }

        [Fact]
        public async Task Company_FullCrudFlow_Success()
        {
            // Arrange
            var token = await GetAuthTokenAsync();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Create
            var companyDto = new CompanyDto
            {
                Name = "Tech Innovations Inc",
                Description = "Leading tech company",
                Website = "https://techinnovations.com",
                Telephone = "555-1234",
                Address = "123 Tech Street",
                Country = "USA",
                Labels = new List<string> { "Enterprise", "Fortune500" }
            };

            var createResponse = await _client.PostAsJsonAsync("/api/companies", companyDto);
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

            var created = await createResponse.Content.ReadFromJsonAsync<CompanyDto>();
            Assert.NotNull(created);
            Assert.Equal(companyDto.Name, created.Name);

            // Read
            var getResponse = await _client.GetAsync($"/api/companies/{created.Id}");
            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

            // Update
            created.Name = "Updated Tech Innovations";
            var updateResponse = await _client.PutAsJsonAsync($"/api/companies/{created.Id}", created);
            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

            var updated = await updateResponse.Content.ReadFromJsonAsync<CompanyDto>();
            Assert.Equal("Updated Tech Innovations", updated!.Name);

            // Delete
            var deleteResponse = await _client.DeleteAsync($"/api/companies/{created.Id}");
            Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        }

        [Fact]
        public async Task Recruiter_FullCrudFlow_Success()
        {
            // Arrange
            var token = await GetAuthTokenAsync();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Create
            var recruiterDto = new RecruiterDto
            {
                Name = "Jane",
                Lastname = "Smith",
                ContactEmail = "jane.smith@recruiting.com",
                Cellphone = "555-1234",
                Landline = "555-5678",
                LinkedInUrl = "https://linkedin.com/in/janesmith",
                IsFromConsultancy = true,
                CompanyName = "Elite Recruiting"
            };

            var createResponse = await _client.PostAsJsonAsync("/api/recruiters", recruiterDto);
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

            var created = await createResponse.Content.ReadFromJsonAsync<RecruiterDto>();
            Assert.NotNull(created);
            Assert.Equal(recruiterDto.Name, created.Name);

            // Read
            var getResponse = await _client.GetAsync($"/api/recruiters/{created.Id}");
            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

            // Update
            created.Cellphone = "555-9999";
            var updateResponse = await _client.PutAsJsonAsync($"/api/recruiters/{created.Id}", created);
            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

            // Delete
            var deleteResponse = await _client.DeleteAsync($"/api/recruiters/{created.Id}");
            Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        }

        [Fact]
        public async Task ApplicantTask_FullCrudFlow_Success()
        {
            // Arrange
            var token = await GetAuthTokenAsync();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Create
            var taskDto = new ApplicantTaskDto
            {
                Type = TaskType.FollowUp,
                Title = "Follow up with recruiter",
                Note = "Call next week",
                StartDate = DateTime.UtcNow,
                Deadline = DateTime.UtcNow.AddDays(7),
                Completed = false
            };

            var createResponse = await _client.PostAsJsonAsync("/api/applicanttasks", taskDto);
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

            var created = await createResponse.Content.ReadFromJsonAsync<ApplicantTaskDto>();
            Assert.NotNull(created);
            Assert.Equal(taskDto.Title, created.Title);

            // Read
            var getResponse = await _client.GetAsync($"/api/applicanttasks/{created.Id}");
            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

            // Update
            created.Completed = true;
            var updateResponse = await _client.PutAsJsonAsync($"/api/applicanttasks/{created.Id}", created);
            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

            var updated = await updateResponse.Content.ReadFromJsonAsync<ApplicantTaskDto>();
            Assert.True(updated!.Completed);

            // Delete
            var deleteResponse = await _client.DeleteAsync($"/api/applicanttasks/{created.Id}");
            Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        }

        [Fact]
        public async Task Meeting_FullCrudFlow_Success()
        {
            // Arrange
            var token = await GetAuthTokenAsync();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Create
            var meetingDto = new MeetingDto
            {
                Title = "Technical Interview",
                Description = "Coding interview",
                StartDateTime = DateTime.UtcNow.AddDays(1),
                EndDateTime = DateTime.UtcNow.AddDays(1).AddHours(1),
                IsPhonecall = false,
                OnlineMeetingURL = "https://zoom.us/j/123456",
                MeetingAccessCode = "123456",
                Note = "Prepare coding questions",
                SendReminderEmail = true,
                ReminderMinutesBefore = 30,
                Recruiters = new List<RecruiterDto>()
            };

            var createResponse = await _client.PostAsJsonAsync("/api/meetings", meetingDto);
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

            var created = await createResponse.Content.ReadFromJsonAsync<MeetingDto>();
            Assert.NotNull(created);
            Assert.Equal(meetingDto.Title, created.Title);

            // Read
            var getResponse = await _client.GetAsync($"/api/meetings/{created.Id}");
            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

            // Update
            created.Outcome = "Great interview, moving forward";
            var updateResponse = await _client.PutAsJsonAsync($"/api/meetings/{created.Id}", created);
            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

            // Delete
            var deleteResponse = await _client.DeleteAsync($"/api/meetings/{created.Id}");
            Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        }

        [Fact]
        public async Task CVVersion_FullCrudFlow_Success()
        {
            // Arrange
            var token = await GetAuthTokenAsync();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Create
            var cvDto = new CVVersionDto
            {
                Name = "Software Engineer CV",
                FileUrl = "https://storage.example.com/cv.pdf",
                UploadedDate = DateTime.UtcNow,
                IsDefault = true
            };

            var createResponse = await _client.PostAsJsonAsync("/api/cvversions", cvDto);
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

            var created = await createResponse.Content.ReadFromJsonAsync<CVVersionDto>();
            Assert.NotNull(created);
            Assert.Equal(cvDto.Name, created.Name);
            Assert.True(created.IsDefault);

            // Read
            var getResponse = await _client.GetAsync($"/api/cvversions/{created.Id}");
            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

            // Update
            created.Name = "Senior Software Engineer CV";
            var updateResponse = await _client.PutAsJsonAsync($"/api/cvversions/{created.Id}", created);
            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

            // Delete
            var deleteResponse = await _client.DeleteAsync($"/api/cvversions/{created.Id}");
            Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        }

        [Fact]
        public async Task GetNonexistentResource_ReturnsNotFound()
        {
            // Arrange
            var token = await GetAuthTokenAsync();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var nonexistentId = Guid.NewGuid().ToString();

            // Act & Assert
            var response = await _client.GetAsync($"/api/jobapplications/{nonexistentId}");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task UpdateNonexistentResource_ReturnsNotFound()
        {
            // Arrange
            var token = await GetAuthTokenAsync();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var jobAppDto = new JobApplicationDto
            {
                Id = Guid.NewGuid().ToString(),
                JobTitle = "Developer",
                Status = JobApplicationStatus.NotApplied
            };

            // Act
            var response = await _client.PutAsJsonAsync($"/api/jobapplications/{jobAppDto.Id}", jobAppDto);

            // Assert
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task CrudOperations_WithoutAuth_ReturnsUnauthorized()
        {
            // Arrange - No auth header
            var companyDto = new CompanyDto { Name = "Test" };

            // Act & Assert
            var createResponse = await _client.PostAsJsonAsync("/api/companies", companyDto);
            Assert.Equal(HttpStatusCode.Unauthorized, createResponse.StatusCode);

            var getResponse = await _client.GetAsync("/api/companies");
            Assert.Equal(HttpStatusCode.Unauthorized, getResponse.StatusCode);
        }
    }
}
