using MiFuturoTrabajo.Api.Services;
using Xunit;

namespace MiFuturoTrabajo.Tests.Unit.Services
{
    public class SecurityTokensTests
    {
        [Fact]
        public void Create_ProducesUrlSafeTokens()
        {
            var token = SecurityTokens.Create();

            Assert.DoesNotContain('+', token);
            Assert.DoesNotContain('/', token);
            Assert.DoesNotContain('=', token);
            Assert.Equal(Uri.EscapeDataString(token), token);
        }

        /// <summary>
        /// 32 random bytes, base64url-encoded without padding. Shorter would
        /// mean the token is guessable; this asserts the entropy is actually
        /// there rather than trusting the constant.
        /// </summary>
        [Fact]
        public void Create_CarriesFullEntropy()
        {
            Assert.Equal(43, SecurityTokens.Create().Length);
        }

        [Fact]
        public void Create_DoesNotRepeat()
        {
            var tokens = Enumerable.Range(0, 500).Select(_ => SecurityTokens.Create()).ToList();

            Assert.Equal(tokens.Count, tokens.Distinct().Count());
        }

        [Fact]
        public void Hash_IsNotTheToken()
        {
            var token = SecurityTokens.Create();

            Assert.NotEqual(token, SecurityTokens.Hash(token));
        }

        [Fact]
        public void Hash_IsStable()
        {
            var token = SecurityTokens.Create();

            Assert.Equal(SecurityTokens.Hash(token), SecurityTokens.Hash(token));
        }

        [Fact]
        public void Matches_AcceptsTheRightToken()
        {
            var token = SecurityTokens.Create();

            Assert.True(SecurityTokens.Matches(token, SecurityTokens.Hash(token)));
        }

        [Fact]
        public void Matches_RejectsADifferentToken()
        {
            var stored = SecurityTokens.Hash(SecurityTokens.Create());

            Assert.False(SecurityTokens.Matches(SecurityTokens.Create(), stored));
        }

        /// <summary>
        /// The stored hash is null once a token is spent. Presenting the hash
        /// itself, or nothing at all, must not be treated as a match.
        /// </summary>
        [Theory]
        [InlineData(null, null)]
        [InlineData("", null)]
        [InlineData(null, "somehash")]
        [InlineData("", "somehash")]
        [InlineData("token", null)]
        [InlineData("token", "")]
        public void Matches_RejectsMissingValues(string? presented, string? stored)
        {
            Assert.False(SecurityTokens.Matches(presented, stored));
        }

        /// <summary>
        /// Someone who read the database row holds the hash, not the token.
        /// Replaying the hash must not authenticate.
        /// </summary>
        [Fact]
        public void Matches_RejectsTheStoredHashReplayed()
        {
            var token = SecurityTokens.Create();
            var stored = SecurityTokens.Hash(token);

            Assert.False(SecurityTokens.Matches(stored, stored));
        }
    }
}
