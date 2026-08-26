using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using MiFuturoTrabajo.Shared.Models;
using Xunit;

namespace MiFuturoTrabajo.Tests.Integration.Tests
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

        /// <summary>
        /// Regression guard for the implicit-[Required] trap: with
        /// &lt;Nullable&gt;enable&lt;/Nullable&gt;, every non-nullable string on a DTO is
        /// treated as required by model validation, so a company submitted with
        /// only a name was rejected with a 400 naming five "optional" fields.
        /// Raw JSON with explicit nulls is used deliberately - serialising a
        /// CompanyDto would send empty strings and pass either way.
        /// </summary>
        [Fact]
        public async Task Company_CreateWithOnlyName_Succeeds()
        {
            // Arrange
            var token = await GetAuthTokenAsync();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var rawJson = """
                {
                  "id": null,
                  "name": "OnlyName Inc",
                  "description": null,
                  "website": null,
                  "telephone": null,
                  "address": null,
                  "country": null,
                  "labels": []
                }
                """;

            // Act
            var response = await _client.PostAsync(
                "/api/companies/CompanyAdd",
                new StringContent(rawJson, System.Text.Encoding.UTF8, "application/json"));

            // Assert
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var created = await response.Content.ReadFromJsonAsync<CompanyDto>();
            Assert.NotNull(created);
            Assert.Equal("OnlyName Inc", created!.Name);

            // and the server must mint an id rather than persisting an empty key
            Assert.False(string.IsNullOrWhiteSpace(created.Id));
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

            var companyResponse = await _client.PostAsJsonAsync("/api/companies/CompanyAdd", companyDto);
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

            var createResponse = await _client.PostAsJsonAsync("/api/jobapplication/JobApplicationAdd", jobAppDto);
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

            var createdApp = await createResponse.Content.ReadFromJsonAsync<JobApplicationDto>();
            Assert.NotNull(createdApp);
            Assert.NotNull(createdApp.Id);
            Assert.Equal(jobAppDto.JobTitle, createdApp.JobTitle);
            Assert.Equal(jobAppDto.Salary, createdApp.Salary);
            Assert.Equal(2, createdApp.Labels.Count);

            // Step 3: Get by ID
            var getResponse = await _client.GetAsync($"/api/jobapplication/JobApplicationGetById/{createdApp.Id}");
            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

            var retrievedApp = await getResponse.Content.ReadFromJsonAsync<JobApplicationDto>();
            Assert.NotNull(retrievedApp);
            Assert.Equal(createdApp.Id, retrievedApp.Id);
            Assert.Equal(createdApp.JobTitle, retrievedApp.JobTitle);

            // Step 4: Update
            retrievedApp.JobTitle = "Lead Software Engineer";
            retrievedApp.Status = JobApplicationStatus.Applied;
            retrievedApp.Salary = 130000;

            var updateResponse = await _client.PatchAsJsonAsync($"/api/jobapplication/{retrievedApp.Id}", retrievedApp);
            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

            var updatedApp = await updateResponse.Content.ReadFromJsonAsync<JobApplicationDto>();
            Assert.NotNull(updatedApp);
            Assert.Equal("Lead Software Engineer", updatedApp.JobTitle);
            Assert.Equal(JobApplicationStatus.Applied, updatedApp.Status);
            Assert.Equal(130000, updatedApp.Salary);

            // Step 5: Get all
            var getAllResponse = await _client.GetAsync("/api/jobapplication");
            Assert.Equal(HttpStatusCode.OK, getAllResponse.StatusCode);

            var allApps = await getAllResponse.Content.ReadFromJsonAsync<List<JobApplicationDto>>();
            Assert.NotNull(allApps);
            Assert.Contains(allApps, a => a.Id == createdApp.Id);

            // Step 6: Delete
            var deleteResponse = await _client.DeleteAsync($"/api/jobapplication/JobApplicationDeleteById/{createdApp.Id}");
            Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

            // Step 7: Verify deletion
            var getDeletedResponse = await _client.GetAsync($"/api/jobapplication/JobApplicationGetById/{createdApp.Id}");
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

            var createResponse = await _client.PostAsJsonAsync("/api/companies/CompanyAdd", companyDto);
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

            var created = await createResponse.Content.ReadFromJsonAsync<CompanyDto>();
            Assert.NotNull(created);
            Assert.Equal(companyDto.Name, created.Name);

            // Read
            var getResponse = await _client.GetAsync($"/api/companies/CompanyGetById/{created.Id}");
            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

            // Update
            created.Name = "Updated Tech Innovations";
            var updateResponse = await _client.PatchAsJsonAsync($"/api/companies/{created.Id}", created);
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

            var createResponse = await _client.PostAsJsonAsync("/api/recruiters/RecruiterAdd", recruiterDto);
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

            var created = await createResponse.Content.ReadFromJsonAsync<RecruiterDto>();
            Assert.NotNull(created);
            Assert.Equal(recruiterDto.Name, created.Name);

            // Read
            var getResponse = await _client.GetAsync($"/api/recruiters/RecruiterGetById/{created.Id}");
            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

            // Update
            created.Cellphone = "555-9999";
            var updateResponse = await _client.PatchAsJsonAsync($"/api/recruiters/{created.Id}", created);
            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

            // Delete
            var deleteResponse = await _client.DeleteAsync($"/api/recruiters/RecruiterDeleteById/{created.Id}");
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

            var createResponse = await _client.PostAsJsonAsync("/api/applicanttask/ApplicantTaskAdd", taskDto);
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

            var created = await createResponse.Content.ReadFromJsonAsync<ApplicantTaskDto>();
            Assert.NotNull(created);
            Assert.Equal(taskDto.Title, created.Title);

            // Read
            var getResponse = await _client.GetAsync($"/api/applicanttask/ApplicantTaskGetById/{created.Id}");
            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

            // Update
            created.Completed = true;
            var updateResponse = await _client.PatchAsJsonAsync($"/api/applicanttask/{created.Id}", created);
            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

            var updated = await updateResponse.Content.ReadFromJsonAsync<ApplicantTaskDto>();
            Assert.True(updated!.Completed);

            // Delete
            var deleteResponse = await _client.DeleteAsync($"/api/applicanttask/ApplicantTaskDeleteById/{created.Id}");
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

            var createResponse = await _client.PostAsJsonAsync("/api/meeting/MeetingAdd", meetingDto);
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

            var created = await createResponse.Content.ReadFromJsonAsync<MeetingDto>();
            Assert.NotNull(created);
            Assert.Equal(meetingDto.Title, created.Title);

            // Read
            var getResponse = await _client.GetAsync($"/api/meeting/MeetingGetById/{created.Id}");
            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

            // Update
            created.Outcome = "Great interview, moving forward";
            var updateResponse = await _client.PatchAsJsonAsync($"/api/meeting/{created.Id}", created);
            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

            // Delete
            var deleteResponse = await _client.DeleteAsync($"/api/meeting/MeetingDeleteById/{created.Id}");
            Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        }

        [Fact]
        public async Task CVVersion_FullCrudFlow_Success()
        {
            // Arrange
            var token = await GetAuthTokenAsync();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Create
            // No file reference here on purpose: a CV version created through
            // JSON is metadata only. The document arrives through the upload
            // endpoint, which is the only thing that may assign a storage key.
            var cvDto = new CVVersionDto
            {
                Name = "Software Engineer CV",
                UploadedDate = DateTime.UtcNow,
                IsDefault = true
            };

            var createResponse = await _client.PostAsJsonAsync("/api/cvversion", cvDto);
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

            var created = await createResponse.Content.ReadFromJsonAsync<CVVersionDto>();
            Assert.NotNull(created);
            Assert.Equal(cvDto.Name, created.Name);
            Assert.True(created.IsDefault);

            // Read
            var getResponse = await _client.GetAsync($"/api/cvversion/{created.Id}");
            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

            // Update
            created.Name = "Senior Software Engineer CV";
            var updateResponse = await _client.PatchAsJsonAsync($"/api/cvversion/{created.Id}", created);
            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

            // Delete
            var deleteResponse = await _client.DeleteAsync($"/api/cvversion/{created.Id}");
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
            var response = await _client.GetAsync($"/api/jobapplication/JobApplicationGetById/{nonexistentId}");
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
            var response = await _client.PatchAsJsonAsync($"/api/jobapplication/{jobAppDto.Id}", jobAppDto);

            // Assert
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task CrudOperations_WithoutAuth_ReturnsUnauthorized()
        {
            // Arrange - No auth header
            var companyDto = new CompanyDto { Name = "Test" };

            // Act & Assert
            var createResponse = await _client.PostAsJsonAsync("/api/companies/CompanyAdd", companyDto);
            Assert.Equal(HttpStatusCode.Unauthorized, createResponse.StatusCode);

            var getResponse = await _client.GetAsync("/api/companies");
            Assert.Equal(HttpStatusCode.Unauthorized, getResponse.StatusCode);
        }
    }
}
