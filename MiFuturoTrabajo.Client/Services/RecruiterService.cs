using MiFuturoTrabajo.Shared.Models;
using System.Net.Http.Json;

namespace MiFuturoTrabajo.Client.Services
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
            return await _http.GetListAsync<RecruiterDto>("recruiters");
        }

        public async Task<RecruiterDto> GetByIdAsync(string id)
        {
            return await _http.GetRequiredAsync<RecruiterDto>($"recruiters/RecruiterGetById/{id}");
        }

        public async Task<RecruiterDto> CreateAsync(RecruiterDto recruiter)
        {
            var response = await _http.PostAsJsonAsync("recruiters/RecruiterAdd", recruiter);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadRequiredAsync<RecruiterDto>("recruiters/RecruiterAdd");
        }

        public async Task<RecruiterDto> UpdateAsync(string id, RecruiterDto recruiter)
        {
            var response = await _http.PatchAsJsonAsync($"recruiters/{id}", recruiter);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadRequiredAsync<RecruiterDto>($"recruiters/{id}");
        }

        public async Task DeleteAsync(string id)
        {
            await _http.DeleteAsync($"recruiters/RecruiterDeleteById/{id}");
        }
    }
}