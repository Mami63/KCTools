using System;
using System.Security.Cryptography;
using System.Text;

namespace KCTools.LicenseServer.Services
{
    /// <summary>
    /// Cihaz anahtar kanıtı: ECDSA P-256 imza doğrulama ve SPKI parmak izi.
    /// Özel anahtar sunucuya asla gönderilmez.
    /// </summary>
    public static class DeviceProof
    {
        public const string SignatureAlgorithm = "ECDSA-P256-SHA256";

        public static string Thumbprint(string publicKeyPem)
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(publicKeyPem);

            var spki = ecdsa.ExportSubjectPublicKeyInfo();
            var hash = SHA256.HashData(spki);

            return Base64Url(hash);
        }

        public static bool Verify(string publicKeyPem, string payload, string signatureBase64)
        {
            try
            {
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportFromPem(publicKeyPem);

                return ecdsa.VerifyData(
                    Encoding.UTF8.GetBytes(payload),
                    Convert.FromBase64String(signatureBase64),
                    HashAlgorithmName.SHA256);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static string NewNonce()
        {
            return Base64Url(RandomNumberGenerator.GetBytes(32));
        }

        public static string HashNonce(string nonce)
        {
            return Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(nonce ?? string.Empty)))
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
