using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiFuturoTrabajo.Infrastructure;
using MiFuturoTrabajo.Infrastructure.Entities;
using MiFuturoTrabajo.Shared.Models;
using Xunit;

namespace MiFuturoTrabajo.Tests.Integration.Tests
{
    /// <summary>
    /// The account's stored language.
    ///
    /// The browser is what decides which language the UI renders in, and it
    /// needs no server for that. This endpoint exists for the one path with no
    /// browser to ask: the reminder timer triggers compose email with no
    /// request in flight, so the preference has to be readable from the user
    /// document or every digest goes out in the default language.
    /// </summary>
    public class LanguagePreferenceTests : IClassFixture<TestWebApplicationFactory>
    {
        private readonly TestWebApplicationFactory _factory;
        private readonly HttpClient _client;

        public LanguagePreferenceTests(TestWebApplicationFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }

        private async Task<(string Email, string AccessToken)> RegisterAsync()
        {
            var email = $"pref{Guid.NewGuid()}@example.com";

            var response = await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
            {
                Email = email,
                Password = "OriginalPassword123!",
                ConfirmPassword = "OriginalPassword123!",
                FirstName = "Pref",
                LastName = "Tester"
            });

            var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
            return (email, auth!.AccessToken);
        }

        private async Task<User?> LoadUserAsync(string email)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return await db.Users.FirstOrDefaultAsync(u => u.Email == email.ToLowerInvariant());
        }

        private async Task<HttpResponseMessage> PutPreferenceAsync(string? accessToken, string language)
        {
            var request = new HttpRequestMessage(HttpMethod.Put, "/api/auth/preferences")
            {
                Content = JsonContent.Create(new UpdatePreferencesRequest { PreferredLanguage = language })
            };

            if (accessToken != null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }

            return await _client.SendAsync(request);
        }

        [Theory]
        [InlineData("en")]
        [InlineData("es")]
        public async Task Preferences_StoreTheChosenLanguage(string language)
        {
            var (email, token) = await RegisterAsync();

            var response = await PutPreferenceAsync(token, language);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

            var user = await LoadUserAsync(email);
            Assert.Equal(language, user!.PreferredLanguage);
        }

        /// <summary>
        /// Rejected rather than coerced. This value picks an email template, so
        /// quietly storing something unrecognised would turn a client bug into a
        /// wrong-language email weeks later, far from its cause.
        /// </summary>
        [Theory]
        [InlineData("fr")]
        [InlineData("en-GB")]
        [InlineData("")]
        [InlineData("' OR 1=1")]
        public async Task Preferences_RejectAnythingButTheTwoSupportedCodes(string language)
        {
            var (email, token) = await RegisterAsync();

            var before = (await LoadUserAsync(email))!.PreferredLanguage;

            var response = await PutPreferenceAsync(token, language);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var after = (await LoadUserAsync(email))!.PreferredLanguage;
            Assert.Equal(before, after);
        }

        [Fact]
        public async Task Preferences_RequireAuthentication()
        {
            var response = await PutPreferenceAsync(accessToken: null, language: "en");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        /// <summary>
        /// One person's setting must not reach another's document. The endpoint
        /// takes no user id - it reads the caller's own token - so this is the
        /// test that would catch someone adding one.
        /// </summary>
        [Fact]
        public async Task Preferences_OnlyTouchTheCallersOwnAccount()
        {
            var (mine, myToken) = await RegisterAsync();
            var (theirs, _) = await RegisterAsync();

            Assert.Equal(HttpStatusCode.NoContent, (await PutPreferenceAsync(myToken, "en")).StatusCode);

            Assert.Equal("en", (await LoadUserAsync(mine))!.PreferredLanguage);
            Assert.NotEqual("en", (await LoadUserAsync(theirs))!.PreferredLanguage);
        }
    }
}
