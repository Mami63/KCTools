using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using KCTools.Common;
using KCTools.Models;

namespace KCTools.Services
{
    /// <summary>
    /// fastboot.exe sarmalayıcısı: cihaz listesi, getvar, flash, erase, unlock, reboot.
    /// Çıktı satırları <see cref="ProcessRunner"/> üzerinden canlı olarak günlüğe akar.
    /// </summary>
    public sealed class FastbootClient
    {
        private static readonly Regex UnlockAbilityPattern =
            new Regex(@"unlock_ability:\s*(\d)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static bool _missingToolLogged;

        private readonly ProcessRunner _runner = new ProcessRunner();

        /// <summary>Birden fazla cihaz varsa -s ile seçim yapılır.</summary>
        public string Serial { get; set; }

        public bool HasTool => Toolchain.Instance.HasFastboot;

        public Task<ProcessResult> DevicesAsync(CancellationToken ct = default)
        {
            return RunAsync(new[] { "devices" }, ct, TimeSpan.FromSeconds(20));
        }

        public async Task<IReadOnlyList<string>> ListSerialsAsync(CancellationToken ct = default)
        {
            var result = await DevicesAsync(ct).ConfigureAwait(false);
            return ParseSerials(result.CombinedOutput);
        }

        public static List<string> ParseSerials(string output)
        {
            var serials = new List<string>();

            foreach (var raw in (output ?? string.Empty).Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length >= 2 && parts[1].StartsWith("fastboot", StringComparison.OrdinalIgnoreCase))
                {
                    serials.Add(parts[0]);
                }
                else if (parts.Length == 1 && line.Length <= 64)
                {
                    serials.Add(parts[0]);
                }
            }

            return serials;
        }

        /// <summary>getvar all + kilit durumu sorgusu.</summary>
        public async Task<FastbootInfo> ReadInfoAsync(CancellationToken ct = default)
        {
            var result = await RunAsync(
                new[] { "getvar", "all" }, ct, TimeSpan.FromSeconds(30), logOutput: false).ConfigureAwait(false);

            var values = FastbootOutput.Parse(result.CombinedOutput);

            var info = new FastbootInfo
            {
                Serial = FastbootOutput.First(values, "serialno", "serial") ?? "—",
                Product = FastbootOutput.First(values, "product", "ro.product.name") ?? "—",
                BootloaderVersion = FastbootOutput.First(values, "version-bootloader", "bootloader") ?? "—",
                ActiveSlot = FastbootOutput.First(values, "current-slot", "slot") ?? "—",
                SlotCount = FastbootOutput.First(values, "slot-count") ?? "—",
                SecureBoot = FastbootOutput.First(values, "secure", "secure-boot") ?? "—",
                Battery = FastbootOutput.First(values, "battery-voltage") ?? "—",
                Baseband = FastbootOutput.First(values, "version-baseband", "baseband") ?? "—",
                VariableCount = values.Count
            };

            var unlocked = FastbootOutput.First(values, "unlocked", "device-unlocked", "is-unlocked");
            if (unlocked == null)
            {
                info.Unlocked = "Bilinmiyor";
            }
            else
            {
                info.IsUnlocked = FastbootOutput.IsUnlocked(unlocked);
                info.Unlocked = info.IsUnlocked ? "Açık (Unlocked)" : "Kilitli (Locked)";
            }

            info.UnlockAbility = await ReadUnlockAbilityAsync(ct).ConfigureAwait(false);

            LogHub.Instance.Info(
                "getvar all → " + info.VariableCount + " değişken · ürün: " + info.Product +
                " · slot: " + info.ActiveSlot + " · kilit: " + info.Unlocked, "FASTBOOT");

            return info;
        }

        /// <summary>"fastboot flashing get_unlock_ability" → 1 ise kilit açılabilir.</summary>
        public async Task<string> ReadUnlockAbilityAsync(CancellationToken ct = default)
        {
            var result = await RunAsync(
                new[] { "flashing", "get_unlock_ability" }, ct, TimeSpan.FromSeconds(15), logOutput: false)
                .ConfigureAwait(false);

            var match = UnlockAbilityPattern.Match(result.CombinedOutput);
            return match.Success ? match.Groups[1].Value : "—";
        }

        /// <summary>fastboot flash &lt;partition&gt; &lt;image&gt; — canlı ilerleme bildirir.</summary>
        /// <summary>
        /// Fastboot üzerinden IMEI okumayı dener (üreticiye göre değişir).
        /// Çoğu kilitli cihazda bu değişken yoktur; null döner.
        /// </summary>
        public async Task<string> ReadImeiAsync(CancellationToken ct = default)
        {
            var candidates = new[]
            {
                "getvar imei",
                "getvar imei1",
                "getvar imei2",
                "oem get_imei"
            };

            foreach (var command in candidates)
            {
                var result = await RunRawAsync(command, ct).ConfigureAwait(false);
                var match = Regex.Match(result.CombinedOutput ?? string.Empty, @"\b(\d{15})\b");

                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }

            return null;
        }

