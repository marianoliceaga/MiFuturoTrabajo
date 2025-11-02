using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using System.Net;
using Letmework4U.Infrastructure;
using Letmework4U.Infrastructure.Entities;
using Letmework4U.Shared.Models;
using Letmework4U.Functions.Services;
using System.Text.Json;

namespace Letmework4U.Functions.Functions
{
    public class AuthFunctions
    {
        private readonly ApplicationDbContext _context;
        private readonly IJwtService _jwtService;
        private readonly IPasswordHasher _passwordHasher;

        public AuthFunctions(
            ApplicationDbContext context,
            IJwtService jwtService,
            IPasswordHasher passwordHasher)
        {
            _context = context;
            _jwtService = jwtService;
            _passwordHasher = passwordHasher;
        }

        /// <summary>
        /// Register a new user
        /// POST /api/auth/register
        /// </summary>
        [Function("Register")]
        public async Task<HttpResponseData> Register(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/register")] HttpRequestData req)
        {
            try
            {
                // Parse request body
                var registerRequest = await JsonSerializer.DeserializeAsync<RegisterRequest>(req.Body);

                if (registerRequest == null)
                {
                    var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteAsJsonAsync(new { error = "Invalid request body" });
                    return badRequestResponse;
                }

                // Validate input
                if (string.IsNullOrWhiteSpace(registerRequest.Email) ||
                    string.IsNullOrWhiteSpace(registerRequest.Password) ||
                    string.IsNullOrWhiteSpace(registerRequest.FirstName) ||
                    string.IsNullOrWhiteSpace(registerRequest.LastName))
                {
                    var validationResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await validationResponse.WriteAsJsonAsync(new { error = "All fields are required" });
                    return validationResponse;
                }

                // Validate email format
                if (!IsValidEmail(registerRequest.Email))
                {
                    var emailResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await emailResponse.WriteAsJsonAsync(new { error = "Invalid email format" });
                    return emailResponse;
                }

                // Validate password match
                if (registerRequest.Password != registerRequest.ConfirmPassword)
                {
                    var passwordMatchResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await passwordMatchResponse.WriteAsJsonAsync(new { error = "Passwords do not match" });
                    return passwordMatchResponse;
                }

                // Validate password strength (minimum 8 characters)
                if (registerRequest.Password.Length < 8)
                {
                    var passwordStrengthResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await passwordStrengthResponse.WriteAsJsonAsync(new { error = "Password must be at least 8 characters long" });
                    return passwordStrengthResponse;
                }

                // Check if user already exists
                var existingUser = await _context.Users
                    .FirstOrDefaultAsync(u => u.Email.ToLower() == registerRequest.Email.ToLower());

                if (existingUser != null)
                {
                    var existsResponse = req.CreateResponse(HttpStatusCode.Conflict);
                    await existsResponse.WriteAsJsonAsync(new { error = "User with this email already exists" });
                    return existsResponse;
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
                    EmailConfirmed = false // In a real app, send confirmation email
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

                var response = req.CreateResponse(HttpStatusCode.Created);
                await response.WriteAsJsonAsync(authResponse);
                return response;
            }
            catch (Exception ex)
            {
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new { error = $"Registration failed: {ex.Message}" });
                return errorResponse;
            }
        }

        /// <summary>
        /// Login with email and password
        /// POST /api/auth/login
        /// </summary>
        [Function("Login")]
        public async Task<HttpResponseData> Login(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/login")] HttpRequestData req)
        {
            try
            {
                // Parse request body
                var loginRequest = await JsonSerializer.DeserializeAsync<LoginRequest>(req.Body);

                if (loginRequest == null)
                {
                    var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteAsJsonAsync(new { error = "Invalid request body" });
                    return badRequestResponse;
                }

                // Validate input
                if (string.IsNullOrWhiteSpace(loginRequest.Email) ||
                    string.IsNullOrWhiteSpace(loginRequest.Password))
                {
                    var validationResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await validationResponse.WriteAsJsonAsync(new { error = "Email and password are required" });
                    return validationResponse;
                }

                // Find user by email
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Email.ToLower() == loginRequest.Email.ToLower());

                if (user == null)
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                    await notFoundResponse.WriteAsJsonAsync(new { error = "Invalid email or password" });
                    return notFoundResponse;
                }

                // Check if account is locked
                if (user.IsLocked && user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTime.UtcNow)
                {
                    var lockedResponse = req.CreateResponse(HttpStatusCode.Forbidden);
                    await lockedResponse.WriteAsJsonAsync(new
                    {
                        error = "Account is locked due to multiple failed login attempts",
                        lockoutEnd = user.LockoutEnd.Value
                    });
                    return lockedResponse;
                }

                // Check if account is active
                if (!user.IsActive)
                {
                    var inactiveResponse = req.CreateResponse(HttpStatusCode.Forbidden);
                    await inactiveResponse.WriteAsJsonAsync(new { error = "Account is disabled" });
                    return inactiveResponse;
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

                    var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                    await unauthorizedResponse.WriteAsJsonAsync(new { error = "Invalid email or password" });
                    return unauthorizedResponse;
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

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(authResponse);
                return response;
            }
            catch (Exception ex)
            {
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new { error = $"Login failed: {ex.Message}" });
                return errorResponse;
            }
        }

