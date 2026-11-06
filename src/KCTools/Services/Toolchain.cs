using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using KCTools.Common;

namespace KCTools.Services
{
    /// <summary>
    /// adb.exe / fastboot.exe konumlarını bulur ve sürümlerini okur.
    /// Arama sırası: KCTOOLS_TOOLS → uygulama yanındaki tools/platform-tools →
    /// ANDROID_HOME / ANDROID_SDK_ROOT → %LOCALAPPDATA%\Android\Sdk → C:\platform-tools → PATH.
    /// </summary>
    public sealed class Toolchain : ObservableObject
    {
        private static readonly Regex VersionPattern =
            new Regex(@"version\s+([0-9][\w\.\-]*)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private string _adbPath;
        private string _fastbootPath;
        private string _adbVersion = "—";
        private string _fastbootVersion = "—";
        private string _toolsFolder = "—";

        private Toolchain()
        {
            Refresh(false);
        }

        public static Toolchain Instance { get; } = new Toolchain();

        public string AdbPath
        {
            get => _adbPath;
            private set
            {
                if (Set(ref _adbPath, value))
                {
                    OnPropertyChanged(nameof(HasAdb));
                    OnPropertyChanged(nameof(AdbPathDisplay));
                }
            }
        }

        public string FastbootPath
        {
            get => _fastbootPath;
            private set
            {
                if (Set(ref _fastbootPath, value))
                {
                    OnPropertyChanged(nameof(HasFastboot));
                    OnPropertyChanged(nameof(FastbootPathDisplay));
                }
            }
        }

        public string AdbVersion
        {
            get => _adbVersion;
            private set => Set(ref _adbVersion, value);
        }

        public string FastbootVersion
        {
            get => _fastbootVersion;
            private set => Set(ref _fastbootVersion, value);
        }

        public string ToolsFolder
        {
            get => _toolsFolder;
            private set => Set(ref _toolsFolder, value);
        }

        public bool HasAdb => !string.IsNullOrWhiteSpace(AdbPath) && File.Exists(AdbPath);

        public bool HasFastboot => !string.IsNullOrWhiteSpace(FastbootPath) && File.Exists(FastbootPath);

        public bool HasAll => HasAdb && HasFastboot;

        public string AdbPathDisplay => HasAdb ? AdbPath : "bulunamadı";

        public string FastbootPathDisplay => HasFastboot ? FastbootPath : "bulunamadı";

        public string StatusText => HasAll ? "Hazır" : HasAdb || HasFastboot ? "Kısmi" : "Eksik";

        /// <summary>Diskteki araçları yeniden arar.</summary>
        public void Refresh(bool log = true)
        {
            AdbPath = Find("adb.exe");
            FastbootPath = Find("fastboot.exe");

            var folder = AdbPath != null
                ? Path.GetDirectoryName(AdbPath)
                : FastbootPath != null ? Path.GetDirectoryName(FastbootPath) : null;

            ToolsFolder = folder ?? "—";
            OnPropertyChanged(nameof(HasAll));
            OnPropertyChanged(nameof(StatusText));

            if (!log)
            {
                return;
            }

            if (HasAdb)
            {
                LogHub.Instance.Ok("adb bulundu: " + AdbPath, "ARAÇ");
            }
            else
            {
                LogHub.Instance.Warn(
                    "adb.exe bulunamadı. platform-tools klasörünü tools\\platform-tools altına kopyalayın " +
                    "veya KCTOOLS_TOOLS ortam değişkenini ayarlayın.", "ARAÇ");
            }

            if (HasFastboot)
            {
                LogHub.Instance.Ok("fastboot bulundu: " + FastbootPath, "ARAÇ");
            }
            else
            {
                LogHub.Instance.Warn("fastboot.exe bulunamadı.", "ARAÇ");
            }
        }

        /// <summary>adb version / fastboot --version çıktısını okur.</summary>
        public async Task RefreshVersionsAsync()
        {
            try
            {
                var runner = new ProcessRunner();

                if (HasAdb)
                {
                    var result = await runner.RunAsync(
                        AdbPath, new[] { "version" },
                        timeout: TimeSpan.FromSeconds(8),
                        echoCommand: false, logOutput: false, logSource: "ARAÇ");

                    AdbVersion = ParseVersion(result.CombinedOutput);
                }

                if (HasFastboot)
                {
                    var result = await runner.RunAsync(
                        FastbootPath, new[] { "--version" },
                        timeout: TimeSpan.FromSeconds(8),
                        echoCommand: false, logOutput: false, logSource: "ARAÇ");

                    FastbootVersion = ParseVersion(result.CombinedOutput);
                }

                OnPropertyChanged(nameof(StatusText));

                if (HasAdb || HasFastboot)
                {
                    LogHub.Instance.Info(
                        "Araç sürümleri — adb: " + AdbVersion + " · fastboot: " + FastbootVersion, "ARAÇ");
                }
            }
            catch (Exception ex)
            {
                LogHub.Instance.Warn("Araç sürümleri okunamadı: " + ex.Message, "ARAÇ");
            }
        }

        private static string ParseVersion(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "—";
            }

            var match = VersionPattern.Match(text);
            return match.Success ? match.Groups[1].Value : "—";
        }

        private static string Find(string executable)
        {
            foreach (var candidate in EnumerateCandidates(executable))
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                    {
                        return Path.GetFullPath(candidate);
                    }
                }
                catch (Exception)
                {
                    // geçersiz yol; sonraki adaya geç
                }
            }

            return null;
        }

        private static IEnumerable<string> EnumerateCandidates(string executable)
        {
            var overrideFolder = Environment.GetEnvironmentVariable("KCTOOLS_TOOLS");
            if (!string.IsNullOrWhiteSpace(overrideFolder))
            {
                yield return Path.Combine(overrideFolder, executable);
            }

            foreach (var root in EnumerateRoots())
            {
                yield return Path.Combine(root, "tools", "platform-tools", executable);
                yield return Path.Combine(root, "platform-tools", executable);
                yield return Path.Combine(root, "tools", executable);
                yield return Path.Combine(root, executable);
            }

            foreach (var variable in new[] { "ANDROID_HOME", "ANDROID_SDK_ROOT" })
            {
                var root = Environment.GetEnvironmentVariable(variable);
                if (!string.IsNullOrWhiteSpace(root))
                {
                    yield return Path.Combine(root, "platform-tools", executable);
                }
            }

            yield return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Android", "Sdk", "platform-tools", executable);

            yield return Path.Combine(@"C:\platform-tools", executable);

            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var folder in path.Split(';'))
            {
                if (string.IsNullOrWhiteSpace(folder))
                {
                    continue;
                }

                string candidate;
                try
                {
                    candidate = Path.Combine(folder.Trim().Trim('"'), executable);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                yield return candidate;
            }
        }

        private static IEnumerable<string> EnumerateRoots()
        {
            var list = new List<string>();
            string current;

            try
            {
                current = AppContext.BaseDirectory;
            }
            catch (Exception)
            {
                current = Environment.CurrentDirectory;
            }

            list.Add(current);

            // Geliştirme sırasında depo kökünü bulmak için birkaç üst klasöre bak.
            try
            {
                var directory = new DirectoryInfo(current);
                for (var i = 0; i < 4 && directory?.Parent != null; i++)
                {
                    directory = directory.Parent;
                    list.Add(directory.FullName);
                }
            }
            catch (Exception)
            {
                // yok sayılır
            }

            return list;
        }
    }
}
