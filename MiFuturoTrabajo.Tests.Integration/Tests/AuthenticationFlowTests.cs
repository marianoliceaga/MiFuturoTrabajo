using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MiFuturoTrabajo.Shared.Models;
using Xunit;

namespace MiFuturoTrabajo.Tests.Integration.Tests
{
    public class AuthenticationFlowTests : IClassFixture<TestWebApplicationFactory>
    {
        private readonly HttpClient _client;

        public AuthenticationFlowTests(TestWebApplicationFactory factory)
        {
            _client = factory.CreateClient();
        }

        [Fact]
        public async Task Register_Login_Logout_FullFlow_Success()
        {
            // Step 1: Register a new user
            var registerRequest = new RegisterRequest
            {
                Email = $"test{Guid.NewGuid()}@example.com",
                Password = "TestPassword123!",
                ConfirmPassword = "TestPassword123!",
                FirstName = "Test",
                LastName = "User"
            };

            var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", registerRequest);
            Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

            var authResponse = await registerResponse.Content.ReadFromJsonAsync<AuthResponse>();
            Assert.NotNull(authResponse);
            Assert.NotNull(authResponse.AccessToken);
            Assert.NotNull(authResponse.RefreshToken);
            Assert.Equal(registerRequest.Email, authResponse.Email);

            // Step 2: Login with the registered credentials
            var loginRequest = new LoginRequest
            {
                Email = registerRequest.Email,
                Password = registerRequest.Password,
                RememberMe = false
            };

            var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);
            Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

            var loginAuthResponse = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();
            Assert.NotNull(loginAuthResponse);
            Assert.NotNull(loginAuthResponse.AccessToken);

            // Step 3: Access protected endpoint (Get Current User)
            _client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", loginAuthResponse.AccessToken);

            var meResponse = await _client.GetAsync("/api/auth/me");
            Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);

            var userDto = await meResponse.Content.ReadFromJsonAsync<UserDto>();
            Assert.NotNull(userDto);
            Assert.Equal(registerRequest.Email, userDto.Email);