        public async Task<ProcessResult> FlashAsync(
            string partition,
            string imagePath,
            Action<string, int> progress = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(partition))
            {
                return Fail("Partisyon adı boş.");
            }

            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            {
                return Fail("İmaj dosyası bulunamadı: " + imagePath);
            }

            void OnLine(string line)
            {
                if (line.IndexOf("Sending", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    progress?.Invoke("İmaj gönderiliyor...", 35);
                }
                else if (line.IndexOf("Writing", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    progress?.Invoke("Bölüm yazılıyor...", 70);
                }
                else if (line.IndexOf("Finished", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    progress?.Invoke("Doğrulanıyor...", 95);
                }
            }

            LogHub.Instance.Info(
                $"Partisyon: {partition} · imaj: {Path.GetFileName(imagePath)} " +
                $"({new FileInfo(imagePath).Length / 1024 / 1024} MB)", "FASTBOOT");

            return await RunAsync(
                new[] { "flash", partition, imagePath },
                ct,
                TimeSpan.FromMinutes(10),
                logOutput: true,
                onLine: OnLine).ConfigureAwait(false);
        }

        /// <summary>fastboot erase &lt;partition&gt; (FRP / metadata / userdata).</summary>
        public Task<ProcessResult> EraseAsync(string partition, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(partition))
            {
                return Task.FromResult(Fail("Silinecek partisyon belirtilmedi."));
            }

            LogHub.Instance.Warn("Silme işlemi: " + partition + " (geri alınamaz)", "FASTBOOT");
            return RunAsync(new[] { "erase", partition }, ct, TimeSpan.FromMinutes(2));
        }

        /// <summary>flashing unlock (yeni cihazlar) veya oem unlock (eski cihazlar).</summary>
        public Task<ProcessResult> UnlockAsync(bool useFlashingUnlock, CancellationToken ct = default)
        {
            var args = useFlashingUnlock
                ? new[] { "flashing", "unlock" }
                : new[] { "oem", "unlock" };

            LogHub.Instance.Warn(
                "Bootloader kilidi açılıyor — kullanıcı verisi silinebilir.", "FASTBOOT");

            return RunAsync(args, ct, TimeSpan.FromMinutes(2));
        }

        public Task<ProcessResult> RebootAsync(string target = null, CancellationToken ct = default)
        {
            var args = string.IsNullOrWhiteSpace(target)
                ? new[] { "reboot" }
                : new[] { "reboot", target };

            return RunAsync(args, ct, TimeSpan.FromSeconds(60));
        }

        public Task<ProcessResult> RebootBootloaderAsync(CancellationToken ct = default)
        {
            return RunAsync(new[] { "reboot-bootloader" }, ct, TimeSpan.FromSeconds(60));
        }

        /// <summary>Serbest komut: "getvar all", "oem device-info" ...</summary>
        public Task<ProcessResult> RunRawAsync(string commandLine, CancellationToken ct = default)
        {
            var parts = CommandLine.Split(commandLine);
            if (parts.Count == 0)
            {
                return Task.FromResult(Fail("Komut boş."));
            }

            return RunAsync(parts, ct, TimeSpan.FromSeconds(60));
        }

        private Task<ProcessResult> RunAsync(
            IEnumerable<string> args,
            CancellationToken ct,
            TimeSpan? timeout = null,
            bool logOutput = true,
            Action<string> onLine = null)
        {
            if (!Toolchain.Instance.HasFastboot)
            {
                if (!_missingToolLogged)
                {
                    _missingToolLogged = true;
                    LogHub.Instance.Error(
                        "fastboot.exe bulunamadı. platform-tools klasörünü tools\\platform-tools altına " +
                        "kopyalayın veya KCTOOLS_TOOLS değişkenini ayarlayın.", "FASTBOOT");
                }

                return Task.FromResult(new ProcessResult
                {
                    FileName = "fastboot.exe",
                    ExitCode = -127,
                    StandardError = "fastboot.exe bulunamadı."
                });
            }

            var list = new List<string>();

            if (!string.IsNullOrWhiteSpace(Serial))
            {
                list.Add("-s");
                list.Add(Serial);
            }

            list.AddRange(args);

            return _runner.RunAsync(
                Toolchain.Instance.FastbootPath,
                list,
                ct,
                timeout ?? TimeSpan.FromSeconds(60),
                echoCommand: true,
                logOutput: logOutput,
                logSource: "FASTBOOT",
                onLine: onLine);
        }

        private static ProcessResult Fail(string message)
        {
            LogHub.Instance.Error(message, "FASTBOOT");
            return new ProcessResult { FileName = "fastboot.exe", ExitCode = -2, StandardError = message };
        }
    }
}
