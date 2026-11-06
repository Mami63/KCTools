using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using KCTools.Common;
using KCTools.Models;
using KCTools.Services;

namespace KCTools.ViewModels
{
    /// <summary>
    /// Root yardımcısı: resmi bootloader kilidi, tek tıkla Magisk kurulumu,
    /// yamalı boot.img flash, root kaldırma ve doğrulama. Exploit yoktur.
    /// </summary>
    public sealed class RootViewModel : PageViewModel, IDisposable
    {
        private readonly AdbClient _adb = new AdbClient();
        private readonly FastbootClient _fastboot = new FastbootClient();
        private readonly IDisposable _subscription;

        private string _model = "—";
        private string _serial = "—";
        private string _bootloader = "—";
        private string _rootText = "—";
        private string _magiskText = "—";
        private string _verified = "—";
        private string _stockImagePath = string.Empty;

        public RootViewModel()
            : base("root", "Root", "Tek tık Magisk, yamalı boot flash ve root kaldırma")
        {
            RefreshCommand = new RelayCommand(_ => { _ = RefreshAsync(); });
            UnlockCommand = new RelayCommand(_ => { _ = RunAsync("unlock"); }, _ => !IsBusy);
            InstallMagiskCommand = new RelayCommand(_ => { _ = RunAsync("magisk"); }, _ => !IsBusy);
            UnrootCommand = new RelayCommand(_ => { _ = RunAsync("unroot"); }, _ => !IsBusy);
            RemoveRootCommand = new RelayCommand(_ => { _ = RunAsync("remove-root"); }, _ => !IsBusy);
            RestoreBootCommand = new RelayCommand(
                _ => { _ = RunAsync("restore"); }, _ => !IsBusy && !string.IsNullOrWhiteSpace(StockImagePath));
            PickStockCommand = new RelayCommand(_ => PickStock());
            ExtractStockCommand = new RelayCommand(_ => { _ = ExtractStockAsync(); });
            OpenMagiskCommand = new RelayCommand(_ => { _ = OpenMagiskAsync(); });
            VerifyCommand = new RelayCommand(_ => { _ = RefreshAsync(); });

            _subscription = EventAggregator.Instance.Subscribe<DeviceStateChangedEvent>(
                e =>
                {
                    if (e != null && e.Connected)
                    {
                        _ = RefreshAsync();
                    }
                });

            _ = RefreshAsync();
        }

        public ICommand RefreshCommand { get; }

        public ICommand UnlockCommand { get; }

        public ICommand InstallMagiskCommand { get; }

        public ICommand UnrootCommand { get; }

        public ICommand RemoveRootCommand { get; }

        public ICommand RestoreBootCommand { get; }

        public ICommand PickStockCommand { get; }

        public ICommand ExtractStockCommand { get; }

        public ICommand OpenMagiskCommand { get; }

        public ICommand VerifyCommand { get; }

        public string ModelText
        {
            get => _model;
            private set => Set(ref _model, value);
        }

        public string SerialText
        {
            get => _serial;
            private set => Set(ref _serial, value);
        }

        public string BootloaderText
        {
            get => _bootloader;
            private set => Set(ref _bootloader, value);
        }

        public string RootText
        {
            get => _rootText;
            private set => Set(ref _rootText, value);
        }

        public string MagiskText
        {
            get => _magiskText;
            private set => Set(ref _magiskText, value);
        }

        public string VerifiedText
        {
            get => _verified;
            private set => Set(ref _verified, value);
        }

