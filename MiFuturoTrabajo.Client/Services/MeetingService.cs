using System.Net.Http.Json;
using MiFuturoTrabajo.Shared.Models;

namespace MiFuturoTrabajo.Client.Services
{
    public class MeetingService : IMeetingService
    {
        private readonly HttpClient _http;

        public MeetingService(HttpClient http)
        {
            _http = http;
        }

        public async Task<List<MeetingDto>> GetAllAsync()
        {
            return await _http.GetListAsync<MeetingDto>("meeting");
        }

        public async Task<List<MeetingDto>> GetByApplicantAsync(string applicantId)
        {
            var all = await GetAllAsync();
            return all.Where(m => m.ApplicantId == applicantId).ToList();
        }

        public async Task<MeetingDto> GetByIdAsync(string id)
        {
            return await _http.GetRequiredAsync<MeetingDto>($"meeting/MeetingGetById/{id}");
        }

        public async Task<MeetingDto> CreateAsync(MeetingDto meeting)
        {
            var response = await _http.PostAsJsonAsync("meeting/MeetingAdd", meeting);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadRequiredAsync<MeetingDto>("meeting/MeetingAdd");
        }

        public async Task<MeetingDto> UpdateAsync(string id, MeetingDto meeting)
        {
            var response = await _http.PatchAsJsonAsync($"meeting/{id}", meeting);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadRequiredAsync<MeetingDto>($"meeting/{id}");
        }

        public async Task DeleteAsync(string id)
        {
            await _http.DeleteAsync($"meeting/MeetingDeleteById/{id}");
        }

        public async Task<List<MeetingDto>> GetUpcomingAsync()
        {
            var all = await GetAllAsync();
            return all.Where(m => m.StartDateTime > DateTime.UtcNow)
                     .OrderBy(m => m.StartDateTime)
                     .ToList();
        }
    }
}