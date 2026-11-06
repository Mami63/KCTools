using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KCTools.Security
{
    /// <summary>OIDC oturumu + offline entitlement; DPAPI ile saklanır.</summary>
    public sealed class OidcSession
    {
        public string AccessToken { get; set; }

        public string RefreshToken { get; set; }

        public DateTimeOffset AccessExpiresAt { get; set; }

        public string OfflineToken { get; set; }

        public string PublicKeyPem { get; set; }

        public string Email { get; set; }

        public DateTimeOffset? LicenseEnd { get; set; }

        public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    public static class SecureSessionStore
    {
        private static readonly byte[] Entropy =
            Encoding.UTF8.GetBytes("KCTools.OidcSession.v1");

        private static string Folder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KCTools");

        private static string FilePath => Path.Combine(Folder, "oidc-session.dat");

        public static OidcSession Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    return null;
                }

                var json = ProtectedData.Unprotect(
                    File.ReadAllBytes(FilePath), Entropy, DataProtectionScope.CurrentUser);

                return JsonSerializer.Deserialize<OidcSession>(json);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static void Save(OidcSession session)
        {
            try
            {
                Directory.CreateDirectory(Folder);

                var json = JsonSerializer.SerializeToUtf8Bytes(session);
                File.WriteAllBytes(
                    FilePath, ProtectedData.Protect(json, Entropy, DataProtectionScope.CurrentUser));
            }
            catch (Exception)
            {
                // yazılamazsa yalnızca bellekte kalır
            }
        }

        public static void Clear()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    File.Delete(FilePath);
                }
            }
            catch (Exception)
            {
                // yok sayılır
            }
        }
    }
}
