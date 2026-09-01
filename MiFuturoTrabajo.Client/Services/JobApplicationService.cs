using System.Net.Http.Json;
using MiFuturoTrabajo.Shared.Models;

namespace MiFuturoTrabajo.Client.Services
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
                var result = await _http.GetFromJsonAsync<List<JobApplicationDto>>("jobapplication");
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
            return await _http.GetRequiredAsync<JobApplicationDto>($"jobapplication/JobApplicationGetById/{id}");
        }

        public async Task<JobApplicationDto> CreateAsync(JobApplicationDto application)
        {
            var response = await _http.PostAsJsonAsync("jobapplication/JobApplicationAdd", application);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadRequiredAsync<JobApplicationDto>("jobapplication/JobApplicationAdd");
        }

        public async Task<JobApplicationDto> UpdateAsync(string id, JobApplicationDto application)
        {
            var response = await _http.PatchAsJsonAsync($"jobapplication/{id}", application);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadRequiredAsync<JobApplicationDto>($"jobapplication/{id}");
        }

        public async Task DeleteAsync(string id)
        {
            var response = await _http.DeleteAsync($"jobapplication/JobApplicationDeleteById/{id}");
            response.EnsureSuccessStatusCode();
        }

        public async Task<List<BoardColumnDto>> GetBoardDataAsync()
        {
            var applications = await GetAllAsync();
            
            // Status and order only. The column heading used to be an English
            // literal here; it is now looked up from the resource file when the
            // board renders, because a service has no business deciding which
            // language the UI is drawn in.
            var boardColumns = new List<BoardColumnDto>
            {
                new BoardColumnDto { Status = JobApplicationStatus.NotApplied },
                new BoardColumnDto { Status = JobApplicationStatus.Applied },
                new BoardColumnDto { Status = JobApplicationStatus.FirstInterview },
                new BoardColumnDto { Status = JobApplicationStatus.Meetings },
                new BoardColumnDto { Status = JobApplicationStatus.Offer },
                new BoardColumnDto { Status = JobApplicationStatus.Rejected },
                new BoardColumnDto { Status = JobApplicationStatus.Completed },
                new BoardColumnDto { Status = JobApplicationStatus.NoResponse }
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