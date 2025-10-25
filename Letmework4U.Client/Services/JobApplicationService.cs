using System.Net.Http.Json;
using Letmework4U.Shared.Models;

namespace Letmework4U.Client.Services
{
    public class JobApplicationService : IJobApplicationService
    {
        private readonly HttpClient _http;

        public JobApplicationService(HttpClient http)
        {
            _http = http;
        }

        public async Task<List<JobApplicationDto>> GetAllAsync()
        {
            try
            {
                var result = await _http.GetFromJsonAsync<List<JobApplicationDto>>("JobApplications");
                return result ?? new List<JobApplicationDto>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching job applications: {ex.Message}");
                return new List<JobApplicationDto>();
            }
        }

        public async Task<JobApplicationDto> GetByIdAsync(string id)
        {
            return await _http.GetFromJsonAsync<JobApplicationDto>($"JobApplications/JobApplicationGetById/{id}");
        }

        public async Task<JobApplicationDto> CreateAsync(JobApplicationDto application)
        {
            var response = await _http.PostAsJsonAsync("JobApplications/JobApplicationAdd", application);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<JobApplicationDto>();
        }

        public async Task<JobApplicationDto> UpdateAsync(string id, JobApplicationDto application)
        {
            var response = await _http.PatchAsJsonAsync($"JobApplications/{id}", application);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<JobApplicationDto>();
        }

        public async Task DeleteAsync(string id)
        {
            var response = await _http.DeleteAsync($"JobApplications/JobApplicationDeleteById/{id}");
            response.EnsureSuccessStatusCode();
        }

        public async Task<List<BoardColumnDto>> GetBoardDataAsync()
        {
            var applications = await GetAllAsync();
            
            var boardColumns = new List<BoardColumnDto>
            {
                new BoardColumnDto { Status = JobApplicationStatus.NotApplied, Title = "Applying" },
                new BoardColumnDto { Status = JobApplicationStatus.Applied, Title = "Applied" },
                new BoardColumnDto { Status = JobApplicationStatus.FirstInterview, Title = "First Interview" },
                new BoardColumnDto { Status = JobApplicationStatus.Meetings, Title = "Interviewing" },
                new BoardColumnDto { Status = JobApplicationStatus.Offer, Title = "Accepted" },
                new BoardColumnDto { Status = JobApplicationStatus.Rejected, Title = "Declined" },
                new BoardColumnDto { Status = JobApplicationStatus.Completed, Title = "Not Selected" },
                new BoardColumnDto { Status = JobApplicationStatus.NoResponse, Title = "No Response (Ghosting)" }
            };

            foreach (var column in boardColumns)
            {
                column.Applications = applications.Where(a => a.Status == column.Status).ToList();
            }

            return boardColumns;
        }

        public async Task<JobApplicationDto> UpdateStatusAsync(string id, JobApplicationStatus newStatus)
        {
            var application = await GetByIdAsync(id);
            application.Status = newStatus;
            return await UpdateAsync(id, application);
        }
    }
}