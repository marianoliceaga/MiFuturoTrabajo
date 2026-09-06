using System.Net.Http.Json;
using MiFuturoTrabajo.Shared.Models;

namespace MiFuturoTrabajo.Client.Services
{
    public interface ICvTailoringService
    {
        /// <summary>
        /// Whether the feature is available and how much allowance is left.
        /// Asked before the user types anything, so the page can explain itself
        /// up front rather than failing at the button.
        /// </summary>
        Task<CvTailoringStatusDto> GetStatusAsync();

        Task<CvTailoringResultDto> TailorAsync(CvTailoringRequestDto request);
    }

    public class CvTailoringService : ICvTailoringService
    {
        private readonly HttpClient _http;

        public CvTailoringService(HttpClient http)
        {
            _http = http;
        }

        public Task<CvTailoringStatusDto> GetStatusAsync() =>
            _http.GetRequiredAsync<CvTailoringStatusDto>("cvtailoring/status");

        public async Task<CvTailoringResultDto> TailorAsync(CvTailoringRequestDto request)
        {
            var response = await _http.PostAsJsonAsync("cvtailoring", request);

            // Not EnsureSuccessStatusCode: every refusal this endpoint issues is
            // one the user can act on - the allowance is spent and renews on a
            // date, the description is too long by a stated amount, the CV is a
            // format that cannot be read. Collapsing those into "400 Bad
            // Request" would throw away the only useful part.
            if (!response.IsSuccessStatusCode)
            {
                var reason = await response.Content.ReadAsStringAsync();

                throw new HttpRequestException(
                    string.IsNullOrWhiteSpace(reason)
                        ? $"The request failed ({(int)response.StatusCode})."
                        : reason);
            }

            return await response.Content.ReadRequiredAsync<CvTailoringResultDto>("cvtailoring");
        }
    }
}
