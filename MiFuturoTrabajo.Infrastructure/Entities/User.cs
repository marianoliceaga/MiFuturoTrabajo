namespace MiFuturoTrabajo.Infrastructure.Entities
{
    /// <summary>
    /// User entity for proprietary authentication system
    /// Stores authentication credentials and basic user information
    /// </summary>
    public class User
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Email address - used as username for login
        /// </summary>
        public string Email { get; set; } = string.Empty;

        /// <summary>
        /// Hashed password (using BCrypt or ASP.NET Core Identity hasher)
        /// Nullable for OAuth-only users who don't have a password
        /// </summary>
        public string? PasswordHash { get; set; }

        /// <summary>
        /// User's first name
        /// </summary>
        public string FirstName { get; set; } = string.Empty;

        /// <summary>
        /// User's last name
        /// </summary>
        public string LastName { get; set; } = string.Empty;

        /// <summary>
        /// Email verification status
        /// </summary>
        public bool EmailConfirmed { get; set; } = false;

        /// <summary>
        /// SHA-256 hash of the email verification token - never the token
        /// itself. Whoever can read this row cannot use what they find.
        /// </summary>
        public string? EmailConfirmationTokenHash { get; set; }

        /// <summary>
        /// When the verification token stops working. Previously absent, which
        /// meant verification links were valid forever.
        /// </summary>
        public DateTime? EmailConfirmationTokenExpiry { get; set; }

        /// <summary>
        /// SHA-256 hash of the password reset token - never the token itself.
        /// </summary>
        public string? PasswordResetTokenHash { get; set; }

        /// <summary>
        /// Password reset token expiration
        /// </summary>
        public DateTime? PasswordResetTokenExpiry { get; set; }

        /// <summary>
        /// Refresh token for JWT token refresh
        /// </summary>
        public string? RefreshToken { get; set; }

        /// <summary>
        /// Refresh token expiration date
        /// </summary>
        public DateTime? RefreshTokenExpiry { get; set; }

        /// <summary>
        /// Account creation date
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last login date
        /// </summary>
        public DateTime? LastLoginAt { get; set; }

        /// <summary>
        /// Account active status
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Account locked status (after failed login attempts)
        /// </summary>
        public bool IsLocked { get; set; } = false;

        /// <summary>
        /// Failed login attempt count
        /// </summary>
        public int FailedLoginAttempts { get; set; } = 0;

        /// <summary>
        /// Account lockout end time
        /// </summary>
        public DateTime? LockoutEnd { get; set; }

        /// <summary>
        /// Related Applicant ID (one-to-one relationship)
        /// </summary>
        public string? ApplicantId { get; set; }

        // No navigation to Applicant: it lives in a different container and
        // Cosmos cannot join across containers. Load it by ApplicantId.

        /// <summary>
        /// OAuth provider used for authentication (null for email/password users)
        /// Possible values: "Google", "Microsoft"
        /// </summary>
        public string? OAuthProvider { get; set; }

        /// <summary>
        /// User's unique ID from the OAuth provider
        /// </summary>
        public string? OAuthProviderId { get; set; }

        /// <summary>
        /// Profile picture URL from OAuth provider
        /// </summary>
        public string? ProfilePictureUrl { get; set; }

        /// <summary>
        /// Language the account's email is written in: "es" or "en".
        ///
        /// The browser is the source of truth for what the UI renders in - that
        /// preference lives in localStorage and never needs the server. This
        /// copy exists for the one path that has no browser to ask: the Azure
        /// Functions timer triggers compose reminder emails with no request in
        /// flight, so without a stored preference every digest would go out in
        /// the default language regardless of what the recipient reads.
        ///
        /// Nullable on purpose. Accounts created before this field existed have
        /// no value, and null is honestly "not stated" rather than a guess;
        /// EmailLanguage.Resolve turns it into the default at send time.
        /// </summary>
        public string? PreferredLanguage { get; set; }
    }
}
