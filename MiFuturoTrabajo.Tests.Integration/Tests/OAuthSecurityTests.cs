using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using MiFuturoTrabajo.Infrastructure;
using MiFuturoTrabajo.Infrastructure.Entities;
using MiFuturoTrabajo.Shared.Models;
using Xunit;

namespace MiFuturoTrabajo.Tests.Integration.Tests
{
    /// <summary>
    /// OAuth sign-in, treated as what it is: an endpoint that accepts a bearer
    /// assertion from an anonymous caller and, if it believes it, issues a
    /// session for whatever email the assertion names.
    ///
    /// Everything a forger controls is in the token. The only thing they cannot
    /// forge is the provider's signature, so that signature is the entire
    /// security boundary - and these tests attack it directly.
    /// </summary>
    public class OAuthSecurityTests : IClassFixture<TestWebApplicationFactory>
    {
        private const string MicrosoftClientId = "11111111-2222-3333-4444-555555555555";

        private readonly TestWebApplicationFactory _factory;
        private readonly HttpClient _client;

        public OAuthSecurityTests(TestWebApplicationFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }

        /// <summary>
        /// Mints a token signed with a key the attacker generated - exactly what
        /// anyone can do. Every claim is chosen to satisfy the checks the old
        /// implementation performed after decoding.
        /// </summary>
        private static string ForgeMicrosoftToken(
            string email,
            string? audience = MicrosoftClientId,
            string? issuer = "https://login.microsoftonline.com/9188040d-6c67-4c5b-b112-36a304b66dad/v2.0",
            DateTime? expires = null)
        {
            var attackerKey = new RsaSecurityKey(RSA.Create(2048));

            var expiry = expires ?? DateTime.UtcNow.AddHours(1);

            var descriptor = new SecurityTokenDescriptor
            {
                Issuer = issuer,
                Audience = audience,

                // NotBefore has to precede Expires or the library refuses to
                // build the token, which matters when forging an expired one.
                NotBefore = expiry.AddHours(-2),
                IssuedAt = expiry.AddHours(-2),
                Expires = expiry,
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim("email", email),
                    new Claim("preferred_username", email),
                    new Claim("given_name", "Totally"),
                    new Claim("family_name", "Legitimate"),
                    new Claim("oid", Guid.NewGuid().ToString())
                }),
                SigningCredentials = new SigningCredentials(attackerKey, SecurityAlgorithms.RsaSha256)
            };

