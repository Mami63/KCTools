using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace KCTools.Services
{
    /// <summary>
    /// Resmi firmware ZIP'inden (fastboot ROM) stok boot.img çıkarır.
    /// Cihazda root olmadan stok boot'a ulaşmanın güvenli yolu budur.
    /// </summary>
    public static class FirmwareBootExtractor
    {
        public static string ExtractBootFromZip(string zipPath, string outputFolder)
        {
            if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
            {
                return null;
            }

            try
            {
                using (var archive = ZipFile.OpenRead(zipPath))
                {
                    var entry = archive.Entries
                        .Where(item =>
                            item.FullName.EndsWith("boot.img", StringComparison.OrdinalIgnoreCase) &&
                            item.FullName.IndexOf("vendor_boot", StringComparison.OrdinalIgnoreCase) < 0 &&
                            item.FullName.IndexOf("init_boot", StringComparison.OrdinalIgnoreCase) < 0 &&
                            item.FullName.IndexOf("recovery", StringComparison.OrdinalIgnoreCase) < 0)
                        .OrderBy(item => item.FullName.Length)
                        .FirstOrDefault();

                    if (entry == null)
                    {
                        return null;
                    }

                    Directory.CreateDirectory(outputFolder);

                    var target = Path.Combine(
                        outputFolder, "stock-boot-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".img");

                    entry.ExtractToFile(target, true);

                    LogHub.Instance.Ok(
                        "Stok boot çıkarıldı: " + Path.GetFileName(target) +
                        " · " + (new FileInfo(target).Length / 1024 / 1024) + " MB", "ROOT");

                    return target;
                }
            }
            catch (Exception ex)
            {
                LogHub.Instance.Warn("Firmware ZIP okunamadı: " + ex.Message, "ROOT");
                return null;
            }
        }
    }
}
