using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KCTools.LicenseServer.Data;

namespace KCTools.LicenseServer.Services
{
    /// <summary>
    /// 72 saatlik çevrimdışı yetki belgesi: RS256 imzalı JWT.
    /// Tolerans, lisansın gerçek bitiş tarihini asla aşmaz.
    /// </summary>
    public sealed class OfflineEntitlementService
    {
        private static readonly JsonSerializerOptions JsonOptions =
            new JsonSerializerOptions { PropertyNamingPolicy = null };

        private readonly RSA _rsa;

        public OfflineEntitlementService(string keyPath)
        {
            _rsa = RSA.Create(2048);

            try
            {
                if (File.Exists(keyPath))
                {
                    _rsa.ImportFromPem(File.ReadAllText(keyPath));
                }
                else
                {
                    var directory = Path.GetDirectoryName(keyPath);

                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    File.WriteAllText(keyPath, _rsa.ExportPkcs8PrivateKeyPem());
                }
            }
            catch (Exception)
            {
                // Dosya yazılamazsa süreç ömrü boyunca geçerli anahtarla devam edilir.
            }
        }

        public string PublicKeyPem => _rsa.ExportSubjectPublicKeyInfoPem();

        public string Issue(License license, Device? device, Guid userId, int toleranceHours)
        {
            var now = DateTimeOffset.UtcNow;

            var offlineUntil = license.EndsAt < now.AddHours(toleranceHours)
                ? license.EndsAt
                : now.AddHours(toleranceHours);

            if (offlineUntil < now)
            {
                offlineUntil = now;
            }

            var payload = new Dictionary<string, object>
            {
                ["iss"] = "kctools-license",
                ["sub"] = license.Key,
                ["uid"] = userId.ToString(),
                ["plan"] = license.Plan,
                ["credits"] = license.Credits,
                ["device_id"] = device?.DeviceId.ToString() ?? string.Empty,
                ["hwid"] = device?.Hwid ?? string.Empty,
                ["license_start"] = license.StartsAt.ToUnixTimeSeconds(),
                ["license_end"] = license.EndsAt.ToUnixTimeSeconds(),
                ["offline_until"] = offlineUntil.ToUnixTimeSeconds(),
                ["iat"] = now.ToUnixTimeSeconds(),
                ["exp"] = offlineUntil.ToUnixTimeSeconds()
            };

            var header = new Dictionary<string, object>
            {
                ["alg"] = "RS256",
                ["typ"] = "JWT"
            };

            var headerPart = Base64Url(JsonSerializer.SerializeToUtf8Bytes(header, JsonOptions));
            var payloadPart = Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions));
            var signingInput = headerPart + "." + payloadPart;

            var signature = _rsa.SignData(
                Encoding.ASCII.GetBytes(signingInput),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            return signingInput + "." + Base64Url(signature);
        }

        /// <summary>Kısa ömürlü işlem (job) token'ı: yetkili cihaz + chipset.</summary>
        public string IssueJobToken(
            Guid userId, string hwid, string chipset, string operation,
            TimeSpan lifetime, out DateTimeOffset expiresAt)
        {
            var now = DateTimeOffset.UtcNow;
            expiresAt = now.Add(lifetime);

            var payload = new Dictionary<string, object>
            {
                ["iss"] = "kctools-license",
                ["typ"] = "job",
                ["sub"] = userId.ToString(),
                ["jti"] = Guid.NewGuid().ToString(),
                ["hwid"] = hwid,
                ["chipset"] = chipset,
                ["operation"] = operation,
                ["iat"] = now.ToUnixTimeSeconds(),
                ["exp"] = expiresAt.ToUnixTimeSeconds()
            };

            var header = new Dictionary<string, object>
            {
                ["alg"] = "RS256",
                ["typ"] = "JWT"
            };

            var headerPart = Base64Url(JsonSerializer.SerializeToUtf8Bytes(header, JsonOptions));
            var payloadPart = Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions));
            var signingInput = headerPart + "." + payloadPart;

            var signature = _rsa.SignData(
                Encoding.ASCII.GetBytes(signingInput),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            return signingInput + "." + Base64Url(signature);
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
