using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KCTools.Services
{
    public sealed class FirmwareFlashEntry
    {
        public string FileName { get; set; } = string.Empty;

        public string Partition { get; set; } = string.Empty;

        public string Display => Partition + " ← " + FileName;
    }

    /// <summary>
    /// Firmware klasöründeki .img dosyalarını, bilinen fastboot bölüm adlarıyla
    /// eşleştirir. Bilinmeyen dosyalar atlanır (yanlış bölüme yazma riski yok).
    /// </summary>
    public static class FirmwareFlashPlan
    {
        private static readonly HashSet<string> KnownPartitions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "boot", "recovery", "vendor_boot", "dtbo", "vbmeta", "vbmeta_system",
                "super", "system", "product", "vendor", "odm", "modem", "abl", "xbl",
                "tz", "hyp", "keymaster", "devcfg", "dsp", "logo", "persist", "cache",
                "userdata", "misc", "radio", "init_boot"
            };

        public static IReadOnlyList<FirmwareFlashEntry> Build(IEnumerable<string> files)
        {
            var result = new List<FirmwareFlashEntry>();

            if (files == null)
            {
                return result;
            }

            foreach (var file in files)
            {
                if (string.IsNullOrWhiteSpace(file) ||
                    !string.Equals(Path.GetExtension(file), ".img", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var partition = Path.GetFileNameWithoutExtension(file).Trim().ToLowerInvariant();

                if (!KnownPartitions.Contains(partition))
                {
                    continue;
                }

                result.Add(new FirmwareFlashEntry
                {
                    FileName = Path.GetFileName(file),
                    Partition = partition
                });
            }

            return result;
        }
    }
}
