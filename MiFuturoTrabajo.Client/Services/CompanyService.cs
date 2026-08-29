using MiFuturoTrabajo.Shared.Models;
using System.Net.Http.Json;

namespace MiFuturoTrabajo.Client.Services
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
            return await _http.GetListAsync<CompanyDto>("companies");
        }

        public async Task<CompanyDto> GetByIdAsync(string id)
        {
            return await _http.GetRequiredAsync<CompanyDto>($"companies/CompanyGetById/{id}");
        }

        public async Task<CompanyDto> CreateAsync(CompanyDto company)
        {
            var response = await _http.PostAsJsonAsync("companies/CompanyAdd", company);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadRequiredAsync<CompanyDto>("companies/CompanyAdd");
        }

        public async Task<CompanyDto> UpdateAsync(string id, CompanyDto company)
        {
            var response = await _http.PatchAsJsonAsync($"companies/{id}", company);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadRequiredAsync<CompanyDto>($"companies/{id}");
        }

        public async Task DeleteAsync(string id)
        {
            await _http.DeleteAsync($"companies/{id}");
        }

        public async Task<List<CompanyDto>> SearchAsync(string searchTerm)
        {
            try
            {
                var all = await GetAllAsync();
                return all.Where(c => c.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                         .ToList();
            }
            catch (Exception)
            {
                // Return empty list on error to prevent UI crash
                return new List<CompanyDto>();
            }
        }
    }
}