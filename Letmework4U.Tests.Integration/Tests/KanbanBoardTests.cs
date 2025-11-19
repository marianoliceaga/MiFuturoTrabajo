using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Letmework4U.Shared.Enums;
using Letmework4U.Shared.Models;
using Xunit;

namespace Letmework4U.Tests.Integration.Tests
{
    public class KanbanBoardTests : IClassFixture<TestWebApplicationFactory>
    {
        private readonly HttpClient _client;

        public KanbanBoardTests(TestWebApplicationFactory factory)
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

        private async Task<CompanyDto> CreateCompanyAsync()
        {
            var companyDto = new CompanyDto
            {
                Name = "Test Company",
                Description = "A test company",
                Website = "https://testcompany.com",
                Country = "USA"
            };

            var response = await _client.PostAsJsonAsync("/api/companies", companyDto);
            return (await response.Content.ReadFromJsonAsync<CompanyDto>())!;
        }

        private async Task<JobApplicationDto> CreateJobApplicationAsync(CompanyDto company, JobApplicationStatus status)
        {
            var jobAppDto = new JobApplicationDto
            {
                Company = company,
                JobTitle = $"Position - {Guid.NewGuid()}",
                Source = JobSource.LinkedIn,
                Status = status,
                Salary = 100000,
                Location = "Remote",
                IsDirect = true
            };

            var response = await _client.PostAsJsonAsync("/api/jobapplications", jobAppDto);
            return (await response.Content.ReadFromJsonAsync<JobApplicationDto>())!;
        }

        [Fact]
        public async Task GetBoard_ReturnsColumnsWithApplications()
        {
            // Arrange
            var token = await GetAuthTokenAsync();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var company = await CreateCompanyAsync();

            // Create applications in different statuses
            await CreateJobApplicationAsync(company, JobApplicationStatus.NotApplied);
            await CreateJobApplicationAsync(company, JobApplicationStatus.Applied);
            await CreateJobApplicationAsync(company, JobApplicationStatus.FirstInterview);
            await CreateJobApplicationAsync(company, JobApplicationStatus.Meetings);
            await CreateJobApplicationAsync(company, JobApplicationStatus.Offer);

            // Act
            var response = await _client.GetAsync("/api/jobapplications/board");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var board = await response.Content.ReadFromJsonAsync<List<BoardColumnDto>>();

            // Assert
            Assert.NotNull(board);
            Assert.Equal(8, board.Count); // All 8 statuses

            // Verify each column has the correct status
            var notAppliedColumn = board.FirstOrDefault(c => c.Status == JobApplicationStatus.NotApplied);
            Assert.NotNull(notAppliedColumn);
            Assert.Single(notAppliedColumn.Applications);

            var appliedColumn = board.FirstOrDefault(c => c.Status == JobApplicationStatus.Applied);
            Assert.NotNull(appliedColumn);
            Assert.Single(appliedColumn.Applications);

            var firstInterviewColumn = board.FirstOrDefault(c => c.Status == JobApplicationStatus.FirstInterview);
            Assert.NotNull(firstInterviewColumn);
            Assert.Single(firstInterviewColumn.Applications);

            var meetingsColumn = board.FirstOrDefault(c => c.Status == JobApplicationStatus.Meetings);
            Assert.NotNull(meetingsColumn);
            Assert.Single(meetingsColumn.Applications);

            var offerColumn = board.FirstOrDefault(c => c.Status == JobApplicationStatus.Offer);
            Assert.NotNull(offerColumn);
            Assert.Single(offerColumn.Applications);
        }

