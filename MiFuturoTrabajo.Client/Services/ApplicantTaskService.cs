using MiFuturoTrabajo.Shared.Models;
using System.Net.Http.Json;

namespace MiFuturoTrabajo.Client.Services
{
    public class ApplicantTaskService : IApplicantTaskService
    {
        private readonly HttpClient _http;

        public ApplicantTaskService(HttpClient http)
        {
            _http = http;
        }

        public async Task<List<ApplicantTaskDto>> GetAllAsync()
        {
            return await _http.GetFromJsonAsync<List<ApplicantTaskDto>>("applicanttask") ?? new List<ApplicantTaskDto>();
        }

        public async Task<List<ApplicantTaskDto>> GetByApplicantAsync(string applicantId)
        {
            var all = await GetAllAsync();
            return all.Where(t => t.ApplicantId == applicantId).ToList();
        }

        public async Task<ApplicantTaskDto> GetByIdAsync(string id)
        {
            return await _http.GetFromJsonAsync<ApplicantTaskDto>($"applicanttask/ApplicantTaskGetById/{id}");
        }

        public async Task<ApplicantTaskDto> CreateAsync(ApplicantTaskDto task)
        {
            var response = await _http.PostAsJsonAsync("applicanttask/ApplicantTaskAdd", task);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<ApplicantTaskDto>();
        }

        public async Task<ApplicantTaskDto> UpdateAsync(string id, ApplicantTaskDto task)
        {
            var response = await _http.PatchAsJsonAsync($"applicanttask/{id}", task);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<ApplicantTaskDto>();
        }

        public async Task DeleteAsync(string id)
        {
            await _http.DeleteAsync($"applicanttask/ApplicantTaskDeleteById/{id}");
        }

        public async Task<ApplicantTaskDto> ToggleCompleteAsync(string id)
        {
            var task = await GetByIdAsync(id);
            task.Completed = !task.Completed;
            return await UpdateAsync(id, task);
        }

        public async Task<List<ApplicantTaskDto>> GetUpcomingAsync()
        {
            var all = await GetAllAsync();
            return all.Where(t => !t.Completed && t.Deadline > DateTime.UtcNow)
                     .OrderBy(t => t.Deadline)
                     .ToList();
        }
    }
}