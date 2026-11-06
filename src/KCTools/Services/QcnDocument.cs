using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Security.Cryptography;
using System.Text;
using KCTools.Common;

namespace KCTools.Services
{
    /// <summary>QCN dosyasında bulunan bir eşleşme (IMEI / hex dizi / ASCII).</summary>
    public sealed class QcnMatch
    {
        public int Offset { get; set; }

        public int Length { get; set; }

        public string Kind { get; set; } = "—";

        public string Preview { get; set; } = string.Empty;

        public string OffsetText => "0x" + Offset.ToString("X6");

        public string SizeText => Length + " bayt";
    }

    /// <summary>
    /// QCN dosyası işlemleri: yükleme, içerik analizi, IMEI/hex arama, yerinde düzenleme
    /// ve yedekli kaydetme.
    ///
    /// Not: QCN konteyner biçimi üreticiye göre değişebilir; bu sınıf dosyayı
    /// yorumlamak yerine **hedefli bayt düzenleme** yapar (arama + aynı uzunlukta yazma)
    /// ve her kayıt öncesi otomatik yedek alır.
    /// </summary>
    public sealed class QcnDocument
    {
        private readonly List<string> _hints = new List<string>();

        private QcnDocument()
        {
        }

        public string FilePath { get; private set; }

        public byte[] Data { get; private set; }

        public string Sha256 { get; private set; } = "—";

        public int Size => Data?.Length ?? 0;

        public bool IsDirty { get; private set; }

        public IReadOnlyList<string> Hints => _hints;

        public string FileName => FilePath == null ? "—" : Path.GetFileName(FilePath);

        public static QcnDocument Load(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("QCN dosyası bulunamadı.", path);
            }

            var document = new QcnDocument
            {
                FilePath = path,
                Data = File.ReadAllBytes(path)
            };

            document.Sha256 = ComputeSha256(document.Data);
            document.Analyze();

            LogHub.Instance.Ok(
                "QCN yüklendi: " + Path.GetFileName(path) + " · " + document.Size.ToString("N0") + " bayt", "QCN");

            return document;
        }

        public List<QcnMatch> FindAscii(string text)
        {
            return string.IsNullOrEmpty(text)
                ? new List<QcnMatch>()
                : FindBytes(Encoding.ASCII.GetBytes(text), "ASCII");
        }

        public List<QcnMatch> FindHex(string hexText)
        {
            if (!Hex.TryParse(hexText, out var bytes))
            {
                return new List<QcnMatch>();
            }

            return FindBytes(bytes, "HEX");
        }

        /// <summary>IMEI'yi hem ASCII hem BCD (nibble takaslı) biçimde arar.</summary>
        public List<QcnMatch> FindImei(string imei)
        {
            var digits = ModemService.OnlyDigits(imei);

            if (digits.Length != 15)
            {
                return new List<QcnMatch>();
            }

            var results = FindBytes(Encoding.ASCII.GetBytes(digits), "IMEI (ASCII)");
            results.AddRange(FindBytes(Hex.ImeiToBcd(digits), "IMEI (BCD)"));

            results.Sort((a, b) => a.Offset.CompareTo(b.Offset));
            return results;
        }

        public List<QcnMatch> FindBytes(byte[] needle, string kind)
        {
            var results = new List<QcnMatch>();

            if (needle == null || needle.Length == 0 || Data == null || Data.Length < needle.Length)
            {
                return results;
            }

            var limit = Data.Length - needle.Length;

            for (var offset = 0; offset <= limit; offset++)
            {
                var found = true;

                for (var i = 0; i < needle.Length; i++)
                {
                    if (Data[offset + i] != needle[i])
                    {
                        found = false;
                        break;
                    }
                }

                if (!found)
                {
                    continue;
                }

                results.Add(new QcnMatch
                {
                    Offset = offset,
                    Length = needle.Length,
                    Kind = kind,
                    Preview = Hex.Ascii(Data, Math.Max(0, offset - 4), Math.Min(24, Data.Length - Math.Max(0, offset - 4)))
                });

                if (results.Count >= 250)
                {
                    break;
                }
            }

            return results;
        }

