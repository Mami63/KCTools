using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using KCTools.Common;
using KCTools.Models;
using KCTools.Services;

namespace KCTools.ViewModels
{
    /// <summary>ADB modülü: gerçek adb.exe komutları (devices, getprop, install, shell).</summary>
    public sealed class AdbViewModel : PageViewModel
    {
        private readonly AdbClient _client = new AdbClient();

        private string _commandText = string.Empty;
        private string _deviceSummary = "Cihaz aranmadı";

        public AdbViewModel()
            : base("adb", "ADB", "Cihaz listesi, paket kurulumu ve kabuk komutları")
        {
            Devices = new ObservableCollection<AdbDevice>();

            InfoFields = new ObservableCollection<DeviceField>
            {
                new DeviceField("Model"),
                new DeviceField("Üretici"),
                new DeviceField("Android"),
                new DeviceField("SDK"),
                new DeviceField("Yapı numarası"),
                new DeviceField("ABI"),
                new DeviceField("Seri no")
            };

            SendCommand = new RelayCommand(_ => { _ = SendRawAsync(); }, _ => !IsBusy);
            ScanCommand = new RelayCommand(_ => { _ = RefreshDevicesAsync(); });
            InstallCommand = new RelayCommand(_ => { _ = RunAsync("install"); }, _ => !IsBusy);
            ScreenshotCommand = new RelayCommand(_ => { _ = RunAsync("screenshot"); }, _ => !IsBusy);

            Tiles.Add(new ActionTile("devices", "Cihazları Listele", "adb devices -l çıktısını al", "Icon.Terminal"));
            Tiles.Add(new ActionTile("info", "Bilgi Oku", "getprop ile model / Android sürümü", "Icon.Chip"));
            Tiles.Add(new ActionTile("install", "APK Yükle", "adb install -r -d", "Icon.Download"));
            Tiles.Add(new ActionTile("screenshot", "Ekran Görüntüsü", "screencap + pull", "Icon.Folder"));
            Tiles.Add(new ActionTile("reboot", "Yeniden Başlat", "adb reboot", "Icon.Bolt"));
            Tiles.Add(new ActionTile("logcat", "Logcat", "Son 200 satırı dök", "Icon.Layers"));
        }

        public ObservableCollection<AdbDevice> Devices { get; }

        public ObservableCollection<DeviceField> InfoFields { get; }

        public ICommand SendCommand { get; }

        public ICommand ScanCommand { get; }

        public ICommand InstallCommand { get; }

        public ICommand ScreenshotCommand { get; }

        public bool IsEmpty => Devices.Count == 0;

        public string DeviceSummary
        {
            get => _deviceSummary;
            private set => Set(ref _deviceSummary, value);
        }

        public string CommandText
        {
            get => _commandText;
            set => Set(ref _commandText, value);
        }

        public override void OnDeviceAttached(DetectedDevice device)
        {
            // PnP tarafında ADB arayüzü göründüğünde gerçek cihaz listesini tazele.
            _ = RefreshDevicesAsync();
        }

        public override void OnDeviceDetached()
        {
            Devices.Clear();
            DeviceSummary = "Cihaz yok";
            _client.Serial = null;
            OnPropertyChanged(nameof(IsEmpty));
        }

        public async Task RefreshDevicesAsync()
        {
            if (!Toolchain.Instance.HasAdb)
            {
                LogHub.Instance.Warn("adb.exe bulunamadı — cihaz listesi okunamadı.", "ADB");
                DeviceSummary = "adb bulunamadı";
                return;
            }

            StatusText = "ADB cihazları taranıyor...";

            var list = await _client.ListDevicesAsync();

            Devices.Clear();
            foreach (var device in list)
            {
                Devices.Add(device);
            }

            var online = Devices.Count(d => d.IsOnline);
            DeviceSummary = Devices.Count == 0 ? "Cihaz yok" : Devices.Count + " cihaz · " + online + " çevrimiçi";
            OnPropertyChanged(nameof(IsEmpty));

            var selected = Devices.FirstOrDefault(d => d.IsOnline) ?? Devices.FirstOrDefault();
            _client.Serial = selected?.Serial;

            if (Devices.Count == 0)
            {
                LogHub.Instance.Warn(
                    "ADB cihazı yok. USB hata ayıklama açık mı, RSA onayı verildi mi?", "ADB");
            }
            else
            {
                LogHub.Instance.Ok(
                    "ADB cihazları: " + string.Join(", ",
                        Devices.Select(d => d.Serial + " (" + d.StateLabel + ")")), "ADB");

                if (Devices.Any(d => !d.IsOnline))
                {
                    LogHub.Instance.Warn(
                        "Bazı cihazlar yetkisiz/çevrimdışı. Telefon ekranındaki RSA onayını kabul edin.",
                        "ADB");
                }
            }

            StatusText = DeviceSummary;
        }

        protected override async Task<bool> ExecuteActionAsync(string actionKey, string actionTitle)
        {
            if (!_client.HasTool)
            {
                LogHub.Instance.Warn("adb.exe bulunamadı — işlem demo modunda çalıştırılıyor.", "ADB");
                return await base.ExecuteActionAsync(actionKey, actionTitle);
            }

            switch (actionKey)
            {
                case "devices":
                    await RefreshDevicesAsync();
                    return !IsEmpty;

                case "info":
                    return await ReadInfoAsync();

                case "install":
                    return await InstallApkAsync();

                case "screenshot":
                    return await ScreenshotAsync();

                case "reboot":
                    return await RebootAsync();

                case "logcat":
                    return await LogcatAsync();

                default:
                    LogHub.Instance.Warn("Bilinmeyen ADB işlemi: " + actionKey, "ADB");
                    return false;
            }
        }

