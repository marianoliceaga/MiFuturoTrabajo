using Letmework4U.Shared.Models;
using System.Net.Http.Json;

namespace Letmework4U.Client.Services
{
    public class CompanyService : ICompanyService
    {
        private readonly HttpClient _http;

        public CompanyService(HttpClient http)
        {
            _http = http;
        }

        public async Task<List<CompanyDto>> GetAllAsync()
        {
            return await _http.GetFromJsonAsync<List<CompanyDto>>("Companies") ?? new List<CompanyDto>();
        }

        public async Task<CompanyDto> GetByIdAsync(string id)
        {
            return await _http.GetFromJsonAsync<CompanyDto>($"Companies/CompanyGetById/{id}");
        }

        public async Task<CompanyDto> CreateAsync(CompanyDto company)
        {
            var response = await _http.PostAsJsonAsync("Companies/CompanyAdd", company);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<CompanyDto>();
        }

        public async Task<CompanyDto> UpdateAsync(string id, CompanyDto company)
        {
            var response = await _http.PatchAsJsonAsync($"Companies/{id}", company);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<CompanyDto>();
        }

        public async Task<List<CompanyDto>> SearchAsync(string searchTerm)
        {
            var all = await GetAllAsync();
            return all.Where(c => c.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                     .ToList();
        }
    }
}