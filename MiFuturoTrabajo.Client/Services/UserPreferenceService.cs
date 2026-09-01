using System.Net.Http.Json;
using MiFuturoTrabajo.Shared.Models;

namespace MiFuturoTrabajo.Client.Services
{
    public interface IUserPreferenceService
    {
        /// <summary>
        /// Mirrors the chosen language onto the account, and reports whether it
        /// landed. Never throws: the browser already has the preference, so a
        /// server that is asleep or a user who is not signed in must not turn a
        /// language change into an error.
        /// </summary>
        Task<bool> TrySaveLanguageAsync(string languageCode);
    }

    /// <summary>
    /// Pushes the language preference to the account.
    ///
    /// The browser is the source of truth for what the UI renders in - that is
    /// what localStorage holds and what survives a signed-out visit. The server
    /// copy exists for one reason: the reminder emails are composed by an Azure
    /// Functions timer trigger, which has no request and therefore no browser to
    /// ask. Without this the daily digest would always go out in the default
    /// language regardless of what the recipient reads.
    /// </summary>
    public class UserPreferenceService : IUserPreferenceService
    {
        private const string Endpoint = "auth/preferences";

        private readonly HttpClient _http;

        public UserPreferenceService(HttpClient http)
        {
            _http = http;
        }

        public async Task<bool> TrySaveLanguageAsync(string languageCode)
        {
            try
            {
                var response = await _http.PutAsJsonAsync(
                    Endpoint, new UpdatePreferencesRequest { PreferredLanguage = languageCode });

                if (response.IsSuccessStatusCode)
                {
                    return true;
                }

                Console.WriteLine($"Could not save language preference: {(int)response.StatusCode}");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not save language preference: {ex.Message}");
                return false;
            }
        }
    }
}
