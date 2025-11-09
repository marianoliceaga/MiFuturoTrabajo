using Letmework4U.Shared.Models;
using System.Net.Http.Json;

namespace Letmework4U.Client.Services
{
    public class CVVersionService : ICVVersionService
    {
        private readonly HttpClient _http;

        public CVVersionService(HttpClient http)
        {
            _http = http;
        }

        public async Task<List<CVVersionDto>> GetAllAsync()
        {
            return await _http.GetFromJsonAsync<List<CVVersionDto>>("cvversion") ?? new List<CVVersionDto>();
        }

        public async Task<CVVersionDto> GetByIdAsync(string id)
        {
            return await _http.GetFromJsonAsync<CVVersionDto>($"cvversion/{id}");
        }

        public async Task<CVVersionDto> CreateAsync(CVVersionDto cvVersion)
        {
            var response = await _http.PostAsJsonAsync("cvversion", cvVersion);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<CVVersionDto>();
        }

        public async Task<CVVersionDto> UpdateAsync(string id, CVVersionDto cvVersion)
        {
            var response = await _http.PatchAsJsonAsync($"cvversion/{id}", cvVersion);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<CVVersionDto>();
        }

        public async Task<CVVersionDto> SetDefaultAsync(string id)
        {
            var response = await _http.PatchAsJsonAsync($"cvversion/{id}/setDefault", new { });
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<CVVersionDto>();
        }

        public async Task DeleteAsync(string id)
        {
            await _http.DeleteAsync($"cvversion/{id}");
        }
    }
}
