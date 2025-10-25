using Letmework4U.Shared.Models;

namespace Letmework4U.Client.Services
{
    
    public class AuthService : IAuthService
    {
        private readonly HttpClient _http;
        private ApplicantDto _currentApplicant;

        public AuthService(HttpClient http)
        {
            _http = http;
        }

        public async Task<ApplicantDto> LoginAsync(string email, string password)
        {
            // Simplified - in real app, implement proper authentication
            var response = await _http.PostAsJsonAsync("Auth/Login", new { email, password });
            if (response.IsSuccessStatusCode)
            {
                _currentApplicant = await response.Content.ReadFromJsonAsync<ApplicantDto>();
                return _currentApplicant;
            }
            return null;
        }

        public Task LogoutAsync()
        {
            _currentApplicant = null;
            return Task.CompletedTask;
        }

        public Task<bool> IsAuthenticatedAsync()
        {
            return Task.FromResult(_currentApplicant != null);
        }

        public Task<string> GetCurrentApplicantIdAsync()
        {
            return Task.FromResult(_currentApplicant?.Id ?? string.Empty);
        }
    }
}