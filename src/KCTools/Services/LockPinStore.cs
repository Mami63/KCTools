using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace KCTools.Services
{
    /// <summary>
    /// Telefon kilit PIN'ini yalnızca kullanıcı isterse DPAPI (CurrentUser) ile
    /// şifreleyip saklar. Dosya silinirse otomatik kilit açma kapanır.
    /// </summary>
    public static class LockPinStore
    {
        /// <summary>Önce %LOCALAPPDATA%, yazılamıyorsa uygulama klasörü.</summary>
        public static IReadOnlyList<string> Paths
        {
            get
            {
                var local = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "KCTools", "lock-pin.dat");

                return new[]
                {
                    local,
                    Path.Combine(AppContext.BaseDirectory, "lock-pin.dat")
                };
            }
        }

        public static string StorePath
        {
            get
            {
                foreach (var path in Paths)
                {
                    if (File.Exists(path))
                    {
                        return path;
                    }
                }

                return Paths[0];
            }
        }
        public static string LastError { get; private set; }

        public static bool HasPin(string serial)
        {
            return !string.IsNullOrWhiteSpace(Load(serial));
        }

        public static void Save(string serial, string pin)
        {
            var payload = serial + "|" + pin;
            var errors = new List<string>();

            foreach (var path in Paths)
            {
                try
                {
                    var directory = Path.GetDirectoryName(path);

                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    var encrypted = ProtectedData.Protect(
                        Encoding.UTF8.GetBytes(payload), null, DataProtectionScope.CurrentUser);

                    File.WriteAllBytes(path, encrypted);
                    LastError = null;

                    LogHub.Instance.Info(
                        "Kilit PIN'i DPAPI ile şifrelenerek kaydedildi: " + path, "KİLİT");
                    return;
                }
                catch (Exception ex)
                {
                    errors.Add(Path.GetFileName(path) + ": " + ex.GetType().Name);
                }
            }

            LastError = string.Join(" | ", errors);
            LogHub.Instance.Warn("PIN kaydedilemedi: " + LastError, "KİLİT");
        }
        public static string Load(string serial)
        {
            foreach (var path in Paths)
            {
                try
                {
                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    var decrypted = ProtectedData.Unprotect(
                        File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);

                    var payload = Encoding.UTF8.GetString(decrypted);
                    var separator = payload.IndexOf('|');

                    if (separator <= 0)
                    {
                        continue;
                    }

                    var savedSerial = payload.Substring(0, separator);
                    var pin = payload.Substring(separator + 1);

                    if (string.Equals(savedSerial, serial, StringComparison.OrdinalIgnoreCase))
                    {
                        return pin;
                    }
                }
                catch (Exception)
                {
                    // sıradaki konumu dene
                }
            }

            return null;
        }
        public static void Clear()
        {
            foreach (var path in Paths)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
                catch (Exception)
                {
                    // yok sayılır
                }
            }
        }    }
}