        /// <summary>
        /// Refresh access token using refresh token
        /// POST /api/auth/refresh
        /// </summary>
        [Function("RefreshToken")]
        public async Task<HttpResponseData> RefreshToken(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/refresh")] HttpRequestData req)
        {
            try
            {
                var refreshRequest = await JsonSerializer.DeserializeAsync<RefreshTokenRequest>(req.Body);

                if (refreshRequest == null || string.IsNullOrWhiteSpace(refreshRequest.RefreshToken))
                {
                    var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteAsJsonAsync(new { error = "Refresh token is required" });
                    return badRequestResponse;
                }

                // Get user ID from the expired access token (don't validate expiry)
                var principal = _jwtService.ValidateToken(refreshRequest.AccessToken);
                var userId = principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

                if (string.IsNullOrEmpty(userId))
                {
                    var invalidResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                    await invalidResponse.WriteAsJsonAsync(new { error = "Invalid token" });
                    return invalidResponse;
                }

                // Find user and validate refresh token
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);

                if (user == null ||
                    user.RefreshToken != refreshRequest.RefreshToken ||
                    user.RefreshTokenExpiry < DateTime.UtcNow)
                {
                    var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                    await unauthorizedResponse.WriteAsJsonAsync(new { error = "Invalid or expired refresh token" });
                    return unauthorizedResponse;
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

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(authResponse);
                return response;
            }
            catch (Exception ex)
            {
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new { error = $"Token refresh failed: {ex.Message}" });
                return errorResponse;
            }
        }

