using Letmework4U.Shared.Models;
using System.Net.Http.Json;

namespace Letmework4U.Client.Services
{
    public class RecruiterService : IRecruiterService
    {
        private readonly HttpClient _http;

        public RecruiterService(HttpClient http)
        {
            _http = http;
        }

        public async Task<List<RecruiterDto>> GetAllAsync()
        {
            return await _http.GetFromJsonAsync<List<RecruiterDto>>("recruiter") ?? new List<RecruiterDto>();
        }

        public async Task<RecruiterDto> GetByIdAsync(string id)
        {
            return await _http.GetFromJsonAsync<RecruiterDto>($"recruiter/RecruiterGetById/{id}");
        }

        public async Task<RecruiterDto> CreateAsync(RecruiterDto recruiter)
        {
            var response = await _http.PostAsJsonAsync("recruiter/RecruiterAdd", recruiter);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<RecruiterDto>();
        }

        public async Task<RecruiterDto> UpdateAsync(string id, RecruiterDto recruiter)
        {
            var response = await _http.PatchAsJsonAsync($"recruiter/{id}", recruiter);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<RecruiterDto>();
        }

        public async Task DeleteAsync(string id)
        {
            await _http.DeleteAsync($"recruiter/RecruiterDeleteById/{id}");
        }
    }
}