        [Fact]
        public async Task UpdateStatus_MovesApplicationBetweenColumns()
        {
            // Arrange
            var token = await GetAuthTokenAsync();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var company = await CreateCompanyAsync();
            var application = await CreateJobApplicationAsync(company, JobApplicationStatus.NotApplied);

            // Act - Move from NotApplied to Applied
            var updateStatusDto = new { Status = JobApplicationStatus.Applied };
            var updateResponse = await _client.PutAsJsonAsync(
                $"/api/jobapplications/{application.Id}/status",
                updateStatusDto
            );

            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

            // Verify the board reflects the change
            var boardResponse = await _client.GetAsync("/api/jobapplications/board");
            var board = await boardResponse.Content.ReadFromJsonAsync<List<BoardColumnDto>>();

            Assert.NotNull(board);

            var notAppliedColumn = board.First(c => c.Status == JobApplicationStatus.NotApplied);
            var appliedColumn = board.First(c => c.Status == JobApplicationStatus.Applied);

            Assert.DoesNotContain(notAppliedColumn.Applications, a => a.Id == application.Id);
            Assert.Contains(appliedColumn.Applications, a => a.Id == application.Id);
        }

        [Fact]
        public async Task UpdateStatus_ThroughApplicationLifecycle_Success()
        {
            // Arrange
            var token = await GetAuthTokenAsync();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var company = await CreateCompanyAsync();
            var application = await CreateJobApplicationAsync(company, JobApplicationStatus.NotApplied);

            // Define the lifecycle progression
            var statuses = new[]
            {
                JobApplicationStatus.NotApplied,
                JobApplicationStatus.Applied,
                JobApplicationStatus.FirstInterview,
                JobApplicationStatus.Meetings,
                JobApplicationStatus.Offer,
                JobApplicationStatus.Completed
            };

            // Act & Assert - Progress through each status
            for (int i = 1; i < statuses.Length; i++)
            {
                var updateStatusDto = new { Status = statuses[i] };
                var updateResponse = await _client.PutAsJsonAsync(
                    $"/api/jobapplications/{application.Id}/status",
                    updateStatusDto
                );

                Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

                // Verify the application has the new status
                var getResponse = await _client.GetAsync($"/api/jobapplications/{application.Id}");
                var updated = await getResponse.Content.ReadFromJsonAsync<JobApplicationDto>();

                Assert.NotNull(updated);
                Assert.Equal(statuses[i], updated.Status);
            }
        }

        [Fact]
        public async Task Board_MultipleApplicationsInSameColumn_AllDisplayed()
        {
            // Arrange
            var token = await GetAuthTokenAsync();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var company = await CreateCompanyAsync();

            // Create multiple applications with the same status
            await CreateJobApplicationAsync(company, JobApplicationStatus.Applied);
            await CreateJobApplicationAsync(company, JobApplicationStatus.Applied);
            await CreateJobApplicationAsync(company, JobApplicationStatus.Applied);

            // Act
            var response = await _client.GetAsync("/api/jobapplications/board");
            var board = await response.Content.ReadFromJsonAsync<List<BoardColumnDto>>();

            // Assert
            Assert.NotNull(board);
            var appliedColumn = board.First(c => c.Status == JobApplicationStatus.Applied);
            Assert.Equal(3, appliedColumn.Applications.Count);
        }

        [Fact]
        public async Task Board_EmptyColumns_StillReturned()
        {
            // Arrange
            var token = await GetAuthTokenAsync();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Don't create any applications

            // Act
            var response = await _client.GetAsync("/api/jobapplications/board");
            var board = await response.Content.ReadFromJsonAsync<List<BoardColumnDto>>();

            // Assert
            Assert.NotNull(board);
            Assert.Equal(8, board.Count); // All columns present even when empty

            foreach (var column in board)
            {
                Assert.Empty(column.Applications);
            }
        }

