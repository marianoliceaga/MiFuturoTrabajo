using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Google.Apis.Auth;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using MiFuturoTrabajo.Shared.Models;

namespace MiFuturoTrabajo.Api.Services
{
    public interface IOAuthService
    {
        Task<OAuthUserInfo?> ValidateGoogleTokenAsync(string idToken);
        Task<OAuthUserInfo?> ValidateMicrosoftTokenAsync(string idToken);
        Task<OAuthUserInfo?> ValidateOAuthTokenAsync(OAuthProvider provider, string idToken);
    }

    /// <summary>
    /// Turns a provider's ID token into a user identity, or into nothing.
    ///
    /// This is the whole security boundary of social sign-in. The endpoint that
    /// calls it is <c>[AllowAnonymous]</c> and issues a session for whatever
    /// address the token names, so everything except the provider's signature
    /// is attacker-controlled. If that signature is not checked against the
    /// provider's published keys, the token is just a wish.
    ///
    /// The previous Microsoft implementation called
    /// <c>JwtSecurityTokenHandler.ReadJwtToken</c> - which decodes and verifies
    /// nothing - and then inspected <c>aud</c>, <c>iss</c> and <c>exp</c> from
    /// the decoded claims. All three were forgeable, so anyone could sign in as
    /// any address without a password, without mailbox access, and without ever
    /// contacting Microsoft.
    /// </summary>
    public class OAuthService : IOAuthService
    {
        private readonly IConfiguration _configuration;
        private readonly IConfigurationManager<OpenIdConnectConfiguration> _microsoftMetadata;
        private readonly ILogger<OAuthService> _logger;

        public OAuthService(
            IConfiguration configuration,
            IConfigurationManager<OpenIdConnectConfiguration> microsoftMetadata,
            ILogger<OAuthService> logger)
        {
            _configuration = configuration;
            _microsoftMetadata = microsoftMetadata;
            _logger = logger;
        }

        public async Task<OAuthUserInfo?> ValidateOAuthTokenAsync(OAuthProvider provider, string idToken)
        {
            if (string.IsNullOrWhiteSpace(idToken))
            {
                return null;
            }

            return provider switch
            {
                OAuthProvider.Google => await ValidateGoogleTokenAsync(idToken),
                OAuthProvider.Microsoft => await ValidateMicrosoftTokenAsync(idToken),
                _ => null
            };
        }

        // ------------------------------------------------------------------
        // Google
        // ------------------------------------------------------------------

        public async Task<OAuthUserInfo?> ValidateGoogleTokenAsync(string idToken)
        {
            var googleClientId = _configuration["OAuth:Google:ClientId"];

            if (string.IsNullOrEmpty(googleClientId))
            {
                _logger.LogWarning("Google OAuth ClientId not configured; refusing Google sign-in");
                return null;
            }

            try
            {
                // Google's library fetches Google's keys and verifies the
                // signature, the audience, the issuer and the lifetime. This
                // path was always sound; it is asserted in tests so a future
                // refactor cannot quietly turn it into a decode.
                var payload = await GoogleJsonWebSignature.ValidateAsync(
                    idToken,
                    new GoogleJsonWebSignature.ValidationSettings
                    {
                        Audience = new[] { googleClientId }
                    });

                if (payload == null || string.IsNullOrWhiteSpace(payload.Email))
                {
                    _logger.LogWarning("Google token carried no email");
                    return null;
                }

                return new OAuthUserInfo
                {
                    Email = payload.Email,
                    FirstName = payload.GivenName ?? string.Empty,
                    LastName = payload.FamilyName ?? string.Empty,
                    ProfilePictureUrl = payload.Picture,
                    ProviderId = payload.Subject,
                    Provider = OAuthProvider.Google,

                    // Reported, not assumed. The caller decides what an
                    // unverified address is allowed to do.
                    EmailVerified = payload.EmailVerified
                };
            }
            catch (InvalidJwtException ex)
            {
                _logger.LogWarning(ex, "Google token failed validation");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating Google token");
                return null;
            }
        }

        // ------------------------------------------------------------------
        // Microsoft
        // ------------------------------------------------------------------

        public async Task<OAuthUserInfo?> ValidateMicrosoftTokenAsync(string idToken)
        {
            var clientId = _configuration["OAuth:Microsoft:ClientId"];
            var tenantId = _configuration["OAuth:Microsoft:TenantId"] ?? "common";

            if (string.IsNullOrEmpty(clientId))
            {
                _logger.LogWarning("Microsoft OAuth ClientId not configured; refusing Microsoft sign-in");
                return null;
            }

            OpenIdConnectConfiguration metadata;

            try
            {
                // Signing keys come from Entra's published JWKS, refreshed on
                // its own schedule by the ConfigurationManager. If they cannot
                // be fetched we refuse: failing closed is the only safe
                // direction for an authentication check.
                metadata = await _microsoftMetadata.GetConfigurationAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not fetch Microsoft OpenID metadata; refusing the sign-in");
                return null;
            }

            var parameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = metadata.SigningKeys,

                ValidateAudience = true,
                ValidAudience = clientId,

                ValidateIssuer = true,
                IssuerValidator = (issuer, _, _) => ValidateMicrosoftIssuer(issuer, tenantId),

                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(2),

                RequireSignedTokens = true,
                RequireExpirationTime = true
            };

