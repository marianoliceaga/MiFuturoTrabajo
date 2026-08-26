using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiFuturoTrabajo.Api.Services;
using MiFuturoTrabajo.Infrastructure;
using MiFuturoTrabajo.Infrastructure.Entities;
using MiFuturoTrabajo.Shared.Models;
using Xunit;

namespace MiFuturoTrabajo.Tests.Integration.Tests
{
    /// <summary>
    /// A validator that returns whatever the test tells it to.
    ///
    /// Signature verification is covered by <see cref="OAuthSecurityTests"/>,
    /// which attacks it with forged tokens. This fixture asks the next
    /// question: given a token the provider really did sign, what does the API
    /// do with the account? That is not reachable through the real validator
    /// without Google's or Entra's private keys.
    /// </summary>
    public class StubOAuthService : IOAuthService
    {
        public OAuthUserInfo? NextResult { get; set; }

        public Task<OAuthUserInfo?> ValidateOAuthTokenAsync(OAuthProvider provider, string idToken) =>
            Task.FromResult(NextResult);

        public Task<OAuthUserInfo?> ValidateGoogleTokenAsync(string idToken) => Task.FromResult(NextResult);

        public Task<OAuthUserInfo?> ValidateMicrosoftTokenAsync(string idToken) => Task.FromResult(NextResult);
    }