        public string StockImagePath
        {
            get => _stockImagePath;
            private set
            {
                if (Set(ref _stockImagePath, value))
                {
                    OnPropertyChanged(nameof(StockImageName));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public string StockImageName =>
            string.IsNullOrWhiteSpace(StockImagePath)
                ? "stok boot.img seçilmedi"
                : Path.GetFileName(StockImagePath);

        public void Dispose()
        {
            _subscription?.Dispose();
        }

        public override void OnDeviceAttached(DetectedDevice device)
        {
            _ = RefreshAsync();
        }

        protected override async Task<bool> ExecuteActionAsync(string actionKey, string actionTitle)
        {
            switch (actionKey)
            {
                case "unlock":
                    return await UnlockAsync();

                case "magisk":
                    return await InstallMagiskAsync();

                case "unroot":
                    return await UnrootAsync();

                case "restore":
                    return await RestoreBootAsync();

                case "remove-root":
                    return await RemoveRootAsync();

                default:
                    return false;
            }
        }

        protected override string GetActionTitle(string actionKey)
        {
            switch (actionKey)
            {
                case "unlock": return "Bootloader Kilidi Aç";
                case "magisk": return "Magisk Kur";
                case "unroot": return "Root Kaldır";
                case "restore": return "Stok Boot Flash";
                case "remove-root": return "Root'u Kaldır";
                default: return "Root";
            }
        }

        protected override IReadOnlyList<string> GetSteps(string actionKey)
        {
            switch (actionKey)
            {
                case "magisk":
                    return Steps("Sürüm kontrolü...", "APK indiriliyor...", "Telefona kuruluyor...");

                case "unroot":
                    return Steps("Magisk kaldırılıyor...", "Doğrulanıyor...");

                default:
                    return Steps("Hazırlanıyor...", "Tamamlandı.");
            }
        }

        private async Task RefreshAsync()
        {
            StatusText = "Cihaz durumu okunuyor...";

            try
            {
                var devices = await _adb.ListDevicesAsync();
                var online = devices.FirstOrDefault(d => d.IsOnline);

                if (online == null)
                {
                    ModelText = "—";
                    SerialText = "—";
                    BootloaderText = "ADB cihazı yok";
                    RootText = "—";
                    MagiskText = "—";
                    VerifiedText = "—";
                    StatusText = "ADB cihazı yok — USB hata ayıklamayı açın";
                    return;
                }

                _adb.Serial = online.Serial;

                var props = await _adb.GetPropsAsync();
                var su = await _adb.ShellAsync("which su");
                var packages = await _adb.ShellAsync("pm list packages");

                var status = RootHelper.Evaluate(props, su?.StandardOutput, packages?.StandardOutput);

                ModelText = props.TryGetValue("ro.product.model", out var model) ? model : "—";
                SerialText = online.Serial;
                BootloaderText = status.BootloaderUnlocked ? "Açık" : "Kilitli";
                RootText = status.Rooted ? "Var (su)" : "Yok";
                MagiskText = status.MagiskInstalled ? "Kurulu" : "Kurulu değil";
                VerifiedText = status.VerifiedBoot;

                StatusText = status.Summary;
                LogHub.Instance.Info("Root durumu: " + status.Summary, "ROOT");
            }
            catch (Exception ex)
            {
                StatusText = "Durum okunamadı";
                LogHub.Instance.Warn("Root durumu okunamadı: " + ex.Message, "ROOT");
            }
        }

        private async Task<bool> UnlockAsync()
        {
            if (!Confirm(
                    "Bootloader kilidini açmak cihazdaki TÜM verileri siler, garantiyi etkiler " +
                    "ve bazı uygulamalar (bankacılık) çalışmayabilir.\n\n" +
                    "Sahibi olduğunuz cihazda devam edilsin mi?"))
            {
                return false;
            }

            var serials = await _fastboot.ListSerialsAsync();

            if (serials.Count == 0)
            {
                LogHub.Instance.Warn("Kilit açma için cihaz fastboot modunda olmalı.", "ROOT");
                return false;
            }

            _fastboot.Serial = serials[0];
            var result = await _fastboot.UnlockAsync(true);

            if (!result.Success)
            {
                result = await _fastboot.UnlockAsync(false);
            }

            if (!result.Success)
            {
                LogHub.Instance.Error("Kilit açma reddedildi: " + result.CombinedOutput, "ROOT");
                return false;
            }

            LogHub.Instance.Ok("Kilit açma komutu gönderildi — cihaz ekranını onaylayın.", "ROOT");
            return true;
        }

        /// <summary>Magisk APK'sını resmi sürümden indirip telefona kurar (tek tık).</summary>
        private async Task<bool> InstallMagiskAsync()
        {
            var serial = await AdbSerialAsync();

            if (serial == null)
            {
                return false;
            }

            StatusText = "Magisk sürümü kontrol ediliyor...";
            Progress = 15;

            var release = await MagiskInstaller.GetLatestAsync();

            if (release == null)
            {
                LogHub.Instance.Error(
                    "Magisk sürümü alınamadı — internet/GitHub erişimini kontrol edin.", "ROOT");
                StatusText = "Magisk sürümü alınamadı";
                return false;
            }

            LogHub.Instance.Info(
                "Magisk " + release.Tag + " indiriliyor (" +
                (release.Size / 1024 / 1024) + " MB) ...", "ROOT");

            Progress = 35;
            StatusText = "Magisk " + release.Tag + " indiriliyor...";

            var apkPath = await MagiskInstaller.DownloadAsync(release);

            if (string.IsNullOrWhiteSpace(apkPath) || !File.Exists(apkPath))
            {
                LogHub.Instance.Error("Magisk APK indirilemedi.", "ROOT");
                StatusText = "İndirme başarısız";
                return false;
            }

            Progress = 70;
            StatusText = "Magisk telefona kuruluyor...";

            _adb.Serial = serial;
            var result = await _adb.InstallAsync(apkPath);

            if (!result.Success)
            {
                LogHub.Instance.Error("Magisk kurulamadı: " + result.CombinedOutput, "ROOT");
                StatusText = "Kurulum başarısız";
                return false;
            }

            Progress = 100;
            await OpenMagiskAsync();
            await RefreshAsync();

            LogHub.Instance.Ok(
                "Magisk " + release.Tag + " kuruldu. Uygulamada boot.img yamalayıp yükleyin.", "ROOT");
            StatusText = "Magisk kuruldu · " + release.Tag;
            return true;
        }

        /// <summary>Magisk uygulamasını kaldırır; tam unroot için stok boot flash gerekir.</summary>
        private async Task<bool> UnrootAsync()
        {
            var serial = await AdbSerialAsync();

            if (serial == null)
            {
                return false;
            }

            if (!Confirm(
                    "Magisk uygulaması kaldırılacak. Telefonda root izni kalmaz ancak " +
                    "yamalı boot.img duruyorsa tam temizlik için STOK boot.img flashlanmalıdır.\n\n" +
                    "Devam edilsin mi?"))
            {
                return false;
            }

            _adb.Serial = serial;
            StatusText = "Magisk kaldırılıyor...";

            var result = await _adb.UninstallAsync(RootHelper.MagiskPackage);

            if (!result.Success)
            {
                LogHub.Instance.Warn(
                    "Magisk kaldırılamadı (kurulu olmayabilir): " + result.CombinedOutput, "ROOT");
            }

            await _adb.ShellAsync("pm uninstall " + RootHelper.MagiskPackage);
            await RefreshAsync();

            LogHub.Instance.Ok(
                "Magisk kaldırıldı. Tam root temizliği için 'Stok boot.img' seçip " +
                "'Stok Boot Flashla' adımını uygulayın.", "ROOT");

            StatusText = "Magisk kaldırıldı — stok boot flashlayın";
            return true;
        }

        /// <summary>Tek tuş: Magisk'i kaldırır ve stok boot.img'yi yazar (tam unroot).</summary>
        private async Task<bool> RemoveRootAsync()
        {
            if (string.IsNullOrWhiteSpace(StockImagePath) || !File.Exists(StockImagePath))
            {
                LogHub.Instance.Warn(
                    "Önce stok boot.img seçin veya firmware ZIP'inden 'ZIP'ten Çıkar' ile üretin.", "ROOT");
                StatusText = "Stok boot yok — önce seçin/çıkarın";
                return false;
            }

            if (!Confirm(
                    "Root tamamen kaldırılacak:\n\n" +
                    "1) Magisk uygulaması silinecek\n" +
                    "2) Stok boot.img yazılacak\n\n" +
                    "Cihaz fastboot modunda olmalı. Devam edilsin mi?"))
            {
                return false;
            }

            var adbSerial = await AdbSerialAsync();

            if (adbSerial != null)
            {
                _adb.Serial = adbSerial;
                await _adb.UninstallAsync(RootHelper.MagiskPackage);
                await _adb.ShellAsync("pm uninstall " + RootHelper.MagiskPackage);
                LogHub.Instance.Info("Magisk kaldırıldı.", "ROOT");
            }

            var serials = await _fastboot.ListSerialsAsync();

            if (serials.Count == 0)
            {
                LogHub.Instance.Warn(
                    "Stok boot yazmak için cihaz fastboot modunda olmalı. Magisk kaldırıldı, " +
                    "boot yazılmadı.", "ROOT");
                StatusText = "Magisk kaldırıldı — boot yazılmadı";
                return false;
            }

            StatusText = "Stok boot yazılıyor...";
            _fastboot.Serial = serials[0];

            var flash = await _fastboot.FlashAsync("boot", StockImagePath);

            if (!flash.Success)
            {
                LogHub.Instance.Error("Stok boot flash başarısız: " + flash.CombinedOutput, "ROOT");
                StatusText = "Stok boot yazılamadı";
                return false;
            }

            await RefreshAsync();
            LogHub.Instance.Ok("Root kaldırıldı: Magisk silindi + stok boot yazıldı.", "ROOT");
            StatusText = "Root kaldırıldı · stok boot yazıldı";
            return true;
        }

        private async Task<bool> RestoreBootAsync()
        {
            if (string.IsNullOrWhiteSpace(StockImagePath) || !File.Exists(StockImagePath))
            {
                LogHub.Instance.Warn("Önce cihaza ait stok boot.img dosyasını seçin.", "ROOT");
                return false;
            }

            if (!Confirm(
                    "Stok boot.img yazılacak; root izni ve Magisk yamaları kalkar.\n" +
                    "Dosya bu cihaza ait olmalı. Devam edilsin mi?"))
            {
                return false;
            }

            var serials = await _fastboot.ListSerialsAsync();

            if (serials.Count == 0)
            {
                LogHub.Instance.Warn("Stok boot flash için cihaz fastboot modunda olmalı.", "ROOT");
                return false;
            }

            _fastboot.Serial = serials[0];
            StatusText = "Stok boot yazılıyor...";

            var result = await _fastboot.FlashAsync("boot", StockImagePath);

            if (!result.Success)
            {
                LogHub.Instance.Error("Stok boot flash başarısız: " + result.CombinedOutput, "ROOT");
                return false;
            }

            await RefreshAsync();
            LogHub.Instance.Ok("Stok boot yazıldı; cihaz root'suz açılmalı.", "ROOT");
            StatusText = "Stok boot yazıldı";
            return true;
        }

        private async Task OpenMagiskAsync()
        {
            var serial = await AdbSerialAsync();

            if (serial == null)
            {
                return;
            }

            _adb.Serial = serial;
            var result = await _adb.ShellAsync(
                "monkey -p " + RootHelper.MagiskPackage + " -c android.intent.category.LAUNCHER 1");

            if (result.Success)
            {
                LogHub.Instance.Info("Magisk açıldı.", "ROOT");
            }
        }

        private async Task<string> AdbSerialAsync()
        {
            var devices = await _adb.ListDevicesAsync();
            var online = devices.FirstOrDefault(d => d.IsOnline);

            if (online == null)
            {
                LogHub.Instance.Warn("ADB cihazı bulunamadı.", "ROOT");
                return null;
            }

            return online.Serial;
        }

        private async Task ExtractStockAsync()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Resmi firmware ZIP seç (fastboot ROM)",
                Filter = "Firmware ZIP (*.zip)|*.zip|Tüm dosyalar (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            StatusText = "Firmware ZIP'ten stok boot çıkarılıyor...";

            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "KCTools-Yedek", "stock-boot");

            var path = await Task.Run(
                () => FirmwareBootExtractor.ExtractBootFromZip(dialog.FileName, folder));

            if (string.IsNullOrWhiteSpace(path))
            {
                StatusText = "ZIP içinde boot.img bulunamadı";
                LogHub.Instance.Warn(
                    "ZIP içinde boot.img yok. Fastboot ROM (images/boot.img) kullanın.", "ROOT");
                return;
            }

            StockImagePath = path;
            StatusText = "Stok boot hazır: " + Path.GetFileName(path);
        }

        private void PickStock()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Stok boot.img seç",
                Filter = "İmaj (*.img)|*.img|Tüm dosyalar (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                StockImagePath = dialog.FileName;
            }
        }

        private static bool Confirm(string message)
        {
            var owner = Application.Current?.MainWindow;

            var result = owner == null
                ? MessageBox.Show(message, "Onay", MessageBoxButton.YesNo, MessageBoxImage.Warning)
                : MessageBox.Show(owner, message, "Onay", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            return result == MessageBoxResult.Yes;
        }
    }
}