            return new JwtSecurityTokenHandler().CreateEncodedJwt(descriptor);
        }

        /// <summary>A token with no signature at all - alg "none".</summary>
        private static string ForgeUnsignedToken(string email)
        {
            static string B64(string json) =>
                Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
                    .TrimEnd('=').Replace('+', '-').Replace('/', '_');

            var header = B64("{\"alg\":\"none\",\"typ\":\"JWT\"}");
            var exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
            var payload = B64(
                $"{{\"aud\":\"{MicrosoftClientId}\"," +
                $"\"iss\":\"https://login.microsoftonline.com/common/v2.0\"," +
                $"\"exp\":{exp},\"email\":\"{email}\",\"oid\":\"{Guid.NewGuid()}\"}}");

            return $"{header}.{payload}.";
        }

        private Task<HttpResponseMessage> OAuthLoginAsync(string idToken, OAuthProvider provider = OAuthProvider.Microsoft) =>
            _client.PostAsJsonAsync("/api/auth/oauth-login", new OAuthLoginRequest
            {
                IdToken = idToken,
                Provider = provider
            });

        private async Task<User?> LoadUserAsync(string email)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return await db.Users.FirstOrDefaultAsync(u => u.Email == email.ToLowerInvariant());
        }

        // ================================================================
        // Signature forgery
        // ================================================================

        /// <summary>
        /// The core of it. The previous implementation called
        /// <c>JwtSecurityTokenHandler.ReadJwtToken</c>, which <em>decodes</em>
        /// and does not verify anything, then inspected aud/iss/exp from the
        /// decoded - that is, attacker-controlled - claims.
        ///
        /// A token signed with a key the attacker generated satisfied all of
        /// those checks, so anyone could sign in as any address with no
        /// credential and no interaction with Microsoft at all.
        /// </summary>
        [Fact]
        public async Task MicrosoftToken_SignedByAnAttackerKey_IsRejected()
        {
            var response = await OAuthLoginAsync(ForgeMicrosoftToken("victim@example.com"));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Null(await LoadUserAsync("victim@example.com"));
        }

        [Fact]
        public async Task MicrosoftToken_WithNoSignature_IsRejected()
        {
            var response = await OAuthLoginAsync(ForgeUnsignedToken("victim2@example.com"));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Null(await LoadUserAsync("victim2@example.com"));
        }

        /// <summary>
        /// The attack that mattered most: an account that already exists, with a
        /// password, taken over by a forged assertion naming its address.
        /// </summary>
        [Fact]
        public async Task ForgedToken_CannotTakeOverAnExistingPasswordAccount()
        {
            var email = $"owner{Guid.NewGuid()}@example.com";

            var register = await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
            {
                Email = email,
                Password = "OriginalPassword123!",
                ConfirmPassword = "OriginalPassword123!",
                FirstName = "Real",
                LastName = "Owner"
            });
            Assert.Equal(HttpStatusCode.Created, register.StatusCode);

            var attack = await OAuthLoginAsync(ForgeMicrosoftToken(email));

            Assert.Equal(HttpStatusCode.Unauthorized, attack.StatusCode);

            // And the account is untouched: not linked to any provider.
            var user = await LoadUserAsync(email);
            Assert.NotNull(user);
            Assert.Null(user!.OAuthProvider);
        }

        // ================================================================
        // Claim checks, which only matter once the signature is real
        // ================================================================

        [Fact]
        public async Task MicrosoftToken_WithNoIssuer_IsRejected()
        {
            // The old guard read `!issuer?.Contains(...) == true`, which
            // evaluates to false when the issuer is null - so a token with no
            // issuer at all sailed past it.
            var response = await OAuthLoginAsync(ForgeMicrosoftToken("noiss@example.com", issuer: null));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task MicrosoftToken_WhoseIssuerMerelyContainsTheRealOne_IsRejected()
        {
            // The old guard was a substring test, so this passed it.
            var response = await OAuthLoginAsync(ForgeMicrosoftToken(
                "sneaky@example.com",
                issuer: "https://evil.example.com/?next=https://login.microsoftonline.com/"));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task MicrosoftToken_ForADifferentAudience_IsRejected()
        {
            var response = await OAuthLoginAsync(ForgeMicrosoftToken(
                "wrongaud@example.com", audience: "99999999-8888-7777-6666-555555555555"));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task MicrosoftToken_ThatHasExpired_IsRejected()
        {
            var response = await OAuthLoginAsync(ForgeMicrosoftToken(
                "expired@example.com", expires: DateTime.UtcNow.AddHours(-1)));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        // ================================================================
        // Malformed input
        // ================================================================

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not-a-jwt")]
        [InlineData("a.b.c")]
        [InlineData("....")]
        public async Task MalformedToken_IsRejectedWithoutCrashing(string idToken)
        {
            var response = await OAuthLoginAsync(idToken);

            Assert.True(
                response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest,
                $"expected a refusal, got {(int)response.StatusCode}");
        }

        [Fact]
        public async Task GoogleToken_ThatIsForged_IsRejected()
        {
            // Google's own validator verifies signatures against Google's keys,
            // so this has always been refused. Asserted so a future refactor
            // cannot quietly replace it with a decode.
            var response = await OAuthLoginAsync(
                ForgeMicrosoftToken("googlevictim@example.com"), OAuthProvider.Google);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Null(await LoadUserAsync("googlevictim@example.com"));
        }
    }
}
