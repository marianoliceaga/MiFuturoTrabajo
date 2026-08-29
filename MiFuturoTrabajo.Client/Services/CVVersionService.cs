using MiFuturoTrabajo.Shared.Models;
using System.Net.Http.Json;

namespace MiFuturoTrabajo.Client.Services
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
            return await _http.GetListAsync<CVVersionDto>("cvversion");
        }

        public async Task<CVVersionDto> GetByIdAsync(string id)
        {
            return await _http.GetRequiredAsync<CVVersionDto>($"cvversion/{id}");
        }

        public async Task<CVVersionDto> CreateAsync(CVVersionDto cvVersion)
        {
            var response = await _http.PostAsJsonAsync("cvversion", cvVersion);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadRequiredAsync<CVVersionDto>("cvversion");
        }

        public async Task<CVVersionDto> UpdateAsync(string id, CVVersionDto cvVersion)
        {
            var response = await _http.PatchAsJsonAsync($"cvversion/{id}", cvVersion);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadRequiredAsync<CVVersionDto>($"cvversion/{id}");
        }

        public async Task<CVVersionDto> SetDefaultAsync(string id)
        {
            var response = await _http.PatchAsJsonAsync($"cvversion/{id}/setDefault", new { });
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadRequiredAsync<CVVersionDto>($"cvversion/{id}/setDefault");
        }

        public async Task DeleteAsync(string id)
        {
            await _http.DeleteAsync($"cvversion/{id}");
        }

        public async Task<CVVersionDto> UploadAsync(string name, string fileName, string contentType, Stream fileStream)
        {
            using var content = CvUploadContent.Build(name, fileName, contentType, fileStream);

            var response = await _http.PostAsync("cvversion/upload", content);
            await CvUploadContent.ThrowIfRejectedAsync(response);

            return await response.Content.ReadRequiredAsync<CVVersionDto>("cvversion/upload");
        }

        public async Task<CVDownloadLinkDto> GetDownloadLinkAsync(string id)
        {
            return await _http.GetRequiredAsync<CVDownloadLinkDto>($"cvversion/{id}/download");
        }
    }
}
