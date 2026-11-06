using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KCTools.Security
{
    /// <summary>
    /// Cihaz anahtar çifti: ECDSA P-256.
    /// Öncelik: Windows CNG (dışa aktarılamayan özel anahtar) → DPAPI dosyası.
    /// Sunucuya yalnızca açık anahtar gönderilir.
    /// </summary>
    public static class DeviceKeyStore
    {
        private const string CngKeyName = "KCTools.DeviceKey.v1";

        private static readonly byte[] Entropy =
            Encoding.UTF8.GetBytes("KCTools.DeviceKey.v1");

        private static readonly object Sync = new object();
        private static (Guid DeviceId, string PublicKeyPem, Func<string, string> Sign) _cached;

        private static string Folder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KCTools");

        private static string FilePath => Path.Combine(Folder, "device-key.dat");

        public static (Guid DeviceId, string PublicKeyPem, Func<string, string> Sign) GetOrCreate()
        {
            lock (Sync)
            {
                if (_cached.PublicKeyPem != null)
                {
                    return _cached;
                }

                try
                {
                    _cached = CreateCng();
                }
                catch (Exception)
                {
                    try
                    {
                        _cached = CreateFromDpapiFile();
                    }
                    catch (Exception)
                    {
                        _cached = CreateEphemeral();
                    }
                }

                return _cached;
            }
        }

        private static (Guid, string, Func<string, string>) CreateCng()
        {
            var provider = CngProvider.MicrosoftSoftwareKeyStorageProvider;

            var cng = CngKey.Exists(CngKeyName, provider)
                ? CngKey.Open(CngKeyName, provider)
                : CngKey.Create(CngAlgorithm.ECDsaP256, CngKeyName, new CngKeyCreationParameters
                {
                    Provider = provider,
                    ExportPolicy = CngExportPolicies.None,
                    KeyUsage = CngKeyUsages.Signing
                });

            var ecdsa = new ECDsaCng(cng);
            var publicPem = ecdsa.ExportSubjectPublicKeyInfoPem();
            var deviceId = DeviceIdFrom(publicPem);

            return (deviceId, publicPem,
                payload => Convert.ToBase64String(
                    ecdsa.SignData(Encoding.UTF8.GetBytes(payload), HashAlgorithmName.SHA256)));
        }

        private static (Guid, string, Func<string, string>) CreateFromDpapiFile()
        {
            Material material = LoadFile();

            if (material == null)
            {
                using var created = ECDsa.Create(ECCurve.NamedCurves.nistP256);

                material = new Material
                {
                    PrivateKeyPkcs8Base64 = Convert.ToBase64String(created.ExportPkcs8PrivateKey())
                };

                SaveFile(material);
            }

            var key = ECDsa.Create();
            key.ImportPkcs8PrivateKey(Convert.FromBase64String(material.PrivateKeyPkcs8Base64), out _);

            var publicPem = key.ExportSubjectPublicKeyInfoPem();
            var deviceId = DeviceIdFrom(publicPem);

            return (deviceId, publicPem,
                payload => Convert.ToBase64String(
                    key.SignData(Encoding.UTF8.GetBytes(payload), HashAlgorithmName.SHA256)));
        }

        private static (Guid, string, Func<string, string>) CreateEphemeral()
        {
            var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var publicPem = key.ExportSubjectPublicKeyInfoPem();

            return (DeviceIdFrom(publicPem), publicPem,
                payload => Convert.ToBase64String(
                    key.SignData(Encoding.UTF8.GetBytes(payload), HashAlgorithmName.SHA256)));
        }

        /// <summary>Cihaz kimliği açık anahtardan türetilir; ayrıca saklanmaz.</summary>
        private static Guid DeviceIdFrom(string publicKeyPem)
        {
            var spki = Convert.FromBase64String(
                publicKeyPem
                    .Replace("-----BEGIN PUBLIC KEY-----", string.Empty)
                    .Replace("-----END PUBLIC KEY-----", string.Empty)
                    .Replace("\r", string.Empty)
                    .Replace("\n", string.Empty)
                    .Trim());

            var hash = SHA256.HashData(spki);
            var bytes = new byte[16];
            Array.Copy(hash, bytes, 16);

            return new Guid(bytes);
        }

        private sealed class Material
        {
            public string PrivateKeyPkcs8Base64 { get; set; } = string.Empty;
        }

        private static Material LoadFile()
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    return null;
                }

                var json = ProtectedData.Unprotect(
                    File.ReadAllBytes(FilePath), Entropy, DataProtectionScope.CurrentUser);

                return JsonSerializer.Deserialize<Material>(json);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void SaveFile(Material material)
        {
            try
            {
                Directory.CreateDirectory(Folder);

                var json = JsonSerializer.SerializeToUtf8Bytes(material);
                File.WriteAllBytes(
                    FilePath, ProtectedData.Protect(json, Entropy, DataProtectionScope.CurrentUser));
            }
            catch (Exception)
            {
                // kalıcı yazma başarısızsa anahtar yalnızca oturumda kullanılır
            }
        }
    }
}
