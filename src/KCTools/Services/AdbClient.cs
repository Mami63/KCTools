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
    /// adb.exe sarmalayıcısı: cihaz listesi, kabuk komutları, getprop, apk kurulumu,
    /// dosya aktarımı, ekran görüntüsü, logcat dökümü ve yeniden başlatma.
    /// Tüm çıktı <see cref="ProcessRunner"/> üzerinden canlı günlüğe yazılır.
    /// </summary>
    public sealed class AdbClient
    {
        private static readonly Regex PropPattern =
            new Regex(@"^\[([^\]]+)\]:\s*\[(.*)\]$", RegexOptions.Compiled);

        private static bool _missingToolLogged;

        private readonly ProcessRunner _runner = new ProcessRunner();

        /// <summary>Birden fazla cihaz varsa -s ile seçim yapılır.</summary>
        public string Serial { get; set; }

        public bool HasTool => Toolchain.Instance.HasAdb;

        public async Task<IReadOnlyList<AdbDevice>> ListDevicesAsync(CancellationToken ct = default)
        {
            var result = await RunAsync(
                new[] { "devices", "-l" }, ct, TimeSpan.FromSeconds(20), logOutput: false).ConfigureAwait(false);

            return ParseDevices(result.StandardOutput);
        }

        public static List<AdbDevice> ParseDevices(string output)
        {
            var devices = new List<AdbDevice>();

            foreach (var raw in (output ?? string.Empty).Split('\n'))
            {
                var line = raw.Trim().TrimEnd('\r');

                if (line.Length == 0 ||
                    line.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("*"))
                {
                    continue;
                }

                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                {
                    continue;
                }

                var device = new AdbDevice(parts[0], null, parts[1]);

                for (var i = 2; i < parts.Length; i++)
                {
                    var pair = parts[i].Split(new[] { ':' }, 2);
                    if (pair.Length != 2)
                    {
                        continue;
                    }

                    switch (pair[0].ToLowerInvariant())
                    {
                        case "model":
                            device.Model = pair[1].Replace('_', ' ');
                            break;
                        case "product":
                            device.Product = pair[1];
                            break;
                        case "device":
                            device.DeviceName = pair[1];
                            break;
                        case "transport_id":
                            device.TransportId = pair[1];
                            break;
                    }
                }

                devices.Add(device);
            }

            return devices;
        }

        public Task<ProcessResult> StartServerAsync(CancellationToken ct = default)
        {
            return RunAsync(new[] { "start-server" }, ct, TimeSpan.FromSeconds(20), logOutput: false);
        }

        public Task<ProcessResult> KillServerAsync(CancellationToken ct = default)
        {
            return RunAsync(new[] { "kill-server" }, ct, TimeSpan.FromSeconds(15), logOutput: true);
        }

        public Task<ProcessResult> DevicesRawAsync(CancellationToken ct = default)
        {
            return RunAsync(new[] { "devices", "-l" }, ct, TimeSpan.FromSeconds(20), logOutput: true);
        }

        public Task<ProcessResult> ShellAsync(
            string command,
            CancellationToken ct = default,
            TimeSpan? timeout = null,
            bool logOutput = true)
        {
            var parts = CommandLine.Split(command);
            if (parts.Count == 0)
            {
                return Task.FromResult(Fail("Kabuk komutu boş."));
            }

            var args = new List<string> { "shell" };
            args.AddRange(parts);

            return RunAsync(args, ct, timeout ?? TimeSpan.FromSeconds(60), logOutput: logOutput);
        }

        /// <summary>adb shell getprop çıktısını sözlüğe çevirir.</summary>
        public async Task<Dictionary<string, string>> GetPropsAsync(CancellationToken ct = default)
        {
            var result = await ShellAsync("getprop", ct, TimeSpan.FromSeconds(30), logOutput: false)
                .ConfigureAwait(false);

            return ParseProps(result.StandardOutput);
        }

        public static Dictionary<string, string> ParseProps(string output)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var raw in (output ?? string.Empty).Split('\n'))
            {
                var match = PropPattern.Match(raw.Trim().TrimEnd('\r'));
                if (match.Success)
                {
                    values[match.Groups[1].Value] = match.Groups[2].Value;
                }
            }

            return values;
        }

        public async Task<ProcessResult> InstallAsync(
            string apkPath,
            bool reinstall = true,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(apkPath) || !File.Exists(apkPath))
            {
                return Fail("APK dosyası bulunamadı: " + apkPath);
            }

            var args = new List<string> { "install" };
            if (reinstall)
            {
                args.Add("-r");
                args.Add("-d");
            }

            args.Add(apkPath);

            LogHub.Instance.Info("Kuruluyor: " + Path.GetFileName(apkPath), "ADB");

            var result = await RunAsync(args, ct, TimeSpan.FromMinutes(10), logOutput: true)
                .ConfigureAwait(false);

            if (result.CombinedOutput.IndexOf("Failure", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                LogHub.Instance.Error(
                    "Kurulum başarısız: " + ProcessRunner.LastLines(result.CombinedOutput, 2), "ADB");
            }

            return result;
        }

        public Task<ProcessResult> UninstallAsync(string package, CancellationToken ct = default)
        {
            return string.IsNullOrWhiteSpace(package)
                ? Task.FromResult(Fail("Paket adı boş."))
                : RunAsync(new[] { "uninstall", package }, ct, TimeSpan.FromMinutes(2));
        }

        public Task<ProcessResult> PushAsync(string localPath, string remotePath, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
            {
                return Task.FromResult(Fail("Gönderilecek dosya bulunamadı: " + localPath));
            }

            return RunAsync(
                new[] { "push", localPath, remotePath }, ct, TimeSpan.FromMinutes(10));
        }

        public Task<ProcessResult> PullAsync(string remotePath, string localPath, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(remotePath) || string.IsNullOrWhiteSpace(localPath))
            {
                return Task.FromResult(Fail("Kaynak/hedef yol boş."));
            }

            return RunAsync(
                new[] { "pull", remotePath, localPath }, ct, TimeSpan.FromMinutes(10));
        }

        public Task<ProcessResult> RebootAsync(string target = null, CancellationToken ct = default)
        {
            var args = string.IsNullOrWhiteSpace(target)
                ? new[] { "reboot" }
                : new[] { "reboot", target };

            return RunAsync(args, ct, TimeSpan.FromSeconds(60));
        }

        /// <summary>Ekran görüntüsünü cihazda alıp bilgisayara çeker.</summary>
        public async Task<ProcessResult> ScreenshotAsync(string localPath, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(localPath))
            {
                return Fail("Hedef dosya yolu boş.");
            }

            const string remote = "/sdcard/kctools-screen.png";

            var capture = await ShellAsync("screencap -p " + remote, ct, TimeSpan.FromSeconds(30))
                .ConfigureAwait(false);

            if (!capture.Success)
            {
                return capture;
            }

            var pull = await PullAsync(remote, localPath, ct).ConfigureAwait(false);
            await ShellAsync("rm -f " + remote, ct, TimeSpan.FromSeconds(15)).ConfigureAwait(false);

            return pull.Success ? pull : Fail("Ekran görüntüsü alınamadı.");
        }

        /// <summary>Son N satırlık logcat dökümü (akış değil, anlık görüntü).</summary>
        public Task<ProcessResult> LogcatDumpAsync(int lines = 200, CancellationToken ct = default)
        {
            var count = Math.Max(20, Math.Min(lines, 2000));
            return RunAsync(
                new[] { "logcat", "-d", "-t", count.ToString() },
                ct,
                TimeSpan.FromSeconds(45),
                logOutput: true);
        }

        /// <summary>Serbest komut: "devices -l", "shell dumpsys battery" ...</summary>
        public Task<ProcessResult> RunRawAsync(string commandLine, CancellationToken ct = default)
        {
            var parts = CommandLine.Split(commandLine);
            if (parts.Count == 0)
            {
                return Task.FromResult(Fail("Komut boş."));
            }

            return RunAsync(parts, ct, TimeSpan.FromMinutes(2));
        }

        private Task<ProcessResult> RunAsync(
            IEnumerable<string> args,
            CancellationToken ct,
            TimeSpan? timeout = null,
            bool logOutput = true)
        {
            if (!Toolchain.Instance.HasAdb)
            {
                if (!_missingToolLogged)
                {
                    _missingToolLogged = true;
                    LogHub.Instance.Error(
                        "adb.exe bulunamadı. platform-tools klasörünü tools\\platform-tools altına " +
                        "kopyalayın veya KCTOOLS_TOOLS değişkenini ayarlayın.", "ADB");
                }

                return Task.FromResult(new ProcessResult
                {
                    FileName = "adb.exe",
                    ExitCode = -127,
                    StandardError = "adb.exe bulunamadı."
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
                Toolchain.Instance.AdbPath,
                list,
                ct,
                timeout ?? TimeSpan.FromSeconds(60),
                echoCommand: true,
                logOutput: logOutput,
                logSource: "ADB");
        }

        private static ProcessResult Fail(string message)
        {
            LogHub.Instance.Error(message, "ADB");
            return new ProcessResult { FileName = "adb.exe", ExitCode = -2, StandardError = message };
        }
    }
}