        /// <summary>Eşleşmeyi aynı uzunlukta yeni IMEI ile değiştirir.</summary>
        public bool ReplaceImei(QcnMatch match, string newImei, out string error)
        {
            error = null;
            var digits = ModemService.OnlyDigits(newImei);

            if (digits.Length != 15)
            {
                error = "IMEI 15 hane olmalı.";
                return false;
            }

            var replacement = match.Kind.IndexOf("BCD", StringComparison.OrdinalIgnoreCase) >= 0
                ? Hex.ImeiToBcd(digits)
                : Encoding.ASCII.GetBytes(digits);

            if (replacement.Length != match.Length)
            {
                error = "Eşleşme uzunluğu yeni değerle uyuşmuyor (" + match.Length + " ≠ " + replacement.Length + ").";
                return false;
            }

            return Patch(match.Offset, replacement, out error);
        }

        public bool Patch(int offset, byte[] replacement, out string error)
        {
            error = null;

            if (replacement == null || replacement.Length == 0)
            {
                error = "Yazılacak veri boş.";
                return false;
            }

            if (offset < 0 || offset + replacement.Length > Data.Length)
            {
                error = "Adres dosya sınırlarının dışında.";
                return false;
            }

            Buffer.BlockCopy(replacement, 0, Data, offset, replacement.Length);
            IsDirty = true;
            return true;
        }

        /// <summary>Yedek alarak diske yazar (kopya + atomik taşıma).</summary>
        public string Save()
        {
            if (!IsDirty)
            {
                return null;
            }

            var backupPath = QcnStore.CreateBackupPath(FilePath);
            File.Copy(FilePath, backupPath, true);

            var tempPath = FilePath + ".kctmp";
            File.WriteAllBytes(tempPath, Data);
            File.Move(tempPath, FilePath, true);

            IsDirty = false;
            Sha256 = ComputeSha256(Data);

            LogHub.Instance.Ok(
                "QCN kaydedildi · yedek: " + Path.GetFileName(backupPath), "QCN");

            return backupPath;
        }

        public string Export(string targetPath)
        {
            File.WriteAllBytes(targetPath, Data);
            LogHub.Instance.Ok("QCN kopyası yazıldı: " + targetPath, "QCN");
            return targetPath;
        }

        private void Analyze()
        {
            _hints.Add("Boyut: " + Size.ToString("N0") + " bayt");
            _hints.Add("SHA-256: " + (Sha256.Length > 16 ? Sha256.Substring(0, 16) + "…" : Sha256));

            if (Data.Length >= 8)
            {
                _hints.Add("İlk baytlar: " + Hex.ToHex(Data, 0, 8));
            }

            if (Data.Length > 0 && Data[0] == 0x4B)
            {
                _hints.Add("0x4B başlığı: QPST/QFIL QCN konteyneri olabilir");
            }

            if (Data.Length >= 3 && Data[0] == 'Q' && Data[1] == 'C' && Data[2] == 'N')
            {
                _hints.Add("'QCN' imzası bulundu");
            }

            var candidate = FindAsciiImeis();

            if (candidate > 0)
            {
                _hints.Add("IMEI benzeri " + candidate + " ASCII dizi bulundu");
            }

            if (IsDirty == false)
            {
                _hints.Add("Düzenleme modu: bayt arama/değiştirme (yedekli)");
            }
        }

        private int FindAsciiImeis()
        {
            var count = 0;
            var run = 0;
            var scanLimit = Math.Min(Data.Length, 2 * 1024 * 1024);

            for (var i = 0; i < scanLimit; i++)
            {
                var b = Data[i];

                if (b >= (byte)'0' && b <= (byte)'9')
                {
                    run++;

                    if (run == 15)
                    {
                        count++;
                        run = 0;
                    }
                }
                else
                {
                    run = 0;
                }
            }

            return count;
        }

