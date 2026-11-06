using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KCTools.Services;

namespace KCTools.Security
{
    /// <summary>Diskte tutulan oturum bilgisi.</summary>
    public sealed class StoredSession
    {
        public string Token { get; set; }

        public string Hwid { get; set; }

        public string Key { get; set; }

        public string Plan { get; set; }

        public int Credits { get; set; }

        public DateTimeOffset? ExpiresAt { get; set; }

        public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// JWT'yi DPAPI (CurrentUser) ile şifreleyip %LOCALAPPDATA%\KCTools\session.dat
    /// dosyasında saklar. Dosya başka bir kullanıcı/makine tarafından çözülemez.
    /// </summary>
    public static class TokenStore
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("KCTools.SessionStore.v1");

        public static string FolderPath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KCTools");

        public static string FilePath => Path.Combine(FolderPath, "session.dat");

        public static bool Save(StoredSession session)
        {
            if (session == null || string.IsNullOrWhiteSpace(session.Token))
            {
                return false;
            }

            try
            {
                Directory.CreateDirectory(FolderPath);

                var json = JsonSerializer.SerializeToUtf8Bytes(session);
                var encrypted = ProtectedData.Protect(json, Entropy, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(FilePath, encrypted);

                return true;
            }
            catch (Exception ex)
            {
                LogHub.Instance.Warn("Oturum kaydedilemedi: " + ex.Message, "LİSANS");
                return false;
            }
        }

        public static StoredSession Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    return null;
                }

                var encrypted = File.ReadAllBytes(FilePath);
                var json = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);

                return JsonSerializer.Deserialize<StoredSession>(json);
            }
            catch (Exception)
            {
                // Bozuk/eski dosya: sessizce yok sayılır, kullanıcı yeniden giriş yapar.
                return null;
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
