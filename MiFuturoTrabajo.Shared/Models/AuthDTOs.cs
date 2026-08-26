namespace MiFuturoTrabajo.Shared.Models
{
    /// <summary>
    /// Request DTO for user registration
    /// </summary>
    public class RegisterRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string ConfirmPassword { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Request DTO for user login
    /// </summary>
    public class LoginRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public bool RememberMe { get; set; } = false;
    }

    /// <summary>
    /// Response DTO for successful authentication
    /// </summary>
    public class AuthResponse
    {
        public string UserId { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public DateTime TokenExpiry { get; set; }
        public string? ApplicantId { get; set; }
    }

    /// <summary>
    /// Request DTO for token refresh
    /// </summary>
    public class RefreshTokenRequest
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
    }

    /// <summary>
    /// Request DTO for password change
    /// </summary>
    public class ChangePasswordRequest
    {
        public string CurrentPassword { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
        public string ConfirmNewPassword { get; set; } = string.Empty;
    }

    /// <summary>
    /// Request DTO for password reset
    /// </summary>
    public class ForgotPasswordRequest
    {
        public string Email { get; set; } = string.Empty;
    }

    /// <summary>
    /// Request DTO for confirming an email address.
    ///
    /// The email travels with the token because Users is partitioned by email
    /// in Cosmos: looking a user up by token alone is a cross-partition scan of
    /// every account in the system, while email plus token is a single-
    /// partition read. The email is not a secret and grants nothing on its own
    /// - the token is still what has to match.
    /// </summary>
    public class VerifyEmailRequest
    {
        public string Email { get; set; } = string.Empty;
        public string VerificationToken { get; set; } = string.Empty;
    }

    /// <summary>
    /// Request DTO for password reset confirmation
    /// </summary>
    public class ResetPasswordRequest
    {
        public string Email { get; set; } = string.Empty;
        public string ResetToken { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    /// <summary>
    /// Response DTO for user profile
    /// </summary>
    public class UserDto
    {
        public string Id { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public bool EmailConfirmed { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public string? ApplicantId { get; set; }
    }

    /// <summary>
    /// OAuth provider types
    /// </summary>
    public enum OAuthProvider
    {
        Google = 1,
        Microsoft = 2
    }

    /// <summary>
    /// Request DTO for OAuth login
    /// </summary>
    public class OAuthLoginRequest
    {
        public OAuthProvider Provider { get; set; }
        public string IdToken { get; set; } = string.Empty;
    }

    /// <summary>
    /// Internal DTO for OAuth user information after validation
    /// </summary>
    public class OAuthUserInfo
    {
        public string Email { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string? ProfilePictureUrl { get; set; }
        public string ProviderId { get; set; } = string.Empty; // User's ID from the OAuth provider
        public OAuthProvider Provider { get; set; }
        public bool EmailVerified { get; set; } = true; // OAuth providers verify emails
    }
}
