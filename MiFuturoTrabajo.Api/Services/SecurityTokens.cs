using System.Security.Cryptography;
using System.Text;

namespace MiFuturoTrabajo.Api.Services
{
    /// <summary>
    /// Single-use credentials sent by email: password reset and email
    /// verification.
    ///
    /// Two decisions worth stating, because both look wrong at a glance:
    ///
    /// 1. Tokens are generated from a CSPRNG, not Guid.NewGuid(). A GUID is
    ///    designed to be unique, not unguessable - v4 exposes only 122 bits and
    ///    its generator carries no such guarantee. These are 256 bits of
    ///    cryptographic randomness.
    ///
    /// 2. They are stored as a plain SHA-256 hash, with no salt and no work
    ///    factor - deliberately unlike a password. A password is low-entropy
    ///    and human-chosen, so it needs BCrypt to make guessing expensive. A
    ///    256-bit random token has no dictionary to attack, so the only thing
    ///    hashing must buy is that a database read does not yield a usable
    ///    credential. A fast hash does that; a slow one would only add latency
    ///    to every redemption.
    /// </summary>
    public static class SecurityTokens
    {
        /// <summary>A URL-safe token with 256 bits of entropy.</summary>
        public static string Create()
        {
            var bytes = RandomNumberGenerator.GetBytes(32);

            return Convert.ToBase64String(bytes)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
        }

        public static string Hash(string token) =>
            Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

        /// <summary>
        /// Compares in constant time. The comparison is against a hash rather
        /// than the token, so a timing leak would be of limited use - but a
        /// fixed-time compare costs nothing and removes the question.
        /// </summary>
        public static bool Matches(string? presented, string? storedHash)
        {
            if (string.IsNullOrEmpty(presented) || string.IsNullOrEmpty(storedHash))
            {
                return false;
            }

            var presentedHash = Encoding.UTF8.GetBytes(Hash(presented));
            var stored = Encoding.UTF8.GetBytes(storedHash);

            return CryptographicOperations.FixedTimeEquals(presentedHash, stored);
        }
    }
}
