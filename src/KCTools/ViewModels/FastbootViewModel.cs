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
    /// <summary>Fastboot modülü: gerçek fastboot.exe komutları (getvar, flash, erase, unlock).</summary>
    public sealed class FastbootViewModel : PageViewModel
    {
        private readonly FastbootClient _client = new FastbootClient();

        private string _bootloaderState = "Bilinmiyor";
        private string _activeSlot = "—";
        private string _serial = "—";
        private string _product = "—";
        private string _bootloaderVersion = "—";
        private string _secureBoot = "—";
        private string _unlockAbility = "—";
        private string _commandText = string.Empty;
        private string _imagePath = string.Empty;
        private string _selectedPartition = "boot";

        public FastbootViewModel()
            : base("fastboot", "Fastboot", "Bootloader kilidi, slot yönetimi ve ham komut konsolu")
        {
            Partitions = new ObservableCollection<string>
            {
                "boot", "recovery", "vbmeta", "dtbo", "system", "vendor",
                "super", "userdata", "modem", "abl", "xbl", "persist"
            };

            Tiles.Add(new ActionTile("devices", "Cihazları Listele", "fastboot devices çıktısını oku", "Icon.Terminal"));
            Tiles.Add(new ActionTile("info", "Bilgi Oku", "getvar all ile cihazı tanı", "Icon.Chip"));
            Tiles.Add(new ActionTile("lockstate", "Kilit Durumu", "unlocked + unlock_ability sorgula", "Icon.Shield"));
            Tiles.Add(new ActionTile("unlock", "Bootloader Aç", "flashing unlock / oem unlock", "Icon.Bolt"));
            Tiles.Add(new ActionTile("frp", "FRP Sıfırla", "fastboot erase frp", "Icon.Download"));
            Tiles.Add(new ActionTile("reboot", "Yeniden Başlat", "fastboot reboot", "Icon.Plug"));

            SendCommand = new RelayCommand(_ => { _ = SendRawAsync(); }, _ => !IsBusy);
            BrowseImageCommand = new RelayCommand(_ => BrowseImage());
            RefreshInfoCommand = new RelayCommand(_ => { _ = RunAsync("info"); }, _ => !IsBusy);
            FlashCommand = new RelayCommand(
                _ => { _ = RunAsync("flash"); },
                _ => !IsBusy && !string.IsNullOrWhiteSpace(ImagePath) &&
                     !string.IsNullOrWhiteSpace(SelectedPartition));
        }

        public override void OnDeviceAttached(DetectedDevice device)
        {
            if (device == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(device.Serial))
            {
                Serial = device.Serial;
            }

            _ = RunAsync("info");
        }

        public override void OnDeviceDetached()
        {
            Serial = "—";
            Product = "—";
            BootloaderState = "Bilinmiyor";
            ActiveSlot = "—";
            SecureBoot = "—";
            StatusText = "Fastboot cihazı yok";
        }

        public ObservableCollection<string> Partitions { get; }

        public ICommand SendCommand { get; }

        public ICommand BrowseImageCommand { get; }

        public ICommand FlashCommand { get; }

        public ICommand RefreshInfoCommand { get; }

        public string BootloaderState
        {
            get => _bootloaderState;
            private set
            {
                if (Set(ref _bootloaderState, value))
                {
                    OnPropertyChanged(nameof(IsUnlocked));
                }
            }
        }

        public bool IsUnlocked => BootloaderState.StartsWith("Açık", StringComparison.OrdinalIgnoreCase);

        public string ActiveSlot
        {
            get => _activeSlot;
            private set => Set(ref _activeSlot, value);
        }

        public string Serial
        {
            get => _serial;
            private set
            {
                if (!Set(ref _serial, value))
                {
                    return;
                }

                _client.Serial = value == "—" ? null : value;
                OnPropertyChanged(nameof(HasDevice));
            }
        }

        public bool HasDevice => Serial != "—" && !string.IsNullOrWhiteSpace(Serial);

        public string Product
        {
            get => _product;
            private set => Set(ref _product, value);
        }

        public string BootloaderVersion
        {
            get => _bootloaderVersion;
            private set => Set(ref _bootloaderVersion, value);
        }

        public string SecureBoot
        {
            get => _secureBoot;
            private set => Set(ref _secureBoot, value);
        }

        public string UnlockAbility
        {
            get => _unlockAbility;
            private set => Set(ref _unlockAbility, value);
        }

        public string CommandText
        {
            get => _commandText;
            set => Set(ref _commandText, value);
        }

        public string ImagePath
        {
            get => _imagePath;
            private set
            {
                if (!Set(ref _imagePath, value))
                {
                    return;
                }

                OnPropertyChanged(nameof(ImageName));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public string ImageName =>
            string.IsNullOrWhiteSpace(ImagePath) ? "İmaj seçilmedi" : Path.GetFileName(ImagePath);

        public string SelectedPartition
        {
            get => _selectedPartition;
            set
            {
                if (Set(ref _selectedPartition, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        protected override async Task<bool> ExecuteActionAsync(string actionKey, string actionTitle)
        {
            if (!_client.HasTool)
            {
                LogHub.Instance.Warn(
                    "fastboot.exe bulunamadı — işlem demo modunda çalıştırılıyor.", "FASTBOOT");
                return await base.ExecuteActionAsync(actionKey, actionTitle);
            }

            switch (actionKey)
            {
                case "devices":
                    return await ListDevicesAsync();

                case "info":
                    return await ReadInfoAsync();

                case "lockstate":
                    return await ReadLockStateAsync();

                case "unlock":
                    return await UnlockAsync();

                case "frp":
                    return await ResetFrpAsync();

                case "flash":
                    return await FlashAsync();

                case "reboot":
                    return await RebootAsync();

                default:
                    LogHub.Instance.Warn("Bilinmeyen fastboot işlemi: " + actionKey, "FASTBOOT");
                    return false;
            }
        }

        private async Task<bool> ListDevicesAsync()
        {
            StatusText = "fastboot cihazları taranıyor...";
            var serials = await _client.ListSerialsAsync();
            Progress = 60;

            if (serials.Count == 0)
            {
                LogHub.Instance.Warn(
                    "fastboot cihazı bulunamadı. Cihazın fastboot modunda ve sürücünün kurulu olduğundan emin olun.",
                    "FASTBOOT");

                StatusText = "Cihaz bulunamadı";
                return false;
            }

            Serial = serials[0];
            LogHub.Instance.Ok(
                serials.Count + " fastboot cihazı: " + string.Join(", ", serials), "FASTBOOT");

            StatusText = "Cihaz: " + serials[0];
            Progress = 100;
            return true;
        }

        private async Task<bool> ReadInfoAsync()
        {
            StatusText = "getvar all okunuyor...";

            var info = await _client.ReadInfoAsync();

            BootloaderState = info.Unlocked == "Bilinmiyor"
                ? "Bilinmiyor"
                : info.IsUnlocked ? "Açık (Unlocked)" : "Kilitli (Locked)";

            ActiveSlot = info.ActiveSlot;
            Product = info.Product;
            BootloaderVersion = info.BootloaderVersion;
            SecureBoot = info.SecureBoot;
            Serial = info.Serial;
            UnlockAbility = info.UnlockAbility == "1"
                ? "Açılabilir (1)"
                : info.UnlockAbility == "0" ? "Kapalı (0)" : "—";

            Progress = 100;

            if (info.VariableCount == 0)
            {
                StatusText = "Cihazdan bilgi alınamadı";
                return false;
            }

            StatusText = "Bilgi okundu · " + info.VariableCount + " değişken";
            return true;
        }

        private async Task<bool> ReadLockStateAsync()
        {
            StatusText = "Kilit durumu sorgulanıyor...";

            var info = await _client.ReadInfoAsync();

            BootloaderState = info.Unlocked == "Bilinmiyor"
                ? "Bilinmiyor"
                : info.IsUnlocked ? "Açık (Unlocked)" : "Kilitli (Locked)";

            UnlockAbility = info.UnlockAbility == "1" ? "Açılabilir (1)" : "Kapalı (0)";
            Progress = 100;

            LogHub.Instance.Info(
                "Kilit: " + BootloaderState + " · unlock_ability: " + info.UnlockAbility, "FASTBOOT");

            StatusText = info.Unlocked;
            return true;
        }

        private async Task<bool> UnlockAsync()
        {
            if (IsUnlocked)
            {
                LogHub.Instance.Info("Bootloader zaten açık görünüyor.", "FASTBOOT");
                StatusText = "Kilit zaten açık";
                return true;
            }

            StatusText = "unlock_ability sorgulanıyor...";
            var ability = await _client.ReadUnlockAbilityAsync();

            if (ability == "0")
            {
                LogHub.Instance.Warn(
                    "Cihaz unlock_ability=0 bildiriyor; üretici ayarlarından OEM kilidini açmanız gerekir.",
                    "FASTBOOT");

                StatusText = "OEM kilidi kapalı";
                return false;
            }

            Progress = 30;
            StatusText = "flashing unlock gönderiliyor...";

            var result = await _client.UnlockAsync(true);

            if (!result.Success)
            {
                LogHub.Instance.Warn("flashing unlock başarısız, oem unlock deneniyor...", "FASTBOOT");
                Progress = 60;
                result = await _client.UnlockAsync(false);
            }

            Progress = 100;

            if (!result.Success)
            {
                StatusText = "Kilit açılamadı";
                return false;
            }

            BootloaderState = "Açık (Unlocked)";
            LogHub.Instance.Ok("Bootloader kilidi açıldı. Cihaz yeniden başlatılıyor...", "FASTBOOT");

            await _client.RebootAsync();
            StatusText = "Kilit açıldı";
            return true;
        }

        private async Task<bool> ResetFrpAsync()
        {
            if (!IsUnlocked)
            {
                LogHub.Instance.Warn(
                    "FRP silme işlemi genellikle açık bootloader gerektirir. Sonuç başarısız olabilir.",
                    "FASTBOOT");
            }

            StatusText = "frp bölümü siliniyor...";
            var result = await _client.EraseAsync("frp");
            Progress = 100;

            if (!result.Success)
            {
                StatusText = "FRP silinemedi";
                return false;
            }

            LogHub.Instance.Ok("frp bölümü silindi. Cihazı yeniden başlatın.", "FASTBOOT");
            StatusText = "FRP temizlendi";
            return true;
        }

        private async Task<bool> FlashAsync()
        {
            if (string.IsNullOrWhiteSpace(ImagePath) || !File.Exists(ImagePath))
            {
                LogHub.Instance.Error("Geçerli bir imaj dosyası seçin.", "FASTBOOT");
                return false;
            }

            StatusText = SelectedPartition + " yazılıyor...";

            var result = await _client.FlashAsync(
                SelectedPartition,
                ImagePath,
                (message, percent) => UpdateOnUi(() =>
                {
                    StatusText = message;
                    Progress = percent;
                }));

            Progress = 100;

            if (!result.Success)
            {
                StatusText = SelectedPartition + " yazılamadı";
                return false;
            }

            LogHub.Instance.Ok(
                SelectedPartition + " yazıldı · " + Path.GetFileName(ImagePath), "FASTBOOT");

            StatusText = SelectedPartition + " yazıldı";
            return true;
        }

        private async Task<bool> RebootAsync()
        {
            StatusText = "reboot gönderiliyor...";
            var result = await _client.RebootAsync();
            Progress = 100;

            if (!result.Success)
            {
                StatusText = "Yeniden başlatılamadı";
                return false;
            }

            StatusText = "Yeniden başlatıldı";
            return true;
        }

        private async Task SendRawAsync()
        {
            var command = (CommandText ?? string.Empty).Trim();

            if (command.Length == 0)
            {
                LogHub.Instance.Warn("Komut boş. Örnek: getvar all", "FASTBOOT");
                return;
            }

            CommandText = string.Empty;
            StatusText = "Komut çalıştırılıyor...";

            var result = await _client.RunRawAsync(command);

            StatusText = result.Success ? "Komut tamamlandı" : "Komut başarısız (çıkış " + result.ExitCode + ")";
        }

        private void BrowseImage()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Fastboot imajı seç",
                Filter = "İmaj dosyası (*.img;*.bin)|*.img;*.bin|Tüm dosyalar (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                ImagePath = dialog.FileName;
                LogHub.Instance.Info("İmaj seçildi: " + dialog.FileName, "FASTBOOT");
            }
        }

        protected override string GetActionTitle(string actionKey)
        {
            switch (actionKey)
            {
                case "devices": return "Cihazları Listele";
                case "info": return "Bilgi Oku";
                case "lockstate": return "Kilit Durumu";
                case "unlock": return "Bootloader Aç";
                case "frp": return "FRP Sıfırla";
                case "flash": return "Partisyon Yaz";
                case "reboot": return "Yeniden Başlat";
                default: return "Fastboot işlemi";
            }
        }

        protected override IReadOnlyList<string> GetSteps(string actionKey)
        {
            switch (actionKey)
            {
                case "devices":
                    return Steps("fastboot sunucusu başlatılıyor...", "USB cihazları taranıyor...", "Cihaz listesi alındı (demo).");

                case "info":
                    return Steps("getvar all isteniyor...", "Değişkenler ayrıştırılıyor...", "Bilgi kartı dolduruldu (demo).");

                case "lockstate":
                    return Steps("Kilit durumu sorgulanıyor...", "unlock_ability okunuyor...", "Sonuç: bilinmiyor (demo).");

                case "unlock":
                    return Steps("Kilit durumu sorgulanıyor...", "Kullanıcı onayı bekleniyor...", "unlock komutu gönderiliyor...", "Cihaz yeniden başlatılıyor...");

                case "frp":
                    return Steps("frp bölümü tespit ediliyor...", "Silme komutu gönderiliyor...", "Doğrulama yapılıyor...");

                case "flash":
                    return Steps("İmaj doğrulanıyor...", "Gönderiliyor (%45)...", "Yazılıyor (%90)...", "Doğrulama tamamlandı.");

                case "reboot":
                    return Steps("Hedef mod doğrulanıyor...", "reboot komutu gönderiliyor...", "Bağlantı kapatılıyor...");

                default:
                    return Steps("İşlem hazırlanıyor...", "Tamamlandı.");
            }
        }
    }
}