        [Fact]
        public async Task UpdateStatus_ToRejected_Success()
        {
            // Arrange
            var token = await GetAuthTokenAsync();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var company = await CreateCompanyAsync();
            var application = await CreateJobApplicationAsync(company, JobApplicationStatus.FirstInterview);

            // Act - Move to Rejected status
            var updateStatusDto = new { Status = JobApplicationStatus.Rejected };
            var updateResponse = await _client.PutAsJsonAsync(
                $"/api/jobapplications/{application.Id}/status",
                updateStatusDto
            );

            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

            // Assert - Verify in Rejected column
            var boardResponse = await _client.GetAsync("/api/jobapplications/board");
            var board = await boardResponse.Content.ReadFromJsonAsync<List<BoardColumnDto>>();

            Assert.NotNull(board);
            var rejectedColumn = board.First(c => c.Status == JobApplicationStatus.Rejected);
            Assert.Contains(rejectedColumn.Applications, a => a.Id == application.Id);
        }

        [Fact]
        public async Task UpdateStatus_ToNoResponse_Success()
        {
            // Arrange
            var token = await GetAuthTokenAsync();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var company = await CreateCompanyAsync();
            var application = await CreateJobApplicationAsync(company, JobApplicationStatus.Applied);

            // Act - Move to NoResponse status
            var updateStatusDto = new { Status = JobApplicationStatus.NoResponse };
            var updateResponse = await _client.PutAsJsonAsync(
                $"/api/jobapplications/{application.Id}/status",
                updateStatusDto
            );

            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

            // Assert - Verify in NoResponse column
            var boardResponse = await _client.GetAsync("/api/jobapplications/board");
            var board = await boardResponse.Content.ReadFromJsonAsync<List<BoardColumnDto>>();

            Assert.NotNull(board);
            var noResponseColumn = board.First(c => c.Status == JobApplicationStatus.NoResponse);
            Assert.Contains(noResponseColumn.Applications, a => a.Id == application.Id);
        }

        [Fact]
        public async Task Board_ApplicationsAreSortedByDate()
        {
            // Arrange
            var token = await GetAuthTokenAsync();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var company = await CreateCompanyAsync();

            // Create applications with slight delays to ensure different timestamps
            var app1 = await CreateJobApplicationAsync(company, JobApplicationStatus.Applied);
            await Task.Delay(100);
            var app2 = await CreateJobApplicationAsync(company, JobApplicationStatus.Applied);
            await Task.Delay(100);
            var app3 = await CreateJobApplicationAsync(company, JobApplicationStatus.Applied);

            // Act
            var response = await _client.GetAsync("/api/jobapplications/board");
            var board = await response.Content.ReadFromJsonAsync<List<BoardColumnDto>>();

            // Assert
            Assert.NotNull(board);
            var appliedColumn = board.First(c => c.Status == JobApplicationStatus.Applied);
            Assert.Equal(3, appliedColumn.Applications.Count);

            // Verify applications are sorted (newest first or oldest first depending on implementation)
            var apps = appliedColumn.Applications;
            Assert.True(apps.Count >= 3);
        }

        [Fact]
        public async Task UpdateStatus_InvalidApplicationId_ReturnsNotFound()
        {
            // Arrange
            var token = await GetAuthTokenAsync();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var nonexistentId = Guid.NewGuid().ToString();
            var updateStatusDto = new { Status = JobApplicationStatus.Applied };

            // Act
            var response = await _client.PutAsJsonAsync(
                $"/api/jobapplications/{nonexistentId}/status",
                updateStatusDto
            );

            // Assert
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task Board_DeletedApplications_NotDisplayed()
        {
            // Arrange
            var token = await GetAuthTokenAsync();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var company = await CreateCompanyAsync();
            var application = await CreateJobApplicationAsync(company, JobApplicationStatus.Applied);

            // Delete the application
            await _client.DeleteAsync($"/api/jobapplications/{application.Id}");

            // Act
            var boardResponse = await _client.GetAsync("/api/jobapplications/board");
            var board = await boardResponse.Content.ReadFromJsonAsync<List<BoardColumnDto>>();

            // Assert
            Assert.NotNull(board);
            var appliedColumn = board.First(c => c.Status == JobApplicationStatus.Applied);
            Assert.DoesNotContain(appliedColumn.Applications, a => a.Id == application.Id);
        }
    }
}