            ClaimsPrincipal principal;

            try
            {
                principal = new JwtSecurityTokenHandler().ValidateToken(idToken, parameters, out _);
            }
            catch (SecurityTokenException ex)
            {
                _logger.LogWarning(ex, "Microsoft token failed validation");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Microsoft token could not be read");
                return null;
            }

            var email = principal.FindFirst("email")?.Value
                        ?? principal.FindFirst("preferred_username")?.Value;

            if (string.IsNullOrWhiteSpace(email))
            {
                _logger.LogWarning("Microsoft token carried no email claim");
                return null;
            }

            var firstName = principal.FindFirst("given_name")?.Value;
            var lastName = principal.FindFirst("family_name")?.Value;
            var fullName = principal.FindFirst("name")?.Value;

            if (string.IsNullOrEmpty(firstName) && !string.IsNullOrEmpty(fullName))
            {
                var parts = fullName.Split(' ', 2);
                firstName = parts[0];
                lastName = parts.Length > 1 ? parts[1] : string.Empty;
            }

            var subject = principal.FindFirst("oid")?.Value
                          ?? principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

            if (string.IsNullOrWhiteSpace(subject))
            {
                // Without a stable provider-side identifier there is nothing to
                // key a linked account on but the email, which is exactly what
                // must not be trusted on its own.
                _logger.LogWarning("Microsoft token carried no oid or sub claim");
                return null;
            }

            return new OAuthUserInfo
            {
                Email = email,
                FirstName = firstName ?? string.Empty,
                LastName = lastName ?? string.Empty,
                ProviderId = subject,
                Provider = OAuthProvider.Microsoft,
                EmailVerified = IsMicrosoftEmailVerified(principal, tenantId)
            };
        }

        /// <summary>
        /// Entra does not promise that the <c>email</c> claim is verified. In a
        /// multi-tenant app anyone can stand up a tenant, set a user's mail
        /// attribute to someone else's address, and sign in - the "nOAuth"
        /// pattern. The signal that it <em>is</em> verified is
        /// <c>xms_edov</c> (email domain owner verified), which a tenant must
        /// opt into emitting.
        ///
        /// So: trust the claim only when the app is pinned to a single tenant,
        /// or when the token says so explicitly. Otherwise the address is
        /// treated as unverified and the caller refuses to link it onto an
        /// account that already exists.
        /// </summary>
        private static bool IsMicrosoftEmailVerified(ClaimsPrincipal principal, string tenantId)
        {
            var edov = principal.FindFirst("xms_edov")?.Value;

            if (bool.TryParse(edov, out var verified)) return verified;
            if (edov == "1") return true;

            return !IsMultiTenant(tenantId);
        }

        private static bool IsMultiTenant(string tenantId) =>
            tenantId.Equals("common", StringComparison.OrdinalIgnoreCase) ||
            tenantId.Equals("organizations", StringComparison.OrdinalIgnoreCase) ||
            tenantId.Equals("consumers", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// A real issuer check, replacing a substring test that
        /// <c>https://evil.example.com/?next=https://login.microsoftonline.com/</c>
        /// satisfied - and that a token with no issuer at all slipped past,
        /// because <c>!issuer?.Contains(x) == true</c> evaluates to false when
        /// issuer is null.
        /// </summary>
        internal static string ValidateMicrosoftIssuer(string? issuer, string tenantId)
        {
            if (string.IsNullOrWhiteSpace(issuer) ||
                !Uri.TryCreate(issuer, UriKind.Absolute, out var uri))
            {
                throw new SecurityTokenInvalidIssuerException($"Issuer is missing or not a URI: '{issuer}'.");
            }

            var hostIsEntra =
                uri.Scheme == Uri.UriSchemeHttps &&
                (uri.Host.Equals("login.microsoftonline.com", StringComparison.OrdinalIgnoreCase) ||
                 uri.Host.Equals("sts.windows.net", StringComparison.OrdinalIgnoreCase));

            if (!hostIsEntra)
            {
                throw new SecurityTokenInvalidIssuerException($"Issuer host is not Microsoft: '{issuer}'.");
            }

            var segments = uri.AbsolutePath.Trim('/').Split('/');

            if (segments.Length == 0 || !Guid.TryParse(segments[0], out _))
            {
                throw new SecurityTokenInvalidIssuerException(
                    $"Issuer path does not start with a tenant id: '{issuer}'.");
            }

            if (!IsMultiTenant(tenantId) && !segments[0].Equals(tenantId, StringComparison.OrdinalIgnoreCase))
            {
                throw new SecurityTokenInvalidIssuerException(
                    $"Token came from tenant '{segments[0]}' but this app accepts only '{tenantId}'.");
            }

            return issuer;
        }
    }
}
