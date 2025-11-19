using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Letmework4U.Infrastructure;
using Letmework4U.Infrastructure.Entities;
using Letmework4U.Shared.Models;
using Letmework4U.Api.Services;

namespace Letmework4U.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IJwtService _jwtService;
        private readonly IPasswordHasher _passwordHasher;
        private readonly IOAuthService _oAuthService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            ApplicationDbContext context,
            IJwtService jwtService,
            IPasswordHasher passwordHasher,
            IOAuthService oAuthService,
            ILogger<AuthController> logger)
        {
            _context = context;
            _jwtService = jwtService;
            _passwordHasher = passwordHasher;
            _oAuthService = oAuthService;
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

                // Check if user already exists
                var existingUser = await _context.Users
                    .FirstOrDefaultAsync(u => u.Email.ToLower() == registerRequest.Email.ToLower());

                if (existingUser != null)
                {
                    return Conflict(new { error = "User with this email already exists" });
                }

                // Hash password
                var passwordHash = _passwordHasher.HashPassword(registerRequest.Password);

                // Create new user
                var newUser = new User
                {
                    Email = registerRequest.Email.ToLower(),
                    PasswordHash = passwordHash,
                    FirstName = registerRequest.FirstName,
                    LastName = registerRequest.LastName,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true,
                    EmailConfirmed = false
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

                // Find user by email
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Email.ToLower() == loginRequest.Email.ToLower());

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

                // Generate verification token
                var verificationToken = Guid.NewGuid().ToString("N");
                user.EmailConfirmationToken = verificationToken;
                _context.Users.Update(user);
                await _context.SaveChangesAsync();

                // TODO: Send email with verification link
                // In production, send email via Azure Communication Services
                // var verificationLink = $"https://your-domain.com/verify-email?token={verificationToken}";

                return Ok(new
                {
                    message = "Verification email sent",
                    verificationToken // Remove in production!
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send verification");
                return StatusCode(500, new { error = $"Failed to send verification: {ex.Message}" });
            }
        }

        /// <summary>
        /// Verify email with token
        /// POST /api/auth/verify-email
        /// </summary>
        [HttpPost("verify-email")]
        [AllowAnonymous]
        public async Task<IActionResult> VerifyEmail([FromBody] Dictionary<string, string> request)
        {
            try
            {
                var verificationToken = request?["verificationToken"];

                if (string.IsNullOrEmpty(verificationToken))
                {
                    return BadRequest(new { error = "Verification token is required" });
                }

                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.EmailConfirmationToken == verificationToken);

                if (user == null)
                {
                    return NotFound(new { error = "Invalid verification token" });
                }

                user.EmailConfirmed = true;
                user.EmailConfirmationToken = null;
                _context.Users.Update(user);
                await _context.SaveChangesAsync();

                return Ok(new { message = "Email verified successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Email verification failed");
                return StatusCode(500, new { error = $"Email verification failed: {ex.Message}" });
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

                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Email.ToLower() == forgotPasswordRequest.Email.ToLower());

                // Always return success to prevent email enumeration
                if (user == null)
                {
                    return Ok(new { message = "If the email exists, a password reset link has been sent" });
                }

                // Generate password reset token
                var resetToken = Guid.NewGuid().ToString("N");
                user.PasswordResetToken = resetToken;
                user.PasswordResetTokenExpiry = DateTime.UtcNow.AddHours(1); // Token valid for 1 hour
                _context.Users.Update(user);
                await _context.SaveChangesAsync();

                // TODO: Send email with reset link
                // In production, send email via Azure Communication Services
                // var resetLink = $"https://your-domain.com/reset-password?token={resetToken}";

                return Ok(new
                {
                    message = "If the email exists, a password reset link has been sent",
                    resetToken // Remove in production!
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Password reset request failed");
                return StatusCode(500, new { error = $"Password reset request failed: {ex.Message}" });
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

                var user = await _context.Users
                    .FirstOrDefaultAsync(u =>
                        u.Email.ToLower() == resetPasswordRequest.Email.ToLower() &&
                        u.PasswordResetToken == resetPasswordRequest.ResetToken);

                if (user == null ||
                    user.PasswordResetTokenExpiry == null ||
                    user.PasswordResetTokenExpiry < DateTime.UtcNow)
                {
                    return BadRequest(new { error = "Invalid or expired reset token" });
                }

                // Hash new password
                user.PasswordHash = _passwordHasher.HashPassword(resetPasswordRequest.NewPassword);
                user.PasswordResetToken = null;
                user.PasswordResetTokenExpiry = null;

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

                // Step 2: Find or create user
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Email == oauthUserInfo.Email);

                if (user == null)
                {
                    // Create new user from OAuth info
                    user = new User
                    {
                        Id = Guid.NewGuid().ToString(),
                        Email = oauthUserInfo.Email,
                        FirstName = oauthUserInfo.FirstName,
                        LastName = oauthUserInfo.LastName,
                        EmailConfirmed = oauthUserInfo.EmailVerified,
                        OAuthProvider = oauthUserInfo.Provider.ToString(),
                        OAuthProviderId = oauthUserInfo.ProviderId,
                        ProfilePictureUrl = oauthUserInfo.ProfilePictureUrl,
                        PasswordHash = null, // OAuth users don't have passwords initially
                        CreatedAt = DateTime.UtcNow,
                        IsActive = true
                    };

                    _context.Users.Add(user);
                    await _context.SaveChangesAsync();

                    _logger.LogInformation($"Created new OAuth user: {user.Email} via {oauthUserInfo.Provider}");
                }
                else
                {
                    // Update existing user with OAuth info if not already set
                    if (string.IsNullOrEmpty(user.OAuthProvider))
                    {
                        user.OAuthProvider = oauthUserInfo.Provider.ToString();
                        user.OAuthProviderId = oauthUserInfo.ProviderId;
                        user.ProfilePictureUrl = oauthUserInfo.ProfilePictureUrl;
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
