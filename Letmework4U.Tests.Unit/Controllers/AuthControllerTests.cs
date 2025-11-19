using Letmework4U.Api.Controllers;
using Letmework4U.Api.Services;
using Letmework4U.Infrastructure;
using Letmework4U.Infrastructure.Entities;
using Letmework4U.Shared.Enums;
using Letmework4U.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Letmework4U.Tests.Unit.Controllers
{
    public class AuthControllerTests : IDisposable
    {
        private readonly ApplicationDbContext _context;
        private readonly Mock<IJwtService> _jwtServiceMock;
        private readonly Mock<IPasswordHasher> _passwordHasherMock;
        private readonly Mock<IOAuthService> _oAuthServiceMock;
        private readonly Mock<ILogger<AuthController>> _loggerMock;
        private readonly AuthController _controller;

        public AuthControllerTests()
        {
            // Setup in-memory database
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _context = new ApplicationDbContext(options);

            // Setup mocks
            _jwtServiceMock = new Mock<IJwtService>();
            _passwordHasherMock = new Mock<IPasswordHasher>();
            _oAuthServiceMock = new Mock<IOAuthService>();
            _loggerMock = new Mock<ILogger<AuthController>>();

            _controller = new AuthController(
                _context,
                _jwtServiceMock.Object,
                _passwordHasherMock.Object,
                _oAuthServiceMock.Object,
                _loggerMock.Object
            );
        }

        public void Dispose()
        {
            _context.Database.EnsureDeleted();
            _context.Dispose();
        }

        [Fact]
        public async Task Register_ValidRequest_ReturnsCreatedWithAuthResponse()
        {
            // Arrange
            var request = new RegisterRequest
            {
                Email = "test@example.com",
                Password = "Password123!",
                ConfirmPassword = "Password123!",
                FirstName = "Test",
                LastName = "User"
            };

            _passwordHasherMock
                .Setup(x => x.HashPassword(It.IsAny<string>()))
                .Returns("hashedPassword");

            _jwtServiceMock
                .Setup(x => x.GenerateAccessToken(It.IsAny<string>(), It.IsAny<string>()))
                .Returns("accessToken");

            _jwtServiceMock
                .Setup(x => x.GenerateRefreshToken())
                .Returns("refreshToken");

            // Act
            var result = await _controller.Register(request);

            // Assert
            var statusCodeResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(201, statusCodeResult.StatusCode);

            var authResponse = Assert.IsType<AuthResponse>(statusCodeResult.Value);
            Assert.Equal(request.Email, authResponse.Email);
            Assert.Equal(request.FirstName, authResponse.FirstName);
            Assert.Equal(request.LastName, authResponse.LastName);
            Assert.Equal("accessToken", authResponse.AccessToken);
            Assert.Equal("refreshToken", authResponse.RefreshToken);

            // Verify user was created in database
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == request.Email.ToLower());
            Assert.NotNull(user);
            Assert.Equal("hashedPassword", user.PasswordHash);
        }

        [Fact]
        public async Task Register_DuplicateEmail_ReturnsConflict()
        {
            // Arrange
            var existingUser = new User
            {
                Email = "existing@example.com",
                PasswordHash = "hash",
                FirstName = "Existing",
                LastName = "User",
                CreatedAt = DateTime.UtcNow
            };
            _context.Users.Add(existingUser);
            await _context.SaveChangesAsync();

            var request = new RegisterRequest
            {
                Email = "existing@example.com",
                Password = "Password123!",
                ConfirmPassword = "Password123!",
                FirstName = "Test",
                LastName = "User"
            };

            // Act
            var result = await _controller.Register(request);

            // Assert
            var conflictResult = Assert.IsType<ConflictObjectResult>(result);
            Assert.Equal(409, conflictResult.StatusCode);
        }

        [Fact]
        public async Task Register_PasswordMismatch_ReturnsBadRequest()
        {
            // Arrange
            var request = new RegisterRequest
            {
                Email = "test@example.com",
                Password = "Password123!",
                ConfirmPassword = "DifferentPassword123!",
                FirstName = "Test",
                LastName = "User"
            };

            // Act
            var result = await _controller.Register(request);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal(400, badRequestResult.StatusCode);
        }

        [Theory]
        [InlineData("short")]
        [InlineData("pass")]
        [InlineData("1234567")]
        public async Task Register_ShortPassword_ReturnsBadRequest(string password)
        {
            // Arrange
            var request = new RegisterRequest
            {
                Email = "test@example.com",
                Password = password,
                ConfirmPassword = password,
                FirstName = "Test",
                LastName = "User"
            };

            // Act
            var result = await _controller.Register(request);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal(400, badRequestResult.StatusCode);
        }

        [Fact]
        public async Task Login_ValidCredentials_ReturnsOkWithAuthResponse()
        {
            // Arrange
            var user = new User
            {
                Email = "test@example.com",
                PasswordHash = "hashedPassword",
                FirstName = "Test",
                LastName = "User",
                CreatedAt = DateTime.UtcNow,
                IsActive = true,
                ApplicantId = Guid.NewGuid().ToString()
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            var request = new LoginRequest
            {
                Email = "test@example.com",
                Password = "Password123!",
                RememberMe = false
            };

            _passwordHasherMock
                .Setup(x => x.VerifyPassword(request.Password, user.PasswordHash))
                .Returns(true);

            _jwtServiceMock
                .Setup(x => x.GenerateAccessToken(It.IsAny<string>(), It.IsAny<string>()))
                .Returns("accessToken");

            _jwtServiceMock
                .Setup(x => x.GenerateRefreshToken())
                .Returns("refreshToken");

            // Act
            var result = await _controller.Login(request);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var authResponse = Assert.IsType<AuthResponse>(okResult.Value);
            Assert.Equal(user.Email, authResponse.Email);
            Assert.Equal("accessToken", authResponse.AccessToken);
        }

        [Fact]
        public async Task Login_InvalidPassword_ReturnsUnauthorized()
        {
            // Arrange
            var user = new User
            {
                Email = "test@example.com",
                PasswordHash = "hashedPassword",
                FirstName = "Test",
                LastName = "User",
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            var request = new LoginRequest
            {
                Email = "test@example.com",
                Password = "WrongPassword",
                RememberMe = false
            };

            _passwordHasherMock
                .Setup(x => x.VerifyPassword(request.Password, user.PasswordHash))
                .Returns(false);

            // Act
            var result = await _controller.Login(request);

            // Assert
            var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result);
            Assert.Equal(401, unauthorizedResult.StatusCode);

            // Verify failed login attempt was incremented
            var updatedUser = await _context.Users.FirstAsync(u => u.Email == user.Email);
            Assert.Equal(1, updatedUser.FailedLoginAttempts);
        }

        [Fact]
        public async Task Login_UserNotFound_ReturnsUnauthorized()
        {
            // Arrange
            var request = new LoginRequest
            {
                Email = "nonexistent@example.com",
                Password = "Password123!",
                RememberMe = false
            };

            // Act
            var result = await _controller.Login(request);

            // Assert
            var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result);
            Assert.Equal(401, unauthorizedResult.StatusCode);
        }

        [Fact]
        public async Task Login_LockedAccount_ReturnsForbidden()
        {
            // Arrange
            var user = new User
            {
                Email = "test@example.com",
                PasswordHash = "hashedPassword",
                FirstName = "Test",
                LastName = "User",
                CreatedAt = DateTime.UtcNow,
                IsActive = true,
                IsLocked = true,
                LockoutEnd = DateTime.UtcNow.AddMinutes(30)
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            var request = new LoginRequest
            {
                Email = "test@example.com",
                Password = "Password123!",
                RememberMe = false
            };

            // Act
            var result = await _controller.Login(request);

            // Assert
            var forbiddenResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(403, forbiddenResult.StatusCode);
        }

        [Fact]
        public async Task Login_MultipleFailedAttempts_LocksAccount()
        {
            // Arrange
            var user = new User
            {
                Email = "test@example.com",
                PasswordHash = "hashedPassword",
                FirstName = "Test",
                LastName = "User",
                CreatedAt = DateTime.UtcNow,
                IsActive = true,
                FailedLoginAttempts = 4 // One more failure will lock
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            var request = new LoginRequest
            {
                Email = "test@example.com",
                Password = "WrongPassword",
                RememberMe = false
            };

            _passwordHasherMock
                .Setup(x => x.VerifyPassword(request.Password, user.PasswordHash))
                .Returns(false);

            // Act
            var result = await _controller.Login(request);

            // Assert
            var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result);

            // Verify account was locked
            var updatedUser = await _context.Users.FirstAsync(u => u.Email == user.Email);
            Assert.Equal(5, updatedUser.FailedLoginAttempts);
            Assert.True(updatedUser.IsLocked);
            Assert.NotNull(updatedUser.LockoutEnd);
        }

        [Fact]
        public async Task Login_SuccessfulLoginAfterFailedAttempts_ResetsCounter()
        {
            // Arrange
            var user = new User
            {
                Email = "test@example.com",
                PasswordHash = "hashedPassword",
                FirstName = "Test",
                LastName = "User",
                CreatedAt = DateTime.UtcNow,
                IsActive = true,
                FailedLoginAttempts = 3,
                ApplicantId = Guid.NewGuid().ToString()
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            var request = new LoginRequest
            {
                Email = "test@example.com",
                Password = "CorrectPassword",
                RememberMe = false
            };

            _passwordHasherMock
                .Setup(x => x.VerifyPassword(request.Password, user.PasswordHash))
                .Returns(true);

            _jwtServiceMock
                .Setup(x => x.GenerateAccessToken(It.IsAny<string>(), It.IsAny<string>()))
                .Returns("accessToken");

            _jwtServiceMock
                .Setup(x => x.GenerateRefreshToken())
                .Returns("refreshToken");

            // Act
            var result = await _controller.Login(request);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);

            // Verify failed attempts were reset
            var updatedUser = await _context.Users.FirstAsync(u => u.Email == user.Email);
            Assert.Equal(0, updatedUser.FailedLoginAttempts);
            Assert.False(updatedUser.IsLocked);
        }

        [Fact]
        public async Task OAuthLogin_NewUser_CreatesUserAndReturnsAuthResponse()
        {
            // Arrange
            var request = new OAuthLoginRequest
            {
                Provider = OAuthProvider.Google,
                IdToken = "google-token"
            };

            var oauthUserInfo = new OAuthUserInfo
            {
                Email = "oauth@example.com",
                FirstName = "OAuth",
                LastName = "User",
                EmailVerified = true,
                Provider = OAuthProvider.Google,
                ProviderId = "google-123",
                ProfilePictureUrl = "https://example.com/pic.jpg"
            };

            _oAuthServiceMock
                .Setup(x => x.ValidateOAuthTokenAsync(request.Provider, request.IdToken))
                .ReturnsAsync(oauthUserInfo);

            _jwtServiceMock
                .Setup(x => x.GenerateAccessToken(It.IsAny<string>(), It.IsAny<string>()))
                .Returns("accessToken");

            _jwtServiceMock
                .Setup(x => x.GenerateRefreshToken())
                .Returns("refreshToken");

            // Act
            var result = await _controller.OAuthLogin(request);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var authResponse = Assert.IsType<AuthResponse>(okResult.Value);
            Assert.Equal(oauthUserInfo.Email, authResponse.Email);

            // Verify user was created
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == oauthUserInfo.Email);
            Assert.NotNull(user);
            Assert.Equal(OAuthProvider.Google.ToString(), user.OAuthProvider);
            Assert.True(user.EmailConfirmed);
        }

        [Fact]
        public async Task OAuthLogin_InvalidToken_ReturnsUnauthorized()
        {
            // Arrange
            var request = new OAuthLoginRequest
            {
                Provider = OAuthProvider.Google,
                IdToken = "invalid-token"
            };

            _oAuthServiceMock
                .Setup(x => x.ValidateOAuthTokenAsync(request.Provider, request.IdToken))
                .ReturnsAsync((OAuthUserInfo?)null);

            // Act
            var result = await _controller.OAuthLogin(request);

            // Assert
            var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result);
            Assert.Equal(401, unauthorizedResult.StatusCode);
        }
    }
}
