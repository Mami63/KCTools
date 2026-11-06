using System;
using System.Security.Cryptography;
using System.Text;

namespace KCTools.LicenseServer.Services
{
    /// <summary>PBKDF2-SHA256 parola hash'i (salt + iterasyon alanlarıyla).</summary>
    public static class PasswordHasher
    {
        public const int DefaultIterations = 100_000;

        public static (string Hash, string Salt, int Iterations) Hash(string password)
        {
            var salt = RandomNumberGenerator.GetBytes(16);

            var hash = Rfc2898DeriveBytes.Pbkdf2(
                password, salt, DefaultIterations, HashAlgorithmName.SHA256, 32);

            return (Convert.ToBase64String(hash), Convert.ToBase64String(salt), DefaultIterations);
        }

        public static bool Verify(string password, string hashBase64, string saltBase64, int iterations)
        {
            try
            {
                var salt = Convert.FromBase64String(saltBase64);
                var expected = Convert.FromBase64String(hashBase64);

                var actual = Rfc2898DeriveBytes.Pbkdf2(
                    password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);

                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static string NewToken(int bytes = 48)
        {
            return Base64Url(RandomNumberGenerator.GetBytes(bytes));
        }

        public static string HashToken(string rawToken)
        {
            return Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)))
                .ToLowerInvariant();
        }

        private static string Base64Url(byte[] data)
        {
            return Convert.ToBase64String(data)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }
    }
}
