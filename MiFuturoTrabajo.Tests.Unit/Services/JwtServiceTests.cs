using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MiFuturoTrabajo.Api.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MiFuturoTrabajo.Tests.Unit.Services
{
    public class JwtServiceTests
    {
        private readonly IJwtService _jwtService;
        private readonly IConfiguration _configuration;

        public JwtServiceTests()
        {
            var configurationBuilder = new ConfigurationBuilder();
            configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Jwt:SecretKey", "ThisIsAVeryLongSecretKeyForTestingPurposesOnly12345678901234567890" },
                { "Jwt:Issuer", "TestIssuer" },
                { "Jwt:Audience", "TestAudience" },
                { "Jwt:AccessTokenExpiryMinutes", "60" },
                { "Jwt:RefreshTokenExpiryDays", "7" }
            });
            _configuration = configurationBuilder.Build();
            _jwtService = new JwtService(_configuration);
        }

        [Fact]
        public void GenerateAccessToken_ShouldReturnValidToken()
        {
            // Arrange
            var userId = Guid.NewGuid().ToString();
            var email = "test@example.com";

            // Act
            var token = _jwtService.GenerateAccessToken(userId, email);

            // Assert
            Assert.NotNull(token);
            Assert.NotEmpty(token);

            // Verify it's a valid JWT format
            var handler = new JwtSecurityTokenHandler();
            Assert.True(handler.CanReadToken(token));
        }

        [Fact]
        public void GenerateAccessToken_ShouldContainCorrectClaims()
        {
            // Arrange
            var userId = Guid.NewGuid().ToString();
            var email = "test@example.com";

            // Act
            var token = _jwtService.GenerateAccessToken(userId, email);
            var principal = _jwtService.ValidateToken(token);

            // Assert
            Assert.NotNull(principal);
            Assert.Equal(userId, principal.FindFirst(ClaimTypes.NameIdentifier)?.Value);
            Assert.Equal(email, principal.FindFirst(ClaimTypes.Email)?.Value);
            // Assert.Equal(userId, principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value); // Mapped to NameIdentifier by default
        }

        [Fact]
        public void GenerateRefreshToken_ShouldReturnNonEmptyString()
        {
            // Act
            var refreshToken = _jwtService.GenerateRefreshToken();

            // Assert
            Assert.NotNull(refreshToken);
            Assert.NotEmpty(refreshToken);
        }

        [Fact]
        public void GenerateRefreshToken_ShouldReturnDifferentTokensEachTime()
        {
            // Act
            var token1 = _jwtService.GenerateRefreshToken();
            var token2 = _jwtService.GenerateRefreshToken();

            // Assert
            Assert.NotEqual(token1, token2);
        }

        [Fact]
        public void ValidateToken_ShouldReturnPrincipalForValidToken()
        {
            // Arrange
            var userId = Guid.NewGuid().ToString();
            var email = "test@example.com";
            var token = _jwtService.GenerateAccessToken(userId, email);

            // Act
            var principal = _jwtService.ValidateToken(token);

            // Assert
            Assert.NotNull(principal);
            Assert.NotNull(principal.Identity);
            Assert.True(principal.Identity.IsAuthenticated);
        }

        [Fact]
        public void ValidateToken_ShouldReturnNullForInvalidToken()
        {
            // Arrange
            var invalidToken = "invalid.token.here";

            // Act
            var principal = _jwtService.ValidateToken(invalidToken);

            // Assert
            Assert.Null(principal);
        }

        [Fact]
        public void ValidateToken_ShouldReturnNullForEmptyToken()
        {
            // Act
            var principal = _jwtService.ValidateToken("");

            // Assert
            Assert.Null(principal);
        }

        [Fact]
        public void GetUserIdFromToken_ShouldReturnCorrectUserId()
        {
            // Arrange
            var userId = Guid.NewGuid().ToString();
            var email = "test@example.com";
            var token = _jwtService.GenerateAccessToken(userId, email);

            // Act
            var extractedUserId = _jwtService.GetUserIdFromToken(token);

            // Assert
            Assert.Equal(userId, extractedUserId);
        }

        [Fact]
        public void GetUserIdFromToken_ShouldReturnNullForInvalidToken()
        {
            // Arrange
            var invalidToken = "invalid.token.here";

            // Act
            var userId = _jwtService.GetUserIdFromToken(invalidToken);

            // Assert
            Assert.Null(userId);
        }

        [Fact]
        public void GenerateAccessToken_ShouldCreateTokenWithCorrectIssuerAndAudience()
        {
            // Arrange
            var userId = Guid.NewGuid().ToString();
            var email = "test@example.com";

            // Act
            var token = _jwtService.GenerateAccessToken(userId, email);
            var handler = new JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(token);

            // Assert
            Assert.Equal("TestIssuer", jwtToken.Issuer);
            Assert.Contains("TestAudience", jwtToken.Audiences);
        }

        [Fact]
        public void GenerateAccessToken_ShouldIncludeJtiClaim()
        {
            // Arrange
            var userId = Guid.NewGuid().ToString();
            var email = "test@example.com";

            // Act
            var token = _jwtService.GenerateAccessToken(userId, email);
            var principal = _jwtService.ValidateToken(token);

            // Assert
            Assert.NotNull(principal);
            var jtiClaim = principal.FindFirst(JwtRegisteredClaimNames.Jti);
            Assert.NotNull(jtiClaim);
            Assert.NotEmpty(jtiClaim.Value);

            // Verify it's a valid GUID
            Assert.True(Guid.TryParse(jtiClaim.Value, out _));
        }

        [Fact]
        public void GenerateAccessToken_ShouldCreateDifferentJtiForEachToken()
        {
            // Arrange
            var userId = Guid.NewGuid().ToString();
            var email = "test@example.com";

            // Act
            var token1 = _jwtService.GenerateAccessToken(userId, email);
            var token2 = _jwtService.GenerateAccessToken(userId, email);

            var principal1 = _jwtService.ValidateToken(token1);
            var principal2 = _jwtService.ValidateToken(token2);

            // Assert
            var jti1 = principal1?.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
            var jti2 = principal2?.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;

            Assert.NotEqual(jti1, jti2);
        }

        [Theory]
        [InlineData("user@example.com")]
        [InlineData("test.user@domain.co.uk")]
        [InlineData("admin@company.org")]
        public void GenerateAccessToken_ShouldHandleVariousEmailFormats(string email)
        {
            // Arrange
            var userId = Guid.NewGuid().ToString();

            // Act
            var token = _jwtService.GenerateAccessToken(userId, email);
            var principal = _jwtService.ValidateToken(token);

            // Assert
            Assert.NotNull(principal);
            Assert.Equal(email, principal.FindFirst(ClaimTypes.Email)?.Value);
        }
    }
}
