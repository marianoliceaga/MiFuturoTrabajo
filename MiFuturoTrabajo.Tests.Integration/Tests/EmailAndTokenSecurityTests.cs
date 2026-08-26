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
    /// The account-takeover defect and the hardening around it.
    ///
    /// Finding 01 of the baseline audit: <c>/forgot-password</c> is anonymous
    /// and used to return the raw <c>resetToken</c> in its response body, so
    /// anyone who knew an email address could take the account. These tests
    /// exist so that cannot come back.
    /// </summary>
    public class EmailAndTokenSecurityTests : IClassFixture<TestWebApplicationFactory>
    {
        private readonly TestWebApplicationFactory _factory;
        private readonly HttpClient _client;

        public EmailAndTokenSecurityTests(TestWebApplicationFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }

        private async Task<(string Email, string AccessToken)> RegisterAsync()
        {
            var email = $"sec{Guid.NewGuid()}@example.com";

            var response = await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
            {
                Email = email,
                Password = "OriginalPassword123!",
                ConfirmPassword = "OriginalPassword123!",
                FirstName = "Sec",
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

        // ================================================================
        // The leak
        // ================================================================

        /// <summary>
        /// The old code returned the token whenever the host was in the
        /// Development environment. That is a security control resting on an
        /// environment variable being right, and it only had to be wrong once.
        /// The token is not in the response in <em>any</em> environment.
        /// </summary>
        [Fact]
        public async Task ForgotPassword_ResponseNeverCarriesTheToken()
        {
            var (email, _) = await RegisterAsync();

            var response = await _client.PostAsJsonAsync("/api/auth/forgot-password",
                new ForgotPasswordRequest { Email = email });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("resetToken", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("token", body, StringComparison.OrdinalIgnoreCase);

            // The token really was issued - the response simply does not carry it.
            var emailed = _factory.Emails.LastTo(email);
            Assert.NotNull(emailed);
            Assert.NotNull(emailed!.LinkParameter("token"));
        }

        [Fact]
        public async Task SendVerification_ResponseNeverCarriesTheToken()
        {
            var (email, accessToken) = await RegisterAsync();

            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await client.PostAsync("/api/auth/send-verification", null);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("verificationToken", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("token", body, StringComparison.OrdinalIgnoreCase);

            Assert.NotNull(_factory.Emails.LastTo(email));
        }

        /// <summary>
        /// The attack the leak enabled, end to end: know an address, ask for a
        /// reset, read the token out of the reply, own the account. It must
        /// fail at the second step.
        /// </summary>
        [Fact]
        public async Task AnonymousCallerCannotTakeOverAnAccountFromTheResponseAlone()
        {
            var (email, _) = await RegisterAsync();

            var forgot = await _client.PostAsJsonAsync("/api/auth/forgot-password",
                new ForgotPasswordRequest { Email = email });

            var body = await forgot.Content.ReadAsStringAsync();

            // Everything an attacker could scrape from that response.
            var candidates = System.Text.RegularExpressions.Regex
                .Matches(body, "[A-Za-z0-9_-]{16,}")
                .Select(m => m.Value)
                .ToList();

            foreach (var candidate in candidates)
            {
                var attempt = await _client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest
                {
                    Email = email,
                    ResetToken = candidate,
                    NewPassword = "AttackerPassword123!",
                    ConfirmPassword = "AttackerPassword123!"
                });

                Assert.Equal(HttpStatusCode.BadRequest, attempt.StatusCode);
            }

            // The original password still works.
            var login = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
            {
                Email = email,
                Password = "OriginalPassword123!"
            });

            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        }

        // ================================================================
        // Tokens at rest
        // ================================================================

        [Fact]
        public async Task ResetToken_IsStoredOnlyAsAHash()
        {
            var (email, _) = await RegisterAsync();

            await _client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest { Email = email });

            var issued = _factory.Emails.LastTo(email)!.LinkParameter("token");
            var stored = await LoadUserAsync(email);

            Assert.NotNull(issued);
            Assert.NotNull(stored!.PasswordResetTokenHash);
            Assert.NotEqual(issued, stored.PasswordResetTokenHash);
        }

        /// <summary>
        /// Someone with read access to the database holds the hash. Presenting
        /// it must not work, or hashing bought nothing.
        /// </summary>
        [Fact]
        public async Task StoredHashCannotBeReplayedAsAToken()
        {
            var (email, _) = await RegisterAsync();

            await _client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest { Email = email });

            var stored = await LoadUserAsync(email);

            var attempt = await _client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest
            {
                Email = email,
                ResetToken = stored!.PasswordResetTokenHash!,
                NewPassword = "AttackerPassword123!",
                ConfirmPassword = "AttackerPassword123!"
            });

            Assert.Equal(HttpStatusCode.BadRequest, attempt.StatusCode);
        }

        [Fact]
        public async Task ResetToken_IsSingleUse()
        {
            var (email, _) = await RegisterAsync();

            await _client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest { Email = email });
            var token = _factory.Emails.LastTo(email)!.LinkParameter("token");

            var request = new ResetPasswordRequest
            {
                Email = email,
                ResetToken = token!,
                NewPassword = "ResetPassword123!",
                ConfirmPassword = "ResetPassword123!"
            };

            var first = await _client.PostAsJsonAsync("/api/auth/reset-password", request);
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);

            var second = await _client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest
            {
                Email = email,
                ResetToken = token!,
                NewPassword = "ThirdPassword123!",
                ConfirmPassword = "ThirdPassword123!"
            });

            Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        }

        [Fact]
        public async Task ResetToken_OfOneAccountDoesNotWorkOnAnother()
        {
            var (victim, _) = await RegisterAsync();
            var (attacker, _) = await RegisterAsync();

            await _client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest { Email = attacker });
            var attackerToken = _factory.Emails.LastTo(attacker)!.LinkParameter("token");

            var attempt = await _client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest
            {
                Email = victim,
                ResetToken = attackerToken!,
                NewPassword = "AttackerPassword123!",
                ConfirmPassword = "AttackerPassword123!"
            });

            Assert.Equal(HttpStatusCode.BadRequest, attempt.StatusCode);
        }

        // ================================================================
        // Verification
        // ================================================================

        [Fact]
        public async Task Register_SendsAVerificationEmailWithAWorkingLink()
        {
            var (email, _) = await RegisterAsync();

            var message = _factory.Emails.LastTo(email);
            Assert.NotNull(message);
            Assert.Contains("Verify", message!.Subject, StringComparison.OrdinalIgnoreCase);

            var token = message.LinkParameter("token");
            var linkEmail = message.LinkParameter("email");

            Assert.NotNull(token);
            Assert.Equal(email, linkEmail);

            var verify = await _client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest
            {
                Email = linkEmail!,
                VerificationToken = token!
            });

            Assert.Equal(HttpStatusCode.OK, verify.StatusCode);

            var user = await LoadUserAsync(email);
            Assert.True(user!.EmailConfirmed);
            Assert.Null(user.EmailConfirmationTokenHash);
        }

        [Fact]
        public async Task VerificationToken_HasAnExpiry()
        {
            var (email, _) = await RegisterAsync();

            var user = await LoadUserAsync(email);

            // Previously absent entirely, so verification links never expired.
            Assert.NotNull(user!.EmailConfirmationTokenExpiry);
            Assert.True(user.EmailConfirmationTokenExpiry > DateTime.UtcNow);
            Assert.True(user.EmailConfirmationTokenExpiry < DateTime.UtcNow.AddDays(2));
        }

        [Fact]
        public async Task VerifyEmail_WithAWrongToken_IsRejected()
        {
            var (email, _) = await RegisterAsync();

            var response = await _client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest
            {
                Email = email,
                VerificationToken = SecurityTokensStub
            });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        /// <summary>
        /// The old handler indexed a dictionary, so a body without the expected
        /// key threw and surfaced as a 500 rather than a 400.
        /// </summary>
        [Fact]
        public async Task VerifyEmail_WithAnEmptyBody_IsABadRequestNotACrash()
        {
            var response = await _client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task VerifyEmail_ForAnUnknownAddress_LooksTheSameAsAWrongToken()
        {
            var (email, _) = await RegisterAsync();

            var unknown = await _client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest
            {
                Email = $"nobody{Guid.NewGuid()}@example.com",
                VerificationToken = SecurityTokensStub
            });

            var wrongToken = await _client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest
            {
                Email = email,
                VerificationToken = SecurityTokensStub
            });

            // Same status and same body: otherwise this endpoint tells an
            // attacker which addresses have accounts.
            Assert.Equal(wrongToken.StatusCode, unknown.StatusCode);
            Assert.Equal(
                await wrongToken.Content.ReadAsStringAsync(),
                await unknown.Content.ReadAsStringAsync());
        }

        // ================================================================
        // Enumeration
        // ================================================================

        [Fact]
        public async Task ForgotPassword_SaysTheSameThingForKnownAndUnknownAddresses()
        {
            var (known, _) = await RegisterAsync();

            var knownResponse = await _client.PostAsJsonAsync("/api/auth/forgot-password",
                new ForgotPasswordRequest { Email = known });

            var unknownResponse = await _client.PostAsJsonAsync("/api/auth/forgot-password",
                new ForgotPasswordRequest { Email = $"nobody{Guid.NewGuid()}@example.com" });

            Assert.Equal(knownResponse.StatusCode, unknownResponse.StatusCode);
            Assert.Equal(
                await knownResponse.Content.ReadAsStringAsync(),
                await unknownResponse.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task ForgotPassword_ForAnUnknownAddress_SendsNothing()
        {
            var before = _factory.Emails.Count;

            await _client.PostAsJsonAsync("/api/auth/forgot-password",
                new ForgotPasswordRequest { Email = $"nobody{Guid.NewGuid()}@example.com" });

            Assert.Equal(before, _factory.Emails.Count);
        }

        // ================================================================
        // Session handling
        // ================================================================

        /// <summary>
        /// Resetting a password is what someone does when they think an account
        /// is compromised. Leaving the old refresh token alive would leave the
        /// intruder logged in.
        /// </summary>
        [Fact]
        public async Task ResetPassword_InvalidatesTheExistingRefreshToken()
        {
            var (email, _) = await RegisterAsync();

            var login = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
            {
                Email = email,
                Password = "OriginalPassword123!"
            });

            var session = await login.Content.ReadFromJsonAsync<AuthResponse>();

            await _client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest { Email = email });
            var token = _factory.Emails.LastTo(email)!.LinkParameter("token");

            await _client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest
            {
                Email = email,
                ResetToken = token!,
                NewPassword = "ResetPassword123!",
                ConfirmPassword = "ResetPassword123!"
            });

            var stored = await LoadUserAsync(email);
            Assert.Null(stored!.RefreshToken);
        }

        [Fact]
        public async Task ResetPassword_AlsoConfirmsTheEmailAddress()
        {
            var (email, _) = await RegisterAsync();

            Assert.False((await LoadUserAsync(email))!.EmailConfirmed);

            await _client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest { Email = email });
            var token = _factory.Emails.LastTo(email)!.LinkParameter("token");

            await _client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest
            {
                Email = email,
                ResetToken = token!,
                NewPassword = "ResetPassword123!",
                ConfirmPassword = "ResetPassword123!"
            });

            // Clicking a link sent to that mailbox proves control of it, which
            // is exactly what verification asks for.
            Assert.True((await LoadUserAsync(email))!.EmailConfirmed);
        }

        // ================================================================
        // Message content
        // ================================================================

        /// <summary>
        /// Names are user-supplied and land inside markup in someone else's
        /// inbox, so they must be encoded on the way in.
        /// </summary>
        [Fact]
        public async Task EmailBody_EncodesUserSuppliedNames()
        {
            var email = $"xss{Guid.NewGuid()}@example.com";

            await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
            {
                Email = email,
                Password = "OriginalPassword123!",
                ConfirmPassword = "OriginalPassword123!",
                FirstName = "<script>alert(1)</script>",
                LastName = "Tester"
            });

            var body = _factory.Emails.LastTo(email)!.HtmlBody;

            Assert.DoesNotContain("<script>", body);
            Assert.Contains("&lt;script&gt;", body);
        }

        // ================================================================
        // Email normalization
        // ================================================================

        /// <summary>
        /// Email is the Cosmos partition key and Cosmos enforces no unique
        /// index, so casing differences do not collide - they create a second
        /// account in a second partition for the same person.
        /// </summary>
        [Fact]
        public async Task Register_TreatsAddressesThatDifferOnlyByCaseAsTheSameAccount()
        {
            var address = $"Case{Guid.NewGuid()}@Example.COM";

            var first = await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
            {
                Email = address,
                Password = "OriginalPassword123!",
                ConfirmPassword = "OriginalPassword123!",
                FirstName = "Case",
                LastName = "Tester"
            });

            Assert.Equal(HttpStatusCode.Created, first.StatusCode);

            var second = await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
            {
                Email = address.ToLowerInvariant(),
                Password = "OriginalPassword123!",
                ConfirmPassword = "OriginalPassword123!",
                FirstName = "Case",
                LastName = "Duplicate"
            });

            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        }

        [Fact]
        public async Task Login_AcceptsAnyCasingOfTheRegisteredAddress()
        {
            var address = $"login{Guid.NewGuid()}@example.com";

            await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
            {
                Email = address,
                Password = "OriginalPassword123!",
                ConfirmPassword = "OriginalPassword123!",
                FirstName = "Case",
                LastName = "Tester"
            });

            var login = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
            {
                Email = address.ToUpperInvariant(),
                Password = "OriginalPassword123!"
            });

            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        }

        private const string SecurityTokensStub = "not-a-real-token-but-long-enough-to-look-like-one";
    }
}