        /// <summary>
        /// Get current user profile
        /// GET /api/auth/me
        /// </summary>
        [Function("GetCurrentUser")]
        public async Task<HttpResponseData> GetCurrentUser(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "auth/me")] HttpRequestData req)
        {
            try
            {
                // Get token from Authorization header
                var token = req.Headers.GetValues("Authorization").FirstOrDefault()?.Replace("Bearer ", "");

                if (string.IsNullOrEmpty(token))
                {
                    var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                    await unauthorizedResponse.WriteAsJsonAsync(new { error = "No token provided" });
                    return unauthorizedResponse;
                }

                // Validate token and get user ID
                var userId = _jwtService.GetUserIdFromToken(token);

                if (string.IsNullOrEmpty(userId))
                {
                    var invalidResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                    await invalidResponse.WriteAsJsonAsync(new { error = "Invalid token" });
                    return invalidResponse;
                }

                // Get user
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);

                if (user == null)
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFoundResponse.WriteAsJsonAsync(new { error = "User not found" });
                    return notFoundResponse;
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

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(userDto);
                return response;
            }
            catch (Exception ex)
            {
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new { error = $"Failed to get user: {ex.Message}" });
                return errorResponse;
            }
        }

        /// <summary>
        /// Send email verification
        /// POST /api/auth/send-verification
        /// </summary>
        [Function("SendEmailVerification")]
        public async Task<HttpResponseData> SendEmailVerification(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/send-verification")] HttpRequestData req)
        {
            try
            {
                var token = req.Headers.GetValues("Authorization").FirstOrDefault()?.Replace("Bearer ", "");
                if (string.IsNullOrEmpty(token))
                {
                    var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                    await unauthorizedResponse.WriteAsJsonAsync(new { error = "No token provided" });
                    return unauthorizedResponse;
                }

                var userId = _jwtService.GetUserIdFromToken(token);
                if (string.IsNullOrEmpty(userId))
                {
                    var invalidResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                    await invalidResponse.WriteAsJsonAsync(new { error = "Invalid token" });
                    return invalidResponse;
                }

                var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
                if (user == null)
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFoundResponse.WriteAsJsonAsync(new { error = "User not found" });
                    return notFoundResponse;
                }

                if (user.EmailConfirmed)
                {
                    var alreadyConfirmedResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await alreadyConfirmedResponse.WriteAsJsonAsync(new { error = "Email already confirmed" });
                    return alreadyConfirmedResponse;
                }

                // Generate verification token
                var verificationToken = Guid.NewGuid().ToString("N");
                user.EmailConfirmationToken = verificationToken;
                _context.Users.Update(user);
                await _context.SaveChangesAsync();

                // TODO: Send email with verification link
                // In production, send email via Azure Communication Services
                // var verificationLink = $"https://your-domain.com/verify-email?token={verificationToken}";

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
                {
                    message = "Verification email sent",
                    verificationToken // Remove in production!
                });
                return response;
            }
            catch (Exception ex)
            {
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new { error = $"Failed to send verification: {ex.Message}" });
                return errorResponse;
            }
        }

        /// <summary>
        /// Verify email with token
        /// POST /api/auth/verify-email
        /// </summary>
        [Function("VerifyEmail")]
        public async Task<HttpResponseData> VerifyEmail(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/verify-email")] HttpRequestData req)
        {
            try
            {
                var request = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(req.Body);
                var verificationToken = request?["verificationToken"];

                if (string.IsNullOrEmpty(verificationToken))
                {
                    var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteAsJsonAsync(new { error = "Verification token is required" });
                    return badRequestResponse;
                }

                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.EmailConfirmationToken == verificationToken);

                if (user == null)
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFoundResponse.WriteAsJsonAsync(new { error = "Invalid verification token" });
                    return notFoundResponse;
                }

                user.EmailConfirmed = true;
                user.EmailConfirmationToken = null;
                _context.Users.Update(user);
                await _context.SaveChangesAsync();

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new { message = "Email verified successfully" });
                return response;
            }
            catch (Exception ex)
            {
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new { error = $"Email verification failed: {ex.Message}" });
                return errorResponse;
            }
        }

        /// <summary>
        /// Request password reset
        /// POST /api/auth/forgot-password
        /// </summary>
        [Function("ForgotPassword")]
        public async Task<HttpResponseData> ForgotPassword(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/forgot-password")] HttpRequestData req)
        {
            try
            {
                var forgotPasswordRequest = await JsonSerializer.DeserializeAsync<ForgotPasswordRequest>(req.Body);

                if (forgotPasswordRequest == null || string.IsNullOrWhiteSpace(forgotPasswordRequest.Email))
                {
                    var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteAsJsonAsync(new { error = "Email is required" });
                    return badRequestResponse;
                }

                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Email.ToLower() == forgotPasswordRequest.Email.ToLower());

                // Always return success to prevent email enumeration
                if (user == null)
                {
                    var response = req.CreateResponse(HttpStatusCode.OK);
                    await response.WriteAsJsonAsync(new { message = "If the email exists, a password reset link has been sent" });
                    return response;
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

                var successResponse = req.CreateResponse(HttpStatusCode.OK);
                await successResponse.WriteAsJsonAsync(new
                {
                    message = "If the email exists, a password reset link has been sent",
                    resetToken // Remove in production!
                });
                return successResponse;
            }
            catch (Exception ex)
            {
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new { error = $"Password reset request failed: {ex.Message}" });
                return errorResponse;
            }
        }

        /// <summary>
        /// Reset password with token
        /// POST /api/auth/reset-password
        /// </summary>
        [Function("ResetPassword")]
        public async Task<HttpResponseData> ResetPassword(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/reset-password")] HttpRequestData req)
        {
            try
            {
                var resetPasswordRequest = await JsonSerializer.DeserializeAsync<ResetPasswordRequest>(req.Body);

                if (resetPasswordRequest == null ||
                    string.IsNullOrWhiteSpace(resetPasswordRequest.Email) ||
                    string.IsNullOrWhiteSpace(resetPasswordRequest.ResetToken) ||
                    string.IsNullOrWhiteSpace(resetPasswordRequest.NewPassword))
                {
                    var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteAsJsonAsync(new { error = "All fields are required" });
                    return badRequestResponse;
                }

                if (resetPasswordRequest.NewPassword != resetPasswordRequest.ConfirmPassword)
                {
                    var passwordMatchResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await passwordMatchResponse.WriteAsJsonAsync(new { error = "Passwords do not match" });
                    return passwordMatchResponse;
                }

                if (resetPasswordRequest.NewPassword.Length < 8)
                {
                    var passwordStrengthResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await passwordStrengthResponse.WriteAsJsonAsync(new { error = "Password must be at least 8 characters long" });
                    return passwordStrengthResponse;
                }

                var user = await _context.Users
                    .FirstOrDefaultAsync(u =>
                        u.Email.ToLower() == resetPasswordRequest.Email.ToLower() &&
                        u.PasswordResetToken == resetPasswordRequest.ResetToken);

                if (user == null ||
                    user.PasswordResetTokenExpiry == null ||
                    user.PasswordResetTokenExpiry < DateTime.UtcNow)
                {
                    var invalidResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await invalidResponse.WriteAsJsonAsync(new { error = "Invalid or expired reset token" });
                    return invalidResponse;
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

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new { message = "Password reset successfully" });
                return response;
            }
            catch (Exception ex)
            {
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new { error = $"Password reset failed: {ex.Message}" });
                return errorResponse;
            }
        }

        /// <summary>
        /// Change password (authenticated user)
        /// POST /api/auth/change-password
        /// </summary>
        [Function("ChangePassword")]
        public async Task<HttpResponseData> ChangePassword(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/change-password")] HttpRequestData req)
        {
            try
            {
                var token = req.Headers.GetValues("Authorization").FirstOrDefault()?.Replace("Bearer ", "");
                if (string.IsNullOrEmpty(token))
                {
                    var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                    await unauthorizedResponse.WriteAsJsonAsync(new { error = "No token provided" });
                    return unauthorizedResponse;
                }

                var userId = _jwtService.GetUserIdFromToken(token);
                if (string.IsNullOrEmpty(userId))
                {
                    var invalidResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                    await invalidResponse.WriteAsJsonAsync(new { error = "Invalid token" });
                    return invalidResponse;
                }

                var changePasswordRequest = await JsonSerializer.DeserializeAsync<ChangePasswordRequest>(req.Body);

                if (changePasswordRequest == null ||
                    string.IsNullOrWhiteSpace(changePasswordRequest.CurrentPassword) ||
                    string.IsNullOrWhiteSpace(changePasswordRequest.NewPassword))
                {
                    var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteAsJsonAsync(new { error = "All fields are required" });
                    return badRequestResponse;
                }

                if (changePasswordRequest.NewPassword != changePasswordRequest.ConfirmNewPassword)
                {
                    var passwordMatchResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await passwordMatchResponse.WriteAsJsonAsync(new { error = "New passwords do not match" });
                    return passwordMatchResponse;
                }

                if (changePasswordRequest.NewPassword.Length < 8)
                {
                    var passwordStrengthResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await passwordStrengthResponse.WriteAsJsonAsync(new { error = "Password must be at least 8 characters long" });
                    return passwordStrengthResponse;
                }

                var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
                if (user == null)
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFoundResponse.WriteAsJsonAsync(new { error = "User not found" });
                    return notFoundResponse;
                }

                // Verify current password
                if (!_passwordHasher.VerifyPassword(changePasswordRequest.CurrentPassword, user.PasswordHash))
                {
                    var incorrectPasswordResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await incorrectPasswordResponse.WriteAsJsonAsync(new { error = "Current password is incorrect" });
                    return incorrectPasswordResponse;
                }

                // Hash and update new password
                user.PasswordHash = _passwordHasher.HashPassword(changePasswordRequest.NewPassword);
                _context.Users.Update(user);
                await _context.SaveChangesAsync();

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new { message = "Password changed successfully" });
                return response;
            }
            catch (Exception ex)
            {
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new { error = $"Password change failed: {ex.Message}" });
                return errorResponse;
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
