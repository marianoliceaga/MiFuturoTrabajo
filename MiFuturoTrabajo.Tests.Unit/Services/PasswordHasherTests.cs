using MiFuturoTrabajo.Api.Services;
using Xunit;

namespace MiFuturoTrabajo.Tests.Unit.Services
{
    public class PasswordHasherTests
    {
        private readonly PasswordHasher _passwordHasher;

        public PasswordHasherTests()
        {
            _passwordHasher = new PasswordHasher();
        }

        [Fact]
        public void HashPassword_ShouldReturnNonEmptyString()
        {
            // Arrange
            var password = "TestPassword123!";

            // Act
            var hash = _passwordHasher.HashPassword(password);

            // Assert
            Assert.NotNull(hash);
            Assert.NotEmpty(hash);
        }

        [Fact]
        public void HashPassword_ShouldReturnDifferentHashesForSamePassword()
        {
            // Arrange
            var password = "TestPassword123!";

            // Act
            var hash1 = _passwordHasher.HashPassword(password);
            var hash2 = _passwordHasher.HashPassword(password);

            // Assert
            Assert.NotEqual(hash1, hash2); // BCrypt uses salts, so hashes should differ
        }

        [Fact]
        public void VerifyPassword_ShouldReturnTrueForCorrectPassword()
        {
            // Arrange
            var password = "TestPassword123!";
            var hash = _passwordHasher.HashPassword(password);

            // Act
            var isValid = _passwordHasher.VerifyPassword(password, hash);

            // Assert
            Assert.True(isValid);
        }

        [Fact]
        public void VerifyPassword_ShouldReturnFalseForIncorrectPassword()
        {
            // Arrange
            var password = "TestPassword123!";
            var wrongPassword = "WrongPassword456!";
            var hash = _passwordHasher.HashPassword(password);

            // Act
            var isValid = _passwordHasher.VerifyPassword(wrongPassword, hash);

            // Assert
            Assert.False(isValid);
        }

        [Fact]
        public void VerifyPassword_ShouldHandleEmptyPassword()
        {
            // Arrange
            var password = "TestPassword123!";
            var hash = _passwordHasher.HashPassword(password);

            // Act
            var isValid = _passwordHasher.VerifyPassword("", hash);

            // Assert
            Assert.False(isValid);
        }

        [Theory]
        [InlineData("short")]
        [InlineData("longerpassword123")]
        [InlineData("P@ssw0rd!")]
        [InlineData("ComplexP@ssw0rd123!@#$%")]
        public void HashPassword_ShouldHandleVariousPasswordLengths(string password)
        {
            // Act
            var hash = _passwordHasher.HashPassword(password);

            // Assert
            Assert.NotNull(hash);
            Assert.NotEmpty(hash);
            Assert.True(_passwordHasher.VerifyPassword(password, hash));
        }

        [Fact]
        public void VerifyPassword_ShouldHandleNullHash()
        {
            // Arrange
            var password = "TestPassword123!";

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => _passwordHasher.VerifyPassword(password, null!));
        }
    }
}