        private async Task<bool> ReadInfoAsync()
        {
            if (_client.Serial == null)
            {
                await RefreshDevicesAsync();
            }

            StatusText = "getprop okunuyor...";

            var props = await _client.GetPropsAsync();
            ApplyProps(props);

            Progress = 100;

            if (props.Count == 0)
            {
                StatusText = "Cihaz bilgisi alınamadı";
                return false;
            }

            LogHub.Instance.Ok(
                "Model: " + Get(props, "ro.product.model") +
                " · Android " + Get(props, "ro.build.version.release") +
                " (SDK " + Get(props, "ro.build.version.sdk") + ")", "ADB");

            StatusText = "Bilgi okundu";
            return true;
        }

        private async Task<bool> InstallApkAsync()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Kurulacak APK'yı seç",
                Filter = "Android paketi (*.apk)|*.apk|Tüm dosyalar (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() != true)
            {
                StatusText = "Kurulum iptal edildi";
                return false;
            }

            StatusText = "APK yükleniyor...";

            var result = await _client.InstallAsync(dialog.FileName);

            Progress = 100;

            var success = result.Success &&
                          result.CombinedOutput.IndexOf("Failure", StringComparison.OrdinalIgnoreCase) < 0;

            StatusText = success ? "Kurulum tamamlandı" : "Kurulum başarısız";
            return success;
        }

        private async Task<bool> ScreenshotAsync()
        {
            var folder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "KCTools", "Screenshots");

                Directory.CreateDirectory(folder);
            }

            var path = Path.Combine(folder, "kctools-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png");

            StatusText = "Ekran görüntüsü alınıyor...";

            var result = await _client.ScreenshotAsync(path);

            Progress = 100;

            if (!result.Success)
            {
                StatusText = "Ekran görüntüsü alınamadı";
                return false;
            }

            LogHub.Instance.Ok("Ekran görüntüsü: " + path, "ADB");
            StatusText = "Kaydedildi: " + Path.GetFileName(path);
            return true;
        }

        private async Task<bool> RebootAsync()
        {
            StatusText = "reboot gönderiliyor...";

            var result = await _client.RebootAsync();

            Progress = 100;
            StatusText = result.Success ? "Yeniden başlatıldı" : "Yeniden başlatılamadı";
            return result.Success;
        }

        private async Task<bool> LogcatAsync()
        {
            StatusText = "logcat dökümü alınıyor...";

            var result = await _client.LogcatDumpAsync(200);

            Progress = 100;

            if (!result.Success)
            {
                StatusText = "logcat alınamadı";
                return false;
            }

            LogHub.Instance.Ok("Logcat dökümü alındı (son 200 satır).", "ADB");
            StatusText = "Logcat alındı";
            return true;
        }

        private async Task SendRawAsync()
        {
            var command = (CommandText ?? string.Empty).Trim();

            if (command.Length == 0)
            {
                LogHub.Instance.Warn("Komut boş. Örnek: shell dumpsys battery", "ADB");
                return;
            }

            CommandText = string.Empty;
            StatusText = "Komut çalıştırılıyor...";

            var result = await _client.RunRawAsync(command);

            StatusText = result.Success ? "Komut tamamlandı" : "Komut başarısız (çıkış " + result.ExitCode + ")";
        }

        private void ApplyProps(Dictionary<string, string> props)
        {
            if (props == null)
            {
                return;
            }

            InfoFields[0].Value = Get(props, "ro.product.model");
            InfoFields[1].Value = Get(props, "ro.product.manufacturer");
            InfoFields[2].Value = Get(props, "ro.build.version.release");
            InfoFields[3].Value = Get(props, "ro.build.version.sdk");
            InfoFields[4].Value = Get(props, "ro.build.display.id");
            InfoFields[5].Value = Get(props, "ro.product.cpu.abi");
            InfoFields[6].Value = Get(props, "ro.serialno");
        }

        private static string Get(Dictionary<string, string> props, string key)
        {
            return props.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : "—";
        }

        protected override string GetActionTitle(string actionKey)
        {
            switch (actionKey)
            {
                case "devices": return "Cihazları Listele";
                case "info": return "Bilgi Oku";
                case "install": return "APK Yükle";
                case "screenshot": return "Ekran Görüntüsü";
                case "reboot": return "Yeniden Başlat";
                case "logcat": return "Logcat";
                default: return "ADB işlemi";
            }
        }

        protected override IReadOnlyList<string> GetSteps(string actionKey)
        {
            switch (actionKey)
            {
                case "devices":
                    return Steps("ADB sunucusu kontrol ediliyor...", "USB cihazları taranıyor...", "Liste güncellendi.");

                case "info":
                    return Steps("getprop isteniyor...", "Özellikler ayrıştırılıyor...", "Bilgi kartı dolduruldu.");

                case "install":
                    return Steps("APK doğrulanıyor...", "Paket yükleniyor (%35)...", "Paket yükleniyor (%80)...", "Kurulum tamamlandı.");

                case "screenshot":
                    return Steps("Ekran görüntüsü alınıyor...", "PNG kodlanıyor...", "Masaüstüne kaydedildi.");

                case "reboot":
                    return Steps("Hedef mod doğrulanıyor...", "reboot komutu gönderiliyor...", "Bağlantı kapatılıyor...");

                case "logcat":
                    return Steps("logcat akışı açılıyor...", "Arabellek okunuyor...", "Döküm alındı.");

                default:
                    return Steps("İşlem hazırlanıyor...", "Tamamlandı.");
            }
        }
    }
}
