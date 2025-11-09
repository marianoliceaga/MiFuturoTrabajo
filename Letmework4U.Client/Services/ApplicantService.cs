using Letmework4U.Shared.Models;
using System.Net.Http.Json;

namespace Letmework4U.Client.Services
{
    public class ApplicantService : IApplicantService
    {
        private readonly HttpClient _http;
        private const string CURRENT_APPLICANT_KEY = "currentApplicantId";

        public ApplicantService(HttpClient http)
        {
            _http = http;
        }

        public async Task<ApplicantDto> GetCurrentApplicantAsync()
        {
            // Get current applicant using the /current endpoint from API
            return await _http.GetFromJsonAsync<ApplicantDto>("applicant/current");
        }

        public async Task<ApplicantDto> UpdateProfileAsync(ApplicantDto applicant)
        {
            var response = await _http.PatchAsJsonAsync($"applicant/{applicant.Id}", applicant);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<ApplicantDto>();
        }

        public async Task<CVVersionDto> UploadCVAsync(string name, Stream fileStream)
        {
            // Implementation for file upload
            var content = new MultipartFormDataContent();
            var streamContent = new StreamContent(fileStream);
            content.Add(streamContent, "file", name);

            var response = await _http.PostAsync("applicant/UploadCV", content);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<CVVersionDto>();
        }

        public async Task<JobReferenceDto> AddReferenceAsync(JobReferenceDto reference)
        {
            var response = await _http.PostAsJsonAsync("applicant/AddReference", reference);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<JobReferenceDto>();
        }

        public async Task DeleteReferenceAsync(string referenceId)
        {
            var response = await _http.DeleteAsync($"applicant/DeleteReference/{referenceId}");
            response.EnsureSuccessStatusCode();
        }
    }
}