    public class StubbedOAuthFactory : TestWebApplicationFactory
    {
        public StubOAuthService OAuth { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IOAuthService));
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }

                services.AddSingleton<IOAuthService>(OAuth);
            });
        }
    }

    /// <summary>
    /// The matrix the roadmap asked for: new user via each provider, existing
    /// password user signing in socially, and the same person coming back.
    /// </summary>
    public class OAuthAccountLinkingTests : IClassFixture<StubbedOAuthFactory>
    {
        private readonly StubbedOAuthFactory _factory;
        private readonly HttpClient _client;

        public OAuthAccountLinkingTests(StubbedOAuthFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }

        private async Task<User?> LoadUserAsync(string email)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return await db.Users.FirstOrDefaultAsync(u => u.Email == email.ToLowerInvariant());
        }

        private async Task<int> CountUsersAsync(string email)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return await db.Users.CountAsync(u => u.Email == email.ToLowerInvariant());
        }

        private async Task RegisterWithPasswordAsync(string email)
        {
            var response = await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
            {
                Email = email,
                Password = "OriginalPassword123!",
                ConfirmPassword = "OriginalPassword123!",
                FirstName = "Real",
                LastName = "Owner"
            });

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        private Task<HttpResponseMessage> SignInAsync(
            string email,
            OAuthProvider provider = OAuthProvider.Google,
            bool emailVerified = true,
            string? providerId = null)
        {
            _factory.OAuth.NextResult = new OAuthUserInfo
            {
                Email = email,
                FirstName = "Social",
                LastName = "User",
                Provider = provider,
                ProviderId = providerId ?? $"provider-{email}",
                EmailVerified = emailVerified
            };

            return _client.PostAsJsonAsync("/api/auth/oauth-login", new OAuthLoginRequest
            {
                IdToken = "stubbed-and-therefore-irrelevant",
                Provider = provider
            });
        }

        // ================================================================
        // New users
        // ================================================================

        [Theory]
        [InlineData(OAuthProvider.Google)]
        [InlineData(OAuthProvider.Microsoft)]
        public async Task NewUser_SigningInSocially_GetsAnAccount(OAuthProvider provider)
        {
            var email = $"new{Guid.NewGuid()}@example.com";

            var response = await SignInAsync(email, provider);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
            Assert.NotNull(auth);
            Assert.False(string.IsNullOrWhiteSpace(auth!.AccessToken));

            var user = await LoadUserAsync(email);
            Assert.NotNull(user);
            Assert.Equal(provider.ToString(), user!.OAuthProvider);
            Assert.Null(user.PasswordHash);
        }

        /// <summary>
        /// Registration creates an applicant record; the OAuth path did not, so
        /// a social sign-up landed on a profile page with nothing behind it.
        /// </summary>
        [Fact]
        public async Task NewUser_SigningInSocially_AlsoGetsAnApplicantRecord()
        {
            var email = $"applicant{Guid.NewGuid()}@example.com";

            await SignInAsync(email);

            var user = await LoadUserAsync(email);
            Assert.NotNull(user!.ApplicantId);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var applicant = await db.Applicants.FirstOrDefaultAsync(a => a.UserId == user.Id);

            Assert.NotNull(applicant);
            Assert.Equal(email.ToLowerInvariant(), applicant!.ContactEmail.ToLowerInvariant());
        }

        [Fact]
        public async Task ReturningSocialUser_DoesNotGetASecondAccount()
        {
            var email = $"returning{Guid.NewGuid()}@example.com";

            Assert.Equal(HttpStatusCode.OK, (await SignInAsync(email)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await SignInAsync(email)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await SignInAsync(email)).StatusCode);

            Assert.Equal(1, await CountUsersAsync(email));
        }

        /// <summary>
        /// Email is the Cosmos partition key and Cosmos enforces no unique
        /// index, so differing casing would silently produce a second account
        /// rather than a conflict.
        /// </summary>
        [Fact]
        public async Task SocialSignIn_WithDifferentCasing_FindsTheSameAccount()
        {
            var email = $"Casing{Guid.NewGuid()}@Example.COM";

            await SignInAsync(email);
            await SignInAsync(email.ToLowerInvariant());

            Assert.Equal(1, await CountUsersAsync(email));
        }

        // ================================================================
        // Linking onto an account that already exists
        // ================================================================

        [Fact]
        public async Task ExistingPasswordUser_WithAVerifiedAddress_IsLinkedNotDuplicated()
        {
            var email = $"link{Guid.NewGuid()}@example.com";
            await RegisterWithPasswordAsync(email);

            var response = await SignInAsync(email, OAuthProvider.Google, emailVerified: true);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(1, await CountUsersAsync(email));

            var user = await LoadUserAsync(email);
            Assert.Equal("Google", user!.OAuthProvider);

            // The password still works - linking adds a way in, it does not
            // replace the existing one.
            Assert.NotNull(user.PasswordHash);

            var login = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
            {
                Email = email,
                Password = "OriginalPassword123!"
            });

            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        }

        /// <summary>
        /// The nOAuth pattern. Entra does not promise the <c>email</c> claim is
        /// verified for a multi-tenant app: anyone can create a tenant, set a
        /// user's mail attribute to somebody else's address, and sign in. If
        /// that linked onto an existing account it would be a takeover with a
        /// perfectly valid signature.
        /// </summary>
        [Fact]
        public async Task ExistingPasswordUser_WithAnUnverifiedAddress_IsRefused()
        {
            var email = $"noauth{Guid.NewGuid()}@example.com";
            await RegisterWithPasswordAsync(email);

            var response = await SignInAsync(email, OAuthProvider.Microsoft, emailVerified: false);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

            var user = await LoadUserAsync(email);
            Assert.Null(user!.OAuthProvider);

            // And the real owner is unaffected.
            var login = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
            {
                Email = email,
                Password = "OriginalPassword123!"
            });

            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        }

        [Fact]
        public async Task LinkingAProvider_NotifiesTheAccountOwner()
        {
            var email = $"notify{Guid.NewGuid()}@example.com";
            await RegisterWithPasswordAsync(email);

            _factory.Emails.Clear();

            await SignInAsync(email, OAuthProvider.Google, emailVerified: true);

            var message = _factory.Emails.LastTo(email);
            Assert.NotNull(message);
            Assert.Contains("Google", message!.Subject);
        }

        [Fact]
        public async Task LinkingAVerifiedProvider_ConfirmsTheEmailAddress()
        {
            var email = $"confirm{Guid.NewGuid()}@example.com";
            await RegisterWithPasswordAsync(email);

            Assert.False((await LoadUserAsync(email))!.EmailConfirmed);

            await SignInAsync(email, OAuthProvider.Google, emailVerified: true);

            Assert.True((await LoadUserAsync(email))!.EmailConfirmed);
        }

        /// <summary>
        /// A brand-new account from an unverified address is fine - there is no
        /// existing account to take over, and nothing is claimed about the
        /// address until something depends on it.
        /// </summary>
        [Fact]
        public async Task NewUser_WithAnUnverifiedAddress_IsAllowedButNotConfirmed()
        {
            var email = $"unverified{Guid.NewGuid()}@example.com";

            var response = await SignInAsync(email, OAuthProvider.Microsoft, emailVerified: false);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var user = await LoadUserAsync(email);
            Assert.NotNull(user);
            Assert.False(user!.EmailConfirmed);
        }

        [Fact]
        public async Task SwitchingProvider_WithAVerifiedAddress_Relinks()
        {
            var email = $"switch{Guid.NewGuid()}@example.com";

            await SignInAsync(email, OAuthProvider.Google, providerId: "google-123");
            Assert.Equal("Google", (await LoadUserAsync(email))!.OAuthProvider);

            await SignInAsync(email, OAuthProvider.Microsoft, providerId: "ms-456");

            var user = await LoadUserAsync(email);
            Assert.Equal("Microsoft", user!.OAuthProvider);
            Assert.Equal("ms-456", user.OAuthProviderId);
            Assert.Equal(1, await CountUsersAsync(email));
        }

        // ================================================================
        // Refusals
        // ================================================================

        [Fact]
        public async Task ARejectedTokenCreatesNothing()
        {
            _factory.OAuth.NextResult = null;

            var response = await _client.PostAsJsonAsync("/api/auth/oauth-login", new OAuthLoginRequest
            {
                IdToken = "whatever",
                Provider = OAuthProvider.Google
            });

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task ADeactivatedAccountCannotBeReopenedSocially()
        {
            var email = $"disabled{Guid.NewGuid()}@example.com";
            await SignInAsync(email);

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var user = await db.Users.FirstAsync(u => u.Email == email.ToLowerInvariant());
                user.IsActive = false;
                await db.SaveChangesAsync();
            }

            var response = await SignInAsync(email);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }
}