        private static string ComputeSha256(byte[] data)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(data);
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }
    }

    /// <summary>QCN yedeklerinin tutulduğu klasör.</summary>
    public static class QcnStore
    {
        public static string BackupFolder { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KCTools", "QcnBackups");

        public static string CreateBackupPath(string sourcePath)
        {
            var folder = ResolveWritableFolder(sourcePath);
            Directory.CreateDirectory(folder);

            var name = Path.GetFileNameWithoutExtension(sourcePath);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var candidate = Path.Combine(folder, name + "_" + stamp + ".qcn.bak");
            var suffix = 1;

            while (File.Exists(candidate))
            {
                candidate = Path.Combine(
                    folder, name + "_" + stamp + "_" + suffix + ".qcn.bak");
                suffix++;
            }

            return candidate;
        }

        /// <summary>
        /// Önce %LOCALAPPDATA%\KCTools\QcnBackups; yazılamıyorsa kaynak dosyanın
        /// yanında "QCN-Yedek" klasörü, o da olmazsa uygulama klasörü kullanılır.
        /// </summary>
        public static string ResolveWritableFolder(string sourcePath)
        {
            var candidates = new List<string>
            {
                BackupFolder,
                Path.Combine(Path.GetDirectoryName(sourcePath) ?? ".", "QCN-Yedek"),
                Path.Combine(AppContext.BaseDirectory, "QcnBackups")
            };

            foreach (var candidate in candidates)
            {
                try
                {
                    Directory.CreateDirectory(candidate);

                    var probe = Path.Combine(candidate, ".write-test");
                    File.WriteAllText(probe, "ok");
                    File.Delete(probe);

                    return candidate;
                }
                catch (Exception)
                {
                    // sıradaki konumu dene
                }
            }

            return Path.GetDirectoryName(sourcePath) ?? ".";
        }

        public static IReadOnlyList<string> ListBackups(string sourcePath)
        {
            var results = new List<string>();

            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return results;
            }

            var prefix = Path.GetFileNameWithoutExtension(sourcePath) + "_";
            var folders = new List<string>
            {
                BackupFolder,
                Path.Combine(Path.GetDirectoryName(sourcePath) ?? ".", "QCN-Yedek"),
                Path.Combine(AppContext.BaseDirectory, "QcnBackups")
            };

            foreach (var folder in folders)
            {
                try
                {
                    if (!Directory.Exists(folder))
                    {
                        continue;
                    }

                    foreach (var file in Directory.GetFiles(folder, prefix + "*.qcn.bak"))
                    {
                        if (!results.Contains(file))
                        {
                            results.Add(file);
                        }
                    }
                }
                catch (Exception)
                {
                    // sıradaki klasör
                }
            }

            results.Sort(StringComparer.OrdinalIgnoreCase);
            results.Reverse();
            return results;
        }

        /// <summary>
        /// Yedeği hedef QCN'nin üzerine yazar; önce mevcut dosyanın güvenlik
        /// kopyasını alır. Dönen değer güvenlik yedeğinin yoludur.
        /// </summary>
        public static string Restore(string backupPath, string targetPath)
        {
            if (!File.Exists(backupPath))
            {
                throw new FileNotFoundException("Yedek dosyası yok.", backupPath);
            }

            if (!File.Exists(targetPath))
            {
                throw new FileNotFoundException("Hedef QCN dosyası yok.", targetPath);
            }

            // Yedeğin içeriğini önce belleğe al: güvenlik kopyası aynı ada denk gelse
            // bile kaynak yedek bozulmaz.
            var backupData = File.ReadAllBytes(backupPath);

            var safety = CreateBackupPath(targetPath);
            File.Copy(targetPath, safety, true);
            File.WriteAllBytes(targetPath, backupData);

            LogHub.Instance.Ok(
                "Yedek geri yüklendi: " + Path.GetFileName(backupPath) +
                " · güvenlik kopyası: " + Path.GetFileName(safety), "QCN");

            return safety;
        }
    }
}
