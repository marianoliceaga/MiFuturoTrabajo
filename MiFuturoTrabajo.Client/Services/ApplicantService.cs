using MiFuturoTrabajo.Shared.Models;
using System.Net.Http.Json;

namespace MiFuturoTrabajo.Client.Services
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
            return await _http.GetRequiredAsync<ApplicantDto>("applicant/current");
        }

        public async Task<ApplicantDto> UpdateProfileAsync(ApplicantDto applicant)
        {
            var response = await _http.PatchAsJsonAsync($"applicant/{applicant.Id}", applicant);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadRequiredAsync<ApplicantDto>($"applicant/{applicant.Id}");
        }

        public async Task<CVVersionDto> UploadCVAsync(string name, string fileName, string contentType, Stream fileStream)
        {
            using var content = CvUploadContent.Build(name, fileName, contentType, fileStream);

            var response = await _http.PostAsync("applicant/UploadCV", content);
            await CvUploadContent.ThrowIfRejectedAsync(response);

            return await response.Content.ReadRequiredAsync<CVVersionDto>("applicant/UploadCV");
        }

        public async Task<CVDownloadLinkDto> GetCVDownloadLinkAsync(string cvId)
        {
            return await _http.GetRequiredAsync<CVDownloadLinkDto>($"applicant/CV/{cvId}/download");
        }

        public async Task<JobReferenceDto> AddReferenceAsync(JobReferenceDto reference)
        {
            var response = await _http.PostAsJsonAsync("applicant/AddReference", reference);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadRequiredAsync<JobReferenceDto>("applicant/AddReference");
        }

        public async Task DeleteReferenceAsync(string referenceId)
        {
            var response = await _http.DeleteAsync($"applicant/DeleteReference/{referenceId}");
            response.EnsureSuccessStatusCode();
        }
    }
}