using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiFuturoTrabajo.Infrastructure;
using MiFuturoTrabajo.Infrastructure.Entities;
using MiFuturoTrabajo.Shared.Models;
using MiFuturoTrabajo.Api.Services;
using MiFuturoTrabajo.Infrastructure.Email;
using Microsoft.AspNetCore.RateLimiting;

namespace MiFuturoTrabajo.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [EnableRateLimiting(RateLimiting.AuthPolicy)]
    public class AuthController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IJwtService _jwtService;
        private readonly IPasswordHasher _passwordHasher;
        private readonly IOAuthService _oAuthService;
        private readonly IEmailService _emailService;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<AuthController> _logger;
        private readonly IConfiguration _configuration;

        public AuthController(
            ApplicationDbContext context,
            IJwtService jwtService,
            IPasswordHasher passwordHasher,
            IOAuthService oAuthService,
            IEmailService emailService,
            IWebHostEnvironment env,
            IConfiguration configuration,
            ILogger<AuthController> logger)
        {
            _context = context;
            _jwtService = jwtService;
            _passwordHasher = passwordHasher;
            _oAuthService = oAuthService;
            _emailService = emailService;
            _env = env;
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>
        /// Register a new user
        /// POST /api/auth/register
        /// </summary>
        [HttpPost("register")]
        [AllowAnonymous]
        public async Task<IActionResult> Register([FromBody] RegisterRequest registerRequest)
        {
            try
            {
                if (registerRequest == null)
                {
                    return BadRequest(new { error = "Invalid request body" });
                }

                // Validate input
                if (string.IsNullOrWhiteSpace(registerRequest.Email) ||
                    string.IsNullOrWhiteSpace(registerRequest.Password) ||
                    string.IsNullOrWhiteSpace(registerRequest.FirstName) ||
                    string.IsNullOrWhiteSpace(registerRequest.LastName))
                {
                    return BadRequest(new { error = "All fields are required" });
                }

                // Validate email format
                if (!IsValidEmail(registerRequest.Email))
                {
                    return BadRequest(new { error = "Invalid email format" });
                }

                // Validate password match
                if (registerRequest.Password != registerRequest.ConfirmPassword)
                {
                    return BadRequest(new { error = "Passwords do not match" });
                }

                // Validate password strength (minimum 8 characters)
                if (registerRequest.Password.Length < 8)
                {
                    return BadRequest(new { error = "Password must be at least 8 characters long" });
                }

                var normalizedEmail = NormalizeEmail(registerRequest.Email);

                // Single-partition read: Email is the partition key, and the
                // comparison is against the stored form rather than wrapping
                // the column in ToLower(), which would defeat that.
                var existingUser = await _context.Users
                    .FirstOrDefaultAsync(u => u.Email == normalizedEmail);

                if (existingUser != null)
                {
                    return Conflict(new { error = "User with this email already exists" });
                }

                // Hash password
                var passwordHash = _passwordHasher.HashPassword(registerRequest.Password);

                // Create new user
                var newUser = new User
                {
                    Email = normalizedEmail,
                    PasswordHash = passwordHash,
                    FirstName = registerRequest.FirstName,
                    LastName = registerRequest.LastName,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true,
                    EmailConfirmed = false,
                    // So the verification email that follows, and every reminder
                    // after it, is written in the language this person is
                    // actually reading the site in.
                    PreferredLanguage = RequestedLanguage()
                };

                _context.Users.Add(newUser);
                await _context.SaveChangesAsync();

                // Create corresponding Applicant record
                var applicant = new Applicant
                {
                    UserId = newUser.Id,
                    Name = registerRequest.FirstName,
                    Lastname = registerRequest.LastName,
                    ContactEmail = registerRequest.Email,
                    CreatedOnDate = DateTime.UtcNow
                };

                _context.Applicants.Add(applicant);
                await _context.SaveChangesAsync();

                // Update user with applicant ID
                newUser.ApplicantId = applicant.Id;
                _context.Users.Update(newUser);
                await _context.SaveChangesAsync();

                // Verification is offered immediately rather than waiting for
                // the user to find a "resend" button. Best-effort: a mail
                // outage must not fail an otherwise good registration.
                await IssueVerificationEmailAsync(newUser);

                // Generate tokens
                var accessToken = _jwtService.GenerateAccessToken(newUser.Id, newUser.Email);
                var refreshToken = _jwtService.GenerateRefreshToken();

                // Store refresh token
                newUser.RefreshToken = refreshToken;
                newUser.RefreshTokenExpiry = DateTime.UtcNow.AddDays(7);
                newUser.LastLoginAt = DateTime.UtcNow;
                _context.Users.Update(newUser);
                await _context.SaveChangesAsync();

                // Return auth response
                var authResponse = new AuthResponse
                {
                    UserId = newUser.Id,
                    Email = newUser.Email,
                    FirstName = newUser.FirstName,
                    LastName = newUser.LastName,
                    AccessToken = accessToken,
                    RefreshToken = refreshToken,
                    TokenExpiry = DateTime.UtcNow.AddMinutes(60),
                    ApplicantId = applicant.Id
                };

                return StatusCode(201, authResponse);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Registration failed");
                return StatusCode(500, new { error = $"Registration failed: {ex.Message}" });
            }
        }

        /// <summary>
        /// Login with email and password
        /// POST /api/auth/login
        /// </summary>
        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromBody] LoginRequest loginRequest)
        {
            try
            {
                if (loginRequest == null)
                {
                    return BadRequest(new { error = "Invalid request body" });
                }

                // Validate input
                if (string.IsNullOrWhiteSpace(loginRequest.Email) ||
                    string.IsNullOrWhiteSpace(loginRequest.Password))
                {
                    return BadRequest(new { error = "Email and password are required" });
                }

                // Find user by email. This is the hottest path in the app, so
                // it matters that it is a single-partition read.
                var loginEmail = NormalizeEmail(loginRequest.Email);

                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Email == loginEmail);

                if (user == null)
                {
                    return Unauthorized(new { error = "Invalid email or password" });
                }

                // Check if account is locked
                if (user.IsLocked && user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTime.UtcNow)
                {
                    return StatusCode(403, new
                    {
                        error = "Account is locked due to multiple failed login attempts",
                        lockoutEnd = user.LockoutEnd.Value
                    });
                }

                // Check if account is active
                if (!user.IsActive)
                {
                    return StatusCode(403, new { error = "Account is disabled" });
                }

                // Verify password
                if (!_passwordHasher.VerifyPassword(loginRequest.Password, user.PasswordHash))
                {
                    // Increment failed login attempts
                    user.FailedLoginAttempts++;

                    // Lock account after 5 failed attempts
                    if (user.FailedLoginAttempts >= 5)
                    {
                        user.IsLocked = true;
                        user.LockoutEnd = DateTime.UtcNow.AddMinutes(30);
                    }

                    _context.Users.Update(user);
                    await _context.SaveChangesAsync();

                    return Unauthorized(new { error = "Invalid email or password" });
                }

                // Reset failed login attempts on successful login
                user.FailedLoginAttempts = 0;
                user.IsLocked = false;
                user.LockoutEnd = null;
                user.LastLoginAt = DateTime.UtcNow;

                // Generate tokens
                var accessToken = _jwtService.GenerateAccessToken(user.Id, user.Email);
                var refreshToken = _jwtService.GenerateRefreshToken();

                // Store refresh token
                user.RefreshToken = refreshToken;
                user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(loginRequest.RememberMe ? 30 : 7);

                _context.Users.Update(user);
                await _context.SaveChangesAsync();

                // Return auth response
                var authResponse = new AuthResponse
                {
                    UserId = user.Id,
                    Email = user.Email,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    AccessToken = accessToken,
                    RefreshToken = refreshToken,
                    TokenExpiry = DateTime.UtcNow.AddMinutes(60),
                    ApplicantId = user.ApplicantId
                };

                return Ok(authResponse);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Login failed");
                return StatusCode(500, new { error = $"Login failed: {ex.Message}" });
            }
        }

        /// <summary>
        /// Refresh access token using refresh token
        /// POST /api/auth/refresh
        /// </summary>
        [HttpPost("refresh")]
        [AllowAnonymous]
        public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest refreshRequest)
        {
            try
            {
                if (refreshRequest == null || string.IsNullOrWhiteSpace(refreshRequest.RefreshToken))
                {
                    return BadRequest(new { error = "Refresh token is required" });
                }

                // Get user ID from the expired access token (don't validate expiry)
                var principal = _jwtService.ValidateToken(refreshRequest.AccessToken);
                var userId = principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized(new { error = "Invalid token" });
                }

                // Find user and validate refresh token
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);

                if (user == null ||
                    user.RefreshToken != refreshRequest.RefreshToken ||
                    user.RefreshTokenExpiry < DateTime.UtcNow)
                {
                    return Unauthorized(new { error = "Invalid or expired refresh token" });
                }

                // Generate new tokens
                var newAccessToken = _jwtService.GenerateAccessToken(user.Id, user.Email);
                var newRefreshToken = _jwtService.GenerateRefreshToken();

                // Update refresh token
                user.RefreshToken = newRefreshToken;
                user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(7);
                _context.Users.Update(user);
                await _context.SaveChangesAsync();

                var authResponse = new AuthResponse
                {
                    UserId = user.Id,
                    Email = user.Email,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    AccessToken = newAccessToken,
                    RefreshToken = newRefreshToken,
                    TokenExpiry = DateTime.UtcNow.AddMinutes(60),
                    ApplicantId = user.ApplicantId
                };

                return Ok(authResponse);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Token refresh failed");
                return StatusCode(500, new { error = $"Token refresh failed: {ex.Message}" });
            }
        }

        /// <summary>
        /// Get current user profile
        /// GET /api/auth/me
        /// </summary>
        [HttpGet("me")]
        [Authorize]
        public async Task<IActionResult> GetCurrentUser()
        {
            try
            {
                var userId = Helpers.AuthHelper.GetUserIdFromClaims(User);

                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized(new { error = "Invalid token" });
                }

                // Get user
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);

                if (user == null)
                {
                    return NotFound(new { error = "User not found" });
                }

                var userDto = new UserDto
                {
                    Id = user.Id,
                    Email = user.Email,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    EmailConfirmed = user.EmailConfirmed,
                    CreatedAt = user.CreatedAt,
                    LastLoginAt = user.LastLoginAt,
                    ApplicantId = user.ApplicantId
                };

                return Ok(userDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get user");
                return StatusCode(500, new { error = $"Failed to get user: {ex.Message}" });
            }
        }

        /// <summary>
        /// Send email verification
        /// POST /api/auth/send-verification
        /// </summary>
        [HttpPost("send-verification")]
        [Authorize]
        public async Task<IActionResult> SendEmailVerification()
        {
            try
            {
                var userId = Helpers.AuthHelper.GetUserIdFromClaims(User);
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized(new { error = "Invalid token" });
                }

                var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
                if (user == null)
                {
                    return NotFound(new { error = "User not found" });
                }

                if (user.EmailConfirmed)
                {
                    return BadRequest(new { error = "Email already confirmed" });
                }

                await IssueVerificationEmailAsync(user);

                // The token is not in this response, in any environment. It is
                // a credential; the only place it belongs is the recipient
                // inbox. With no mail provider configured, NoOpEmailService
                // writes the link to the server log instead.
                return Ok(new { message = "Verification email sent" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send verification");
                return StatusCode(500, new { error = "Failed to send verification" });
            }
        }

        // ------------------------------------------------------------------
        // Email-borne credentials
        // ------------------------------------------------------------------

        /// <summary>
        /// Email is the Cosmos partition key for Users, so its exact casing is
        /// part of a document address. Every read and write goes through this,
        /// because "User@example.com" and "user@example.com" would otherwise be
        /// two different partitions - that is, two different accounts for one
        /// person, with no unique index to catch it.
        /// </summary>
        /// <summary>
        /// Every user needs an applicant record - registration creates one, and
        /// an account created through OAuth was silently getting none, so the
        /// first page load after a social sign-in had nothing to read.
        /// </summary>
        private async Task CreateApplicantForAsync(User user)
        {
            var applicant = new Applicant
            {
                UserId = user.Id,
                Name = user.FirstName,
                Lastname = user.LastName,
                ContactEmail = user.Email,
                CreatedOnDate = DateTime.UtcNow
            };

            _context.Applicants.Add(applicant);
            await _context.SaveChangesAsync();

            user.ApplicantId = applicant.Id;
            _context.Users.Update(user);
            await _context.SaveChangesAsync();
        }

        private static string NormalizeEmail(string? email) =>
            (email ?? string.Empty).Trim().ToLowerInvariant();

        private const int PasswordResetTokenLifetimeHours = 1;
        private const int VerificationTokenLifetimeHours = 24;

        private const string NeutralForgotPasswordMessage =
            "If the email exists, a password reset link has been sent";

        /// <summary>
        /// Mints a verification token, stores only its hash, and mails the
        /// link. Never throws: callers use it as a side effect of registering
        /// or of an explicit resend, and neither should fail because a mail
        /// provider is down.
        /// </summary>
        private async Task IssueVerificationEmailAsync(User user)
        {
            var verificationToken = SecurityTokens.Create();

            user.EmailConfirmationTokenHash = SecurityTokens.Hash(verificationToken);
            user.EmailConfirmationTokenExpiry = DateTime.UtcNow.AddHours(VerificationTokenLifetimeHours);

            _context.Users.Update(user);
            await _context.SaveChangesAsync();

            var link = BuildClientLink("verify-email", verificationToken, user.Email);

            await TrySendAsync(
                user.Email,
                EmailTemplates.Verification(LanguageFor(user), user.FirstName, link, VerificationTokenLifetimeHours),
                "email verification");
        }

        /// <summary>
        /// Stores the caller's language preference on their own account.
        /// PUT /api/auth/preferences
        ///
        /// The browser keeps its own copy in localStorage and does not need
        /// this to render; the server copy exists so the reminder timer
        /// triggers, which run with no request in flight, know which language
        /// to write in.
        /// </summary>
        [HttpPut("preferences")]
        [Authorize]
        public async Task<IActionResult> UpdatePreferences([FromBody] UpdatePreferencesRequest request)
        {
            try
            {
                var userId = Helpers.AuthHelper.GetUserIdFromClaims(User);

                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized(new { error = "Invalid token" });
                }

                // Rejected rather than coerced: this value selects an email
                // template, and silently storing something unrecognised would
                // turn a client bug into a wrong-language email months later.
                if (request?.PreferredLanguage is not (EmailLanguage.Spanish or EmailLanguage.English))
                {
                    return BadRequest(new { error = "PreferredLanguage must be \"es\" or \"en\"" });
                }

                var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);

                if (user == null)
                {
                    return NotFound(new { error = "User not found" });
                }

                user.PreferredLanguage = request.PreferredLanguage;
                _context.Users.Update(user);
                await _context.SaveChangesAsync();

                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update preferences");
                return StatusCode(500, new { error = "Failed to update preferences" });
            }
        }

        /// <summary>
        /// The language to write this account's mail in: what the account has
        /// stored, or - for an account that predates the setting - whatever the
        /// browser making this request asked for.
        /// </summary>
        private string LanguageFor(User user) =>
            string.IsNullOrWhiteSpace(user.PreferredLanguage)
                ? RequestedLanguage()
                : EmailLanguage.Resolve(user.PreferredLanguage);

        /// <summary>
        /// The language the caller's browser asked for, via Accept-Language.
        /// The Blazor client sets it explicitly from the stored preference, so
        /// this is the choice the person made rather than a browser default.
        ///
        /// Null-safe on the request: this runs from paths that may have no
        /// HttpContext at all, and "no header" is a perfectly good answer that
        /// resolves to the default. Reading Request directly threw instead.
        /// </summary>
        private string RequestedLanguage() =>
            EmailLanguage.Resolve(HttpContext?.Request?.Headers.AcceptLanguage.ToString());

        /// <summary>
        /// Builds a link into the Blazor client. The email rides along with the
        /// token so redemption is a single-partition read in Cosmos rather than
        /// a scan across every account.
        /// </summary>
        private string BuildClientLink(string page, string token, string email)
        {
            var baseUrl = (_configuration["BlazorClient:Url"] ?? "https://localhost:5011").TrimEnd('/');

            return $"{baseUrl}/{page}?token={Uri.EscapeDataString(token)}&email={Uri.EscapeDataString(email)}";
        }

        /// <summary>
        /// Sends and swallows. A failure here is logged and nothing more: the
        /// caller has already committed its state change, and surfacing a mail
        /// error would leak whether an account exists and would fail requests
        /// that actually succeeded.
        /// </summary>
        private async Task TrySendAsync(string toEmail, EmailMessageContent content, string what)
        {
            try
            {
                await _emailService.SendAsync(toEmail, content);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not send {What} email to {Email}", what, toEmail);
            }
        }

        /// <summary>
        /// Verify email with token
        /// POST /api/auth/verify-email
        /// </summary>
        [HttpPost("verify-email")]
        [AllowAnonymous]
        public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequest request)
        {
            try
            {
                if (request == null ||
                    string.IsNullOrWhiteSpace(request.Email) ||
                    string.IsNullOrWhiteSpace(request.VerificationToken))
                {
                    return BadRequest(new { error = "Email and verification token are required" });
                }

                var email = NormalizeEmail(request.Email);

                // Email is the partition key, so this is a single-partition
                // read rather than a scan of every account in the system.
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);

                // One message for every failure: a distinct "no such account"
                // would turn this endpoint into an email-enumeration oracle.
                if (user == null ||
                    !SecurityTokens.Matches(request.VerificationToken, user.EmailConfirmationTokenHash) ||
                    user.EmailConfirmationTokenExpiry == null ||
                    user.EmailConfirmationTokenExpiry < DateTime.UtcNow)
                {
                    return BadRequest(new { error = "Invalid or expired verification link" });
                }

                user.EmailConfirmed = true;

                // Single use: the token is spent whether or not it is replayed.
                user.EmailConfirmationTokenHash = null;
                user.EmailConfirmationTokenExpiry = null;

                _context.Users.Update(user);
                await _context.SaveChangesAsync();

                return Ok(new { message = "Email verified successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Email verification failed");
                return StatusCode(500, new { error = "Email verification failed" });
            }
        }

        /// <summary>
        /// Request password reset
        /// POST /api/auth/forgot-password
        /// </summary>
        [HttpPost("forgot-password")]
        [AllowAnonymous]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest forgotPasswordRequest)
        {
            try
            {
                if (forgotPasswordRequest == null || string.IsNullOrWhiteSpace(forgotPasswordRequest.Email))
                {
                    return BadRequest(new { error = "Email is required" });
                }

                var forgotEmail = NormalizeEmail(forgotPasswordRequest.Email);

                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Email == forgotEmail);

                // Always return success to prevent email enumeration
                if (user == null)
                {
                    return Ok(new { message = NeutralForgotPasswordMessage });
                }

                var resetToken = SecurityTokens.Create();

                user.PasswordResetTokenHash = SecurityTokens.Hash(resetToken);
                user.PasswordResetTokenExpiry = DateTime.UtcNow.AddHours(PasswordResetTokenLifetimeHours);
                _context.Users.Update(user);
                await _context.SaveChangesAsync();

                var resetLink = BuildClientLink("reset-password", resetToken, user.Email);

                // A mail outage must not change what this endpoint says or how
                // long it takes to say it, or the neutral message above becomes
                // a way to tell real accounts from imaginary ones.
                await TrySendAsync(
                    user.Email,
                    EmailTemplates.PasswordReset(LanguageFor(user), user.FirstName, resetLink, PasswordResetTokenLifetimeHours),
                    "password reset");

                return Ok(new { message = NeutralForgotPasswordMessage });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Password reset request failed");
                return StatusCode(500, new { error = "Password reset request failed" });
            }
        }

        /// <summary>
        /// Reset password with token
        /// POST /api/auth/reset-password
        /// </summary>
        [HttpPost("reset-password")]
        [AllowAnonymous]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest resetPasswordRequest)
        {
            try
            {
                if (resetPasswordRequest == null ||
                    string.IsNullOrWhiteSpace(resetPasswordRequest.Email) ||
                    string.IsNullOrWhiteSpace(resetPasswordRequest.ResetToken) ||
                    string.IsNullOrWhiteSpace(resetPasswordRequest.NewPassword))
                {
                    return BadRequest(new { error = "All fields are required" });
                }

                if (resetPasswordRequest.NewPassword != resetPasswordRequest.ConfirmPassword)
                {
                    return BadRequest(new { error = "Passwords do not match" });
                }

                if (resetPasswordRequest.NewPassword.Length < 8)
                {
                    return BadRequest(new { error = "Password must be at least 8 characters long" });
                }

                var email = NormalizeEmail(resetPasswordRequest.Email);

                // Looked up by email alone - the partition key - then the token
                // is compared against its stored hash in constant time. Putting
                // the token in the query would have meant a cross-partition
                // scan, and could not work at all once tokens are hashed.
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);

                if (user == null ||
                    !SecurityTokens.Matches(resetPasswordRequest.ResetToken, user.PasswordResetTokenHash) ||
                    user.PasswordResetTokenExpiry == null ||
                    user.PasswordResetTokenExpiry < DateTime.UtcNow)
                {
                    return BadRequest(new { error = "Invalid or expired reset token" });
                }

                // Hash new password
                user.PasswordHash = _passwordHasher.HashPassword(resetPasswordRequest.NewPassword);
                user.PasswordResetTokenHash = null;
                user.PasswordResetTokenExpiry = null;

                // A completed reset proves control of the mailbox, so it also
                // settles verification. Without this, someone who registered
                // and never clicked the first link stays unverified forever
                // despite having demonstrated exactly what verification asks.
                user.EmailConfirmed = true;

                // Any reset link still outstanding is now void.
                user.EmailConfirmationTokenHash = null;
                user.EmailConfirmationTokenExpiry = null;

                // Existing sessions do not survive a password reset - that is
                // the point of resetting it.
                user.RefreshToken = null;
                user.RefreshTokenExpiry = null;

                // Reset failed login attempts
                user.FailedLoginAttempts = 0;
                user.IsLocked = false;
                user.LockoutEnd = null;

                _context.Users.Update(user);
                await _context.SaveChangesAsync();

                return Ok(new { message = "Password reset successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Password reset failed");
                return StatusCode(500, new { error = $"Password reset failed: {ex.Message}" });
            }
        }

        /// <summary>
        /// Change password (authenticated user)
        /// POST /api/auth/change-password
        /// </summary>
        [HttpPost("change-password")]
        [Authorize]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest changePasswordRequest)
        {
            try
            {
                var userId = Helpers.AuthHelper.GetUserIdFromClaims(User);
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized(new { error = "Invalid token" });
                }

                if (changePasswordRequest == null ||
                    string.IsNullOrWhiteSpace(changePasswordRequest.CurrentPassword) ||
                    string.IsNullOrWhiteSpace(changePasswordRequest.NewPassword))
                {
                    return BadRequest(new { error = "All fields are required" });
                }

                if (changePasswordRequest.NewPassword != changePasswordRequest.ConfirmNewPassword)
                {
                    return BadRequest(new { error = "New passwords do not match" });
                }

                if (changePasswordRequest.NewPassword.Length < 8)
                {
                    return BadRequest(new { error = "Password must be at least 8 characters long" });
                }

                var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
                if (user == null)
                {
                    return NotFound(new { error = "User not found" });
                }

                // Verify current password
                if (!_passwordHasher.VerifyPassword(changePasswordRequest.CurrentPassword, user.PasswordHash))
                {
                    return BadRequest(new { error = "Current password is incorrect" });
                }

                // Hash and update new password
                user.PasswordHash = _passwordHasher.HashPassword(changePasswordRequest.NewPassword);
                _context.Users.Update(user);
                await _context.SaveChangesAsync();

                return Ok(new { message = "Password changed successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Password change failed");
                return StatusCode(500, new { error = $"Password change failed: {ex.Message}" });
            }
        }

        /// <summary>
        /// Logout - Revoke refresh token
        /// POST /api/auth/logout
        /// </summary>
        [HttpPost("logout")]
        [Authorize]
        public async Task<IActionResult> Logout()
        {
            try
            {
                var userId = Helpers.AuthHelper.GetUserIdFromClaims(User);
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized(new { error = "Invalid token" });
                }

                var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
                if (user == null)
                {
                    return NotFound(new { error = "User not found" });
                }

                // Revoke refresh token
                user.RefreshToken = null;
                user.RefreshTokenExpiry = null;

                _context.Users.Update(user);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"User {user.Email} logged out successfully");

                return Ok(new { message = "Logged out successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Logout failed");
                return StatusCode(500, new { error = $"Logout failed: {ex.Message}" });
            }
        }

        /// <summary>
        /// OAuth login (Google/Microsoft)
        /// POST /api/auth/oauth-login
        /// </summary>
        [HttpPost("oauth-login")]
        [AllowAnonymous]
        public async Task<IActionResult> OAuthLogin([FromBody] OAuthLoginRequest request)
        {
            try
            {
                if (request == null || string.IsNullOrWhiteSpace(request.IdToken))
                {
                    return BadRequest(new { error = "Invalid OAuth token" });
                }

                // Step 1: Validate OAuth token with the provider
                var oauthUserInfo = await _oAuthService.ValidateOAuthTokenAsync(request.Provider, request.IdToken);

                if (oauthUserInfo == null)
                {
                    return Unauthorized(new { error = "Invalid OAuth token or provider" });
                }

                // Normalized for the same reason as every other email in this
                // controller: it is the partition key, so a provider returning
                // mixed case would create a second account for the same person.
                var oauthEmail = NormalizeEmail(oauthUserInfo.Email);

                // Step 2: Find or create user
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Email == oauthEmail);

                var provider = oauthUserInfo.Provider.ToString();

                if (user == null)
                {
                    // Create new user from OAuth info
                    user = new User
                    {
                        Id = Guid.NewGuid().ToString(),
                        Email = oauthEmail,
                        FirstName = oauthUserInfo.FirstName,
                        LastName = oauthUserInfo.LastName,
                        EmailConfirmed = oauthUserInfo.EmailVerified,
                        OAuthProvider = provider,
                        OAuthProviderId = oauthUserInfo.ProviderId,
                        ProfilePictureUrl = oauthUserInfo.ProfilePictureUrl,
                        PasswordHash = null, // OAuth users don't have passwords initially
                        CreatedAt = DateTime.UtcNow,
                        IsActive = true,
                        PreferredLanguage = RequestedLanguage()
                    };

                    _context.Users.Add(user);
                    await _context.SaveChangesAsync();

                    await CreateApplicantForAsync(user);

                    _logger.LogInformation("Created new OAuth user {Email} via {Provider}", user.Email, provider);
                }
                else
                {
                    var alreadyLinked =
                        string.Equals(user.OAuthProvider, provider, StringComparison.OrdinalIgnoreCase) &&
                        user.OAuthProviderId == oauthUserInfo.ProviderId;

                    if (!alreadyLinked)
                    {
                        // Linking a provider identity onto an account that
                        // already exists hands over that account. The only thing
                        // connecting them is the email address, so the provider
                        // has to have actually verified it.
                        //
                        // Entra does not promise that for multi-tenant apps:
                        // anyone can create a tenant and set a user's mail
                        // attribute to someone else's address. Refusing here is
                        // what stops that from being an account takeover.
                        if (!oauthUserInfo.EmailVerified)
                        {
                            _logger.LogWarning(
                                "Refused to link {Provider} identity to existing account {Email}: " +
                                "the provider did not verify the address",
                                provider, user.Email);

                            return Unauthorized(new
                            {
                                error = "This email already has an account, and your provider has not " +
                                        "verified that the address belongs to you. Sign in with your " +
                                        "password instead."
                            });
                        }

                        if (!string.IsNullOrEmpty(user.OAuthProvider))
                        {
                            _logger.LogInformation(
                                "Account {Email} moving from {Old} to {New}", user.Email, user.OAuthProvider, provider);
                        }

                        user.OAuthProvider = provider;
                        user.OAuthProviderId = oauthUserInfo.ProviderId;

                        if (!string.IsNullOrEmpty(oauthUserInfo.ProfilePictureUrl))
                        {
                            user.ProfilePictureUrl = oauthUserInfo.ProfilePictureUrl;
                        }

                        // Signing in through a provider that verified the address
                        // proves the same thing the verification email proves.
                        user.EmailConfirmed = true;

                        await TrySendAsync(
                            user.Email,
                            EmailTemplates.ProviderLinked(LanguageFor(user), user.FirstName, provider),
                            "provider linked");
                    }

                    // Check if account is locked
                    if (user.IsLocked && user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTime.UtcNow)
                    {
                        return Unauthorized(new { error = "Account is locked. Please try again later." });
                    }

                    // Check if account is active
                    if (!user.IsActive)
                    {
                        return Unauthorized(new { error = "Account is deactivated" });
                    }

                    // Update last login
                    user.LastLoginAt = DateTime.UtcNow;
                    user.FailedLoginAttempts = 0;
                    user.IsLocked = false;
                    user.LockoutEnd = null;

                    _context.Users.Update(user);
                    await _context.SaveChangesAsync();

                    _logger.LogInformation($"OAuth login successful for: {user.Email} via {oauthUserInfo.Provider}");
                }

                // Step 3: Generate internal JWT tokens
                var accessToken = _jwtService.GenerateAccessToken(user.Id, user.Email);
                var refreshToken = _jwtService.GenerateRefreshToken();

                // Store refresh token
                user.RefreshToken = refreshToken;
                user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(30); // OAuth logins get 30 days

                _context.Users.Update(user);
                await _context.SaveChangesAsync();

                // Step 4: Return standard AuthResponse with internal JWT
                var authResponse = new AuthResponse
                {
                    UserId = user.Id,
                    Email = user.Email,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    AccessToken = accessToken,
                    RefreshToken = refreshToken,
                    TokenExpiry = DateTime.UtcNow.AddMinutes(60),
                    ApplicantId = user.ApplicantId
                };

                return Ok(authResponse);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"OAuth login failed for provider: {request?.Provider}");
                return StatusCode(500, new { error = $"OAuth login failed: {ex.Message}" });
            }
        }

        private static bool IsValidEmail(string email)
        {
            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }
    }
}
