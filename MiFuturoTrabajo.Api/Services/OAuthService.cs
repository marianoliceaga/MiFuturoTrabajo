using Google.Apis.Auth;
using Microsoft.Identity.Client;
using MiFuturoTrabajo.Shared.Models;

namespace MiFuturoTrabajo.Api.Services
{
    public interface IOAuthService
    {
        Task<OAuthUserInfo?> ValidateGoogleTokenAsync(string idToken);
        Task<OAuthUserInfo?> ValidateMicrosoftTokenAsync(string idToken);
        Task<OAuthUserInfo?> ValidateOAuthTokenAsync(OAuthProvider provider, string idToken);
    }

    public class OAuthService : IOAuthService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<OAuthService> _logger;

        public OAuthService(IConfiguration configuration, ILogger<OAuthService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<OAuthUserInfo?> ValidateOAuthTokenAsync(OAuthProvider provider, string idToken)
        {
            return provider switch
            {
                OAuthProvider.Google => await ValidateGoogleTokenAsync(idToken),
                OAuthProvider.Microsoft => await ValidateMicrosoftTokenAsync(idToken),
                _ => null
            };
        }

        public async Task<OAuthUserInfo?> ValidateGoogleTokenAsync(string idToken)
        {
            try
            {
                var googleClientId = _configuration["OAuth:Google:ClientId"];

                if (string.IsNullOrEmpty(googleClientId))
                {
                    _logger.LogWarning("Google OAuth ClientId not configured");
                    return null;
                }

                // Validate the token with Google
                var validationSettings = new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = new[] { googleClientId }
                };

                var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, validationSettings);

                if (payload == null)
                {
                    _logger.LogWarning("Google token validation returned null payload");
                    return null;
                }

                // Extract user information
                return new OAuthUserInfo
                {
                    Email = payload.Email,
                    FirstName = payload.GivenName ?? string.Empty,
                    LastName = payload.FamilyName ?? string.Empty,
                    ProfilePictureUrl = payload.Picture,
                    ProviderId = payload.Subject, // Google's unique user ID
                    Provider = OAuthProvider.Google,
                    EmailVerified = payload.EmailVerified
                };
            }
            catch (InvalidJwtException ex)
            {
                _logger.LogError(ex, "Invalid Google JWT token");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating Google token");
                return null;
            }
        }

        public async Task<OAuthUserInfo?> ValidateMicrosoftTokenAsync(string idToken)
        {
            try
            {
                var microsoftClientId = _configuration["OAuth:Microsoft:ClientId"];
                var microsoftTenantId = _configuration["OAuth:Microsoft:TenantId"] ?? "common";

                if (string.IsNullOrEmpty(microsoftClientId))
                {
                    _logger.LogWarning("Microsoft OAuth ClientId not configured");
                    return null;
                }

                // For token validation, we need to verify the token signature and claims
                // Microsoft tokens can be validated using the Microsoft Identity library

                // Create a confidential client application (not used for validation, but for token info)
                var authority = $"https://login.microsoftonline.com/{microsoftTenantId}";

                // Parse the JWT token manually to extract claims
                var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                var jwtToken = handler.ReadJwtToken(idToken);

                // Validate basic token structure
                if (jwtToken == null)
                {
                    _logger.LogWarning("Failed to parse Microsoft JWT token");
                    return null;
                }

                // Verify audience claim
                var audienceClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "aud")?.Value;
                if (audienceClaim != microsoftClientId)
                {
                    _logger.LogWarning($"Microsoft token audience mismatch. Expected: {microsoftClientId}, Got: {audienceClaim}");
                    return null;
                }

                // Verify issuer
                var issuerClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "iss")?.Value;
                if (!issuerClaim?.Contains("https://login.microsoftonline.com/") == true)
                {
                    _logger.LogWarning($"Microsoft token issuer invalid: {issuerClaim}");
                    return null;
                }

                // Check expiration
                var expClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "exp")?.Value;
                if (long.TryParse(expClaim, out var exp))
                {
                    var expirationTime = DateTimeOffset.FromUnixTimeSeconds(exp);
                    if (expirationTime < DateTimeOffset.UtcNow)
                    {
                        _logger.LogWarning("Microsoft token has expired");
                        return null;
                    }
                }

                // Extract user information from claims
                var email = jwtToken.Claims.FirstOrDefault(c => c.Type == "email" || c.Type == "preferred_username")?.Value;
                var firstName = jwtToken.Claims.FirstOrDefault(c => c.Type == "given_name")?.Value;
                var lastName = jwtToken.Claims.FirstOrDefault(c => c.Type == "family_name")?.Value;
                var name = jwtToken.Claims.FirstOrDefault(c => c.Type == "name")?.Value;
                var oid = jwtToken.Claims.FirstOrDefault(c => c.Type == "oid" || c.Type == "sub")?.Value;

                if (string.IsNullOrEmpty(email))
                {
                    _logger.LogWarning("Microsoft token missing email claim");
                    return null;
                }

                // If first/last name not available, try to split the full name
                if (string.IsNullOrEmpty(firstName) && !string.IsNullOrEmpty(name))
                {
                    var nameParts = name.Split(' ', 2);
                    firstName = nameParts[0];
                    lastName = nameParts.Length > 1 ? nameParts[1] : string.Empty;
                }

                return new OAuthUserInfo
                {
                    Email = email,
                    FirstName = firstName ?? string.Empty,
                    LastName = lastName ?? string.Empty,
                    ProviderId = oid ?? email, // Use OID (object identifier) or email as fallback
                    Provider = OAuthProvider.Microsoft,
                    EmailVerified = true // Microsoft verifies emails during account creation
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating Microsoft token");
                return null;
            }
        }
    }
}