            // Step 4: Logout
            var logoutResponse = await _client.PostAsync("/api/auth/logout", null);
            Assert.Equal(HttpStatusCode.OK, logoutResponse.StatusCode);
        }

        [Fact]
        public async Task Register_DuplicateEmail_ReturnsConflict()
        {
            // Arrange
            var email = $"duplicate{Guid.NewGuid()}@example.com";
            var registerRequest = new RegisterRequest
            {
                Email = email,
                Password = "TestPassword123!",
                ConfirmPassword = "TestPassword123!",
                FirstName = "Test",
                LastName = "User"
            };

            // First registration
            await _client.PostAsJsonAsync("/api/auth/register", registerRequest);

            // Act - Try to register with same email
            var response = await _client.PostAsJsonAsync("/api/auth/register", registerRequest);

            // Assert
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }

        [Fact]
        public async Task Login_InvalidCredentials_ReturnsUnauthorized()
        {
            // Arrange
            var loginRequest = new LoginRequest
            {
                Email = "nonexistent@example.com",
                Password = "WrongPassword123!",
                RememberMe = false
            };

            // Act
            var response = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task RefreshToken_ValidToken_ReturnsNewTokens()
        {
            // Arrange - Register and login
            var registerRequest = new RegisterRequest
            {
                Email = $"refresh{Guid.NewGuid()}@example.com",
                Password = "TestPassword123!",
                ConfirmPassword = "TestPassword123!",
                FirstName = "Test",
                LastName = "User"
            };

            var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", registerRequest);
            var authResponse = await registerResponse.Content.ReadFromJsonAsync<AuthResponse>();
            Assert.NotNull(authResponse);

            // Act - Refresh token
            var refreshRequest = new RefreshTokenRequest
            {
                AccessToken = authResponse.AccessToken,
                RefreshToken = authResponse.RefreshToken
            };

            var refreshResponse = await _client.PostAsJsonAsync("/api/auth/refresh", refreshRequest);

            // Assert
            Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);

            var newAuthResponse = await refreshResponse.Content.ReadFromJsonAsync<AuthResponse>();
            Assert.NotNull(newAuthResponse);
            Assert.NotNull(newAuthResponse.AccessToken);
            Assert.NotNull(newAuthResponse.RefreshToken);
            Assert.NotEqual(authResponse.AccessToken, newAuthResponse.AccessToken);
            Assert.NotEqual(authResponse.RefreshToken, newAuthResponse.RefreshToken);
        }

        [Fact]
        public async Task RefreshToken_AfterLogout_ReturnsUnauthorized()
        {
            // Arrange - Register, login, and logout
            var registerRequest = new RegisterRequest
            {
                Email = $"logouttest{Guid.NewGuid()}@example.com",
                Password = "TestPassword123!",
                ConfirmPassword = "TestPassword123!",
                FirstName = "Test",
                LastName = "User"
            };

            var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", registerRequest);
            var authResponse = await registerResponse.Content.ReadFromJsonAsync<AuthResponse>();
            Assert.NotNull(authResponse);

            // Logout
            _client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", authResponse.AccessToken);
            await _client.PostAsync("/api/auth/logout", null);

            // Act - Try to refresh token after logout
            var refreshRequest = new RefreshTokenRequest
            {
                AccessToken = authResponse.AccessToken,
                RefreshToken = authResponse.RefreshToken
            };

            var refreshResponse = await _client.PostAsJsonAsync("/api/auth/refresh", refreshRequest);

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);
        }

        [Fact]
        public async Task ChangePassword_ValidRequest_Success()
        {
            // Arrange - Register
            var registerRequest = new RegisterRequest
            {
                Email = $"changepass{Guid.NewGuid()}@example.com",
                Password = "OldPassword123!",
                ConfirmPassword = "OldPassword123!",
                FirstName = "Test",
                LastName = "User"
            };

            var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", registerRequest);
            var authResponse = await registerResponse.Content.ReadFromJsonAsync<AuthResponse>();
            Assert.NotNull(authResponse);

            // Act - Change password
            _client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", authResponse.AccessToken);

            var changePasswordRequest = new ChangePasswordRequest
            {
                CurrentPassword = "OldPassword123!",
                NewPassword = "NewPassword123!",
                ConfirmNewPassword = "NewPassword123!"
            };

            var changeResponse = await _client.PostAsJsonAsync("/api/auth/change-password", changePasswordRequest);

            // Assert
            Assert.Equal(HttpStatusCode.OK, changeResponse.StatusCode);

            // Verify can login with new password
            _client.DefaultRequestHeaders.Authorization = null;
            var loginRequest = new LoginRequest
            {
                Email = registerRequest.Email,
                Password = "NewPassword123!",
                RememberMe = false
            };

            var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);
            Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        }

        [Fact]
        public async Task ForgotPassword_ResetPassword_FullFlow_Success()
        {
            // Arrange - Register
            var registerRequest = new RegisterRequest
            {
                Email = $"resetpass{Guid.NewGuid()}@example.com",
                Password = "OriginalPassword123!",
                ConfirmPassword = "OriginalPassword123!",
                FirstName = "Test",
                LastName = "User"
            };

            await _client.PostAsJsonAsync("/api/auth/register", registerRequest);

            // Step 1: Request password reset
            var forgotPasswordRequest = new ForgotPasswordRequest
            {
                Email = registerRequest.Email
            };

            var forgotResponse = await _client.PostAsJsonAsync("/api/auth/forgot-password", forgotPasswordRequest);
            Assert.Equal(HttpStatusCode.OK, forgotResponse.StatusCode);

            // Extract reset token from response (in production this would be sent via email)
            var forgotResponseData = await forgotResponse.Content.ReadAsStringAsync();
            Assert.Contains("resetToken", forgotResponseData);

            // Parse the reset token (this is a hack for testing - in production it would come from email)
            var forgotResponseObj = await forgotResponse.Content.ReadFromJsonAsync<Dictionary<string, object>>();
            var resetToken = forgotResponseObj?["resetToken"]?.ToString();
            Assert.NotNull(resetToken);

            // Step 2: Reset password with token
            var resetPasswordRequest = new ResetPasswordRequest
            {
                Email = registerRequest.Email,
                ResetToken = resetToken,
                NewPassword = "ResetPassword123!",
                ConfirmPassword = "ResetPassword123!"
            };

            var resetResponse = await _client.PostAsJsonAsync("/api/auth/reset-password", resetPasswordRequest);
            Assert.Equal(HttpStatusCode.OK, resetResponse.StatusCode);

            // Step 3: Verify can login with new password
            var loginRequest = new LoginRequest
            {
                Email = registerRequest.Email,
                Password = "ResetPassword123!",
                RememberMe = false
            };

            var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);
            Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        }

        [Fact]
        public async Task ProtectedEndpoint_WithoutToken_ReturnsUnauthorized()
        {
            // Act
            var response = await _client.GetAsync("/api/auth/me");

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Login_MultipleFailedAttempts_LocksAccount()
        {
            // Arrange - Register
            var registerRequest = new RegisterRequest
            {
                Email = $"locktest{Guid.NewGuid()}@example.com",
                Password = "CorrectPassword123!",
                ConfirmPassword = "CorrectPassword123!",
                FirstName = "Test",
                LastName = "User"
            };

            await _client.PostAsJsonAsync("/api/auth/register", registerRequest);

            // Act - Make 5 failed login attempts
            var loginRequest = new LoginRequest
            {
                Email = registerRequest.Email,
                Password = "WrongPassword123!",
                RememberMe = false
            };

            for (int i = 0; i < 5; i++)
            {
                await _client.PostAsJsonAsync("/api/auth/login", loginRequest);
            }

            // Assert - 6th attempt should return locked status
            var response = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            // Verify correct password also fails due to lock
            loginRequest.Password = "CorrectPassword123!";
            var lockedResponse = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);
            Assert.Equal(HttpStatusCode.Forbidden, lockedResponse.StatusCode);
        }

        [Theory]
        [InlineData("short")]
        [InlineData("1234567")]
        public async Task Register_WeakPassword_ReturnsBadRequest(string weakPassword)
        {
            // Arrange
            var registerRequest = new RegisterRequest
            {
                Email = $"weakpass{Guid.NewGuid()}@example.com",
                Password = weakPassword,
                ConfirmPassword = weakPassword,
                FirstName = "Test",
                LastName = "User"
            };

            // Act
            var response = await _client.PostAsJsonAsync("/api/auth/register", registerRequest);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Register_InvalidEmail_ReturnsBadRequest()
        {
            // Arrange
            var registerRequest = new RegisterRequest
            {
                Email = "invalid-email",
                Password = "TestPassword123!",
                ConfirmPassword = "TestPassword123!",
                FirstName = "Test",
                LastName = "User"
            };

            // Act
            var response = await _client.PostAsJsonAsync("/api/auth/register", registerRequest);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }
}
