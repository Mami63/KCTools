using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KCTools.Common;
using KCTools.Models;
using KCTools.Services;

namespace KCTools.ViewModels
{
    /// <summary>
    /// Cihaz panosu: bağlı telefondan bilgileri okur ve tek tık işlem
    /// kategorilerini (sistem, format, yedekleme, yazılım, güvenlik) sunar.
    /// </summary>
    public sealed class DeviceViewModel : PageViewModel, IDisposable
    {
        private readonly AdbClient _adb = new AdbClient();
        private readonly FastbootClient _fastboot = new FastbootClient();
        private readonly DeviceInfoService _infoService = new DeviceInfoService();
        private readonly IDisposable _subscription;

        private DeviceInfo _info;
        private string _partition = "boot";
        private string _imagePath = string.Empty;
        private bool _loading;

        private string _matchTitle = "Katalogda aranıyor...";
        private string _matchDetail = "Cihaz bağlanınca model kataloğuyla eşleştirilir.";
        private string _matchConfidence = "—";
        private bool _matchTested;

        private string _deviceSerial;

        private readonly HashSet<string> _autoUnlockTried =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public DeviceViewModel()
            : base("device", "Cihaz", "Bağlı telefonun bilgileri ve tek tık işlemleri")
        {
            Fields = new ObservableCollection<DeviceField>();
            Partitions = new ObservableCollection<string>
            {
                "boot", "recovery", "vendor_boot", "dtbo", "vbmeta", "system", "super"
            };

            Tiles.Add(new ActionTile("reboot", "Yeniden Başlat",
                "Sistemi normal olarak yeniden başlat", "Icon.Restore", "Sistem"));
            Tiles.Add(new ActionTile("recovery", "Recovery Modu",
                "Kurtarma bölümüne geç", "Icon.Shield", "Sistem"));
            Tiles.Add(new ActionTile("bootloader", "Bootloader",
                "Fastboot moduna geç", "Icon.Bolt", "Sistem"));
            Tiles.Add(new ActionTile("slot", "Aktif Slot",
                "A/B slot bilgisini oku", "Icon.Layers", "Sistem"));
            Tiles.Add(new ActionTile("poweroff", "Kapat",
                "Cihazı tamamen kapat", "Icon.Close", "Sistem"));

            Tiles.Add(new ActionTile("wipe-data", "Verileri Sil",
                "Fabrika sıfırlama (fastboot -w)", "Icon.Warning", "Format & Temizlik"));
            Tiles.Add(new ActionTile("wipe-cache", "Cache Temizle",
                "Önbellek bölümünü sil", "Icon.Folder", "Format & Temizlik"));
            Tiles.Add(new ActionTile("frp", "FRP Sıfırla",
                "fastboot erase frp (kilit açık olmalı)", "Icon.Shield", "Format & Temizlik"));

            Tiles.Add(new ActionTile("backup-sdcard", "Depolamayı Yedekle",
                "/sdcard → Masaüstü klasörü", "Icon.Download", "Yedekleme"));
            Tiles.Add(new ActionTile("logcat", "Logcat Kaydet",
                "Sistem günlüğünü dosyaya al", "Icon.Terminal", "Yedekleme"));
            Tiles.Add(new ActionTile("critical-backup", "Kritik Veri Yedeği",
                "NVRAM / persist / modem (root)", "Icon.Download", "Yedekleme"));

            Tiles.Add(new ActionTile("sideload", "ZIP Sideload",
                "Recovery'den yazılım yükle", "Icon.Download", "Yazılım"));
            Tiles.Add(new ActionTile("flash-folder", "Klasörden Flash",
                "Klasördeki .img dosyalarını bölümlere yaz", "Icon.Layers", "Yazılım"));

            Tiles.Add(new ActionTile("unlock", "Bootloader Kilidini Aç",
                "flashing unlock — veriler silinir", "Icon.Shield", "Güvenlik & Kilit"));
            Tiles.Add(new ActionTile("security", "Güvenlik Durumu",
                "Doğrulanmış önyükleme / FRP / kilit", "Icon.Warning", "Güvenlik & Kilit"));
            Tiles.Add(new ActionTile("imei", "IMEI Onarımı",
                "IMEI sekmesine geç", "Icon.Chip", "Güvenlik & Kilit"));

            Tiles.Add(new ActionTile("partitions", "Bölüm Yöneticisi",
                "by-name bölümlerini listele", "Icon.Layers", "Gelişmiş"));
            Tiles.Add(new ActionTile("network", "Ağ Onarımı",
                "Wi-Fi / mobil veriyi yeniden aç", "Icon.Plug", "Gelişmiş"));
            Tiles.Add(new ActionTile("shell", "ADB Kabuğu",
                "ADB sekmesine geç", "Icon.Terminal", "Gelişmiş"));

            ActionsView = CollectionViewSource.GetDefaultView(Tiles);
            ActionsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ActionTile.Category)));

            _subscription = EventAggregator.Instance.Subscribe<DeviceStateChangedEvent>(OnDeviceChanged);

            RefreshInfoCommand = new RelayCommand(_ => { _ = LoadInfoAsync(); }, _ => !IsBusy);
            PickImageCommand = new RelayCommand(_ => PickImage());
            FlashImageCommand = new RelayCommand(
                _ => { _ = RunAsync("flash-image"); },
                _ => !IsBusy && !string.IsNullOrWhiteSpace(ImagePath));

            CatalogCommand = new RelayCommand(_ =>
                EventAggregator.Instance.Publish(new NavigationRequestedEvent { Key = "catalog" }));

            UnlockCommand = new RelayCommand(
                async _ => await PromptUnlockAsync(), _ => !string.IsNullOrWhiteSpace(_deviceSerial));

            ClearActivitiesCommand = new RelayCommand(_ => ClearActivities());
        }

        public ObservableCollection<DeviceField> Fields { get; }

        public ObservableCollection<string> Partitions { get; }

        public ICollectionView ActionsView { get; }

        public ICommand RefreshInfoCommand { get; }

        public ICommand PickImageCommand { get; }

        public ICommand FlashImageCommand { get; }

        public ICommand CatalogCommand { get; }

        public ICommand UnlockCommand { get; }

        public ICommand ClearActivitiesCommand { get; }

        /// <summary>Telefona yapılan işlemlerin geçmişi (en yeni üstte).</summary>
        public ObservableCollection<DeviceActivity> Activities { get; } =
            new ObservableCollection<DeviceActivity>();

        public string MatchTitle
        {
            get => _matchTitle;
            private set => Set(ref _matchTitle, value);
        }

        public string MatchDetail
        {
            get => _matchDetail;
            private set => Set(ref _matchDetail, value);
        }

        public string MatchConfidence
        {
            get => _matchConfidence;
            private set => Set(ref _matchConfidence, value);
        }

        public bool MatchTested
        {
            get => _matchTested;
            private set => Set(ref _matchTested, value);
        }

        public DeviceInfo Info
        {
            get => _info;
            private set
            {
                if (Set(ref _info, value))
                {
                    OnPropertyChanged(nameof(HasInfo));
                }
            }
        }

        public bool HasInfo => _info != null;

        public string Partition
        {
            get => _partition;
            set => Set(ref _partition, value);
        }

        public string ImagePath
        {
            get => _imagePath;
            private set
            {
                if (Set(ref _imagePath, value))
                {
                    OnPropertyChanged(nameof(ImageName));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public string ImageName =>
            string.IsNullOrWhiteSpace(ImagePath) ? "İmaj seçilmedi" : Path.GetFileName(ImagePath);

        public async Task LoadInfoAsync()
        {
            if (_loading || IsBusy)
            {
                return;
            }

            _loading = true;
            StatusText = "Cihaz bilgisi okunuyor...";

            try
            {
                var info = await _infoService.ReadAsync();

                if (info == null)
                {
                    if (await TryLoadFastbootInfoAsync())
                    {
                        return;
                    }

                    StatusText = "ADB cihazı yok — USB hata ayıklamayı açın";
                    LogHub.Instance.Info("Cihaz bilgisi alınamadı: ADB cihazı yok.", "CİHAZ");
                    return;
                }

                Info = info;
                ApplyFields(info);
                ApplyCatalogMatch(info);
                StatusText = "Cihaz hazır — " + info.Model;

                _deviceSerial = info.Serial;
                Record(
                    "Cihaz bağlandı",
                    info.Model + " · Android " + info.Android + " · " + info.Serial,
                    ActivityStatus.Success);
                _ = TryAutoUnlockAsync(info.Serial);

                LogHub.Instance.Ok(
                    "Cihaz bilgisi: " + info.Model + " · Android " + info.Android +
                    " · batarya " + info.Battery + " · depolama " + info.Storage, "CİHAZ");
            }
            catch (Exception ex)
            {
                if (await TryLoadFastbootInfoAsync())
                {
                    return;
                }

                StatusText = "Bilgi alınamadı";
                LogHub.Instance.Warn("Cihaz bilgisi alınamadı: " + ex.Message, "CİHAZ");
            }
            finally
            {
                _loading = false;
            }
        }

        /// <summary>ADB yokken fastboot üzerinden temel bilgileri okur.</summary>
        private async Task<bool> TryLoadFastbootInfoAsync()
        {
            try
            {
                var serials = await _fastboot.ListSerialsAsync();

                if (serials.Count == 0)
                {
                    return false;
                }

                _fastboot.Serial = serials[0];
                var info = await _fastboot.ReadInfoAsync();
                ApplyFastbootFields(info);

                _deviceSerial = info.Serial;
                StatusText = "Fastboot modu — " + info.Product;

                Record(
                    "Fastboot cihazı",
                    info.Product + " · " + info.Serial + " · kilit: " + info.Unlocked,
                    ActivityStatus.Success);

                LogHub.Instance.Ok(
                    "Fastboot bilgisi: " + info.Product + " · " + info.Serial +
                    " · kilit " + info.Unlocked + " · slot " + info.ActiveSlot, "FASTBOOT");

                return true;
            }
            catch (Exception ex)
            {
                LogHub.Instance.Warn("Fastboot bilgisi okunamadı: " + ex.Message, "FASTBOOT");
                return false;
            }
        }

        private void ApplyFastbootFields(FastbootInfo info)
        {
            Fields.Clear();
            Fields.Add(new DeviceField("Mod", "Fastboot"));
            Fields.Add(new DeviceField("Model", info.Product));
            Fields.Add(new DeviceField("Seri No", info.Serial));
            Fields.Add(new DeviceField("Bootloader", info.Unlocked == "yes" ? "Açık" : "Kilitli"));
            Fields.Add(new DeviceField("Aktif Slot", info.ActiveSlot));
            Fields.Add(new DeviceField("Secure Boot", info.SecureBoot));
            Fields.Add(new DeviceField("Batarya", info.Battery));
        }
        public void Dispose()
        {
            _subscription?.Dispose();
        }

        // ------------------------------------------------------------ kilit açma

        private async Task PromptUnlockAsync()
        {
            if (string.IsNullOrWhiteSpace(_deviceSerial))
            {
                LogHub.Instance.Warn("Kilit açmak için bağlı ADB cihazı gerekli.", "KİLİT");
                return;
            }

            var dialog = new Views.UnlockDialog
            {
                Owner = Application.Current?.MainWindow
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            StatusText = "Telefon kilidi açılıyor...";

            var result = await DeviceUnlockService.UnlockAsync(_deviceSerial, dialog.Pin);

            if (result.InjectionBlocked)
            {
                Record("Kilit açılamadı", "ADB giriş enjeksiyonu engelli (INJECT_EVENTS)", ActivityStatus.Error);
                LogHub.Instance.Warn(result.Message, "KİLİT");
                return;
            }

            if (!result.Success)
            {
                Record("Kilit açılamadı", result.Message, ActivityStatus.Error);
                LogHub.Instance.Warn(result.Message, "KİLİT");
                return;
            }

            if (dialog.Remember)
            {
                LockPinStore.Save(_deviceSerial, dialog.Pin);
            }

            Record("Kilit açıldı", _deviceSerial + " · PIN ile", ActivityStatus.Success);
            LogHub.Instance.Ok(result.Message, "KİLİT");
            StatusText = "Telefon kilidi açıldı";
        }

        private async Task TryAutoUnlockAsync(string serial)
        {
            if (string.IsNullOrWhiteSpace(serial) || !_autoUnlockTried.Add(serial))
            {
                return;
            }

            var pin = LockPinStore.Load(serial);

            if (string.IsNullOrWhiteSpace(pin))
            {
                return;
            }

            var locked = await DeviceUnlockService.IsLockedAsync(serial);

            if (locked == false)
            {
                return;
            }

            var result = await DeviceUnlockService.UnlockAsync(serial, pin);

            if (result.Success)
            {
                Record("Otomatik kilit açıldı", _deviceSerial, ActivityStatus.Success);
                LogHub.Instance.Ok("Otomatik kilit açma başarılı.", "KİLİT");
            }
            else if (result.InjectionBlocked)
            {
                LogHub.Instance.Warn("Otomatik kilit açma engelli: " + result.Message, "KİLİT");
            }
            else
            {
                LogHub.Instance.Warn("Otomatik kilit açma: " + result.Message, "KİLİT");
            }
        }

        /// <summary>İşlem sonucunu cihaz geçmişine yazar.</summary>
        public void Record(string title, string detail, ActivityStatus status)
        {
            UpdateOnUi(() =>
            {
                Activities.Insert(0, new DeviceActivity
                {
                    Time = DateTime.Now,
                    Title = title,
                    Detail = detail,
                    Status = status
                });

                while (Activities.Count > 80)
                {
                    Activities.RemoveAt(Activities.Count - 1);
                }
            });
        }

        private void RecordAction(string actionKey, string actionTitle, bool success)
        {
            string title;

            switch (actionKey)
            {
                case "reboot": title = "Yeniden başlatıldı"; break;
                case "recovery": title = "Recovery moduna geçildi"; break;
                case "bootloader": title = "Fastboot moduna geçildi"; break;
                case "poweroff": title = "Cihaz kapatıldı"; break;
                case "slot": title = "Aktif slot okundu"; break;
                case "wipe-data": title = "Veriler silindi"; break;
                case "wipe-cache": title = "Cache temizlendi"; break;
                case "frp": title = "FRP sıfırlandı"; break;
                case "backup-sdcard": title = "Depolama yedeklendi"; break;
                case "logcat": title = "Logcat kaydedildi"; break;
                case "critical-backup": title = "Kritik veriler yedeklendi"; break;
                case "sideload": title = "ZIP sideload yapıldı"; break;
                case "flash-folder": title = "Klasörden flash yapıldı"; break;
                case "flash-image": title = "İmaj yazıldı"; break;
                case "unlock": title = "Bootloader kilidi açıldı"; break;
                case "security": title = "Güvenlik durumu okundu"; break;
                case "partitions": title = "Bölümler listelendi"; break;
                case "network": title = "Ağ onarımı yapıldı"; break;
                case "imei": title = "IMEI Onarımı açıldı"; break;
                case "shell": title = "ADB Kabuğu açıldı"; break;
                default: title = actionTitle; break;
            }

            Record(title, success ? "İşlem tamamlandı" : "İşlem başarısız",
                success ? ActivityStatus.Success : ActivityStatus.Error);
        }

        private void ClearActivities()
        {
            Activities.Clear();
        }

        protected override async Task<bool> ExecuteActionAsync(string actionKey, string actionTitle)
        {
            bool ok;

            switch (actionKey)
            {
                case "reboot": ok = await RebootAsync(null); break;
                case "recovery": ok = await RebootAsync("recovery"); break;
                case "bootloader": ok = await RebootAsync("bootloader"); break;
                case "poweroff": ok = await RebootAsync("poweroff"); break;
                case "wipe-data": ok = await WipeDataAsync(); break;
                case "wipe-cache": ok = await WipeCacheAsync(); break;
                case "backup-sdcard": ok = await BackupSdcardAsync(); break;
                case "logcat": ok = await SaveLogcatAsync(); break;
                case "sideload": ok = await SideloadAsync(); break;
                case "flash-folder": ok = await FlashFolderAsync(); break;
                case "unlock": ok = await UnlockAsync(); break;
                case "security": ok = await SecurityAsync(); break;
                case "frp": ok = await FrpResetAsync(); break;
                case "slot": ok = await ActiveSlotAsync(); break;
                case "partitions": ok = await ListPartitionsAsync(); break;
                case "network": ok = await NetworkRepairAsync(); break;
                case "critical-backup": ok = await CriticalBackupAsync(); break;
                case "flash-image": ok = await FlashImageAsync(); break;
                case "imei":
                case "shell":
                    EventAggregator.Instance.Publish(new NavigationRequestedEvent
                    {
                        Key = actionKey == "imei" ? "imei" : "adb"
                    });

                    LogHub.Instance.Info("Sekme açıldı: " + actionTitle, "CİHAZ");
                    ok = true;
                    break;
                default:
                    LogHub.Instance.Warn("Bilinmeyen cihaz işlemi: " + actionKey, "CİHAZ");
                    ok = false;
                    break;
            }

            RecordAction(actionKey, actionTitle, ok);
            return ok;
        }
        protected override string GetActionTitle(string actionKey)
        {
            switch (actionKey)
            {
                case "reboot": return "Yeniden Başlat";
                case "recovery": return "Recovery Modu";
                case "bootloader": return "Bootloader";
                case "poweroff": return "Kapat";
                case "wipe-data": return "Verileri Sil";
                case "wipe-cache": return "Cache Temizle";
                case "backup-sdcard": return "Depolama Yedeği";
                case "logcat": return "Logcat Kaydı";
                case "sideload": return "ZIP Sideload";
                case "flash-folder": return "Klasörden Flash";
                case "unlock": return "Bootloader Kilidini Aç";
                case "security": return "Güvenlik Durumu";
                case "frp": return "FRP Sıfırla";
                case "slot": return "Aktif Slot";
                case "partitions": return "Bölüm Yöneticisi";
                case "network": return "Ağ Onarımı";
                case "critical-backup": return "Kritik Veri Yedeği";
                case "flash-image": return "İmaj Flash";
                default: return "Cihaz İşlemi";
            }
        }

        protected override IReadOnlyList<string> GetSteps(string actionKey)
        {
            switch (actionKey)
            {
                case "backup-sdcard":
                    return Steps("Cihaz kontrolü...", "Dosyalar kopyalanıyor...", "Yedek tamamlandı.");

                case "wipe-data":
                case "wipe-cache":
                    return Steps("Fastboot kontrolü...", "Bölüm siliniyor...", "Tamamlandı.");

                default:
                    return Steps("Hazırlanıyor...", "Komut gönderiliyor...", "Tamamlandı.");
            }
        }

        private void OnDeviceChanged(DeviceStateChangedEvent e)
        {
            if (e == null)
            {
                return;
            }

            if (e.Connected)
            {
                _ = LoadInfoAsync();
                return;
            }

            _deviceSerial = null;
            Record("Cihaz ayrıldı", "Bağlantı kesildi", ActivityStatus.Warning);
        }

        /// <summary>Katalog eşleşmesini cihaz bilgisinden hesaplar (override öncelikli).</summary>
        private void ApplyCatalogMatch(DeviceInfo info)
        {
            var match = DeviceCatalogService.Instance.GetOverride(info.Serial)
                        ?? DeviceCatalogService.Instance.Match(
                            info.Model, info.Codename, info.Brand, null);

            var deviceName = (info.MarketName ?? string.Empty).Trim();

            if (deviceName.Length == 0 || deviceName == "—")
            {
                deviceName = null;
            }

            if (!match.IsKnown)
            {
                MatchTitle = deviceName ?? "Katalogda bulunamadı";
                MatchDetail = deviceName != null
                    ? "Cihazın kendi adı: " + deviceName + " · katalogda eşleşme yok; Katalog sekmesinden manuel seçin."
                    : "Katalog sekmesinden marka/model seçip bu cihaza atayabilirsiniz.";
                MatchConfidence = "BİLİNMİYOR";
                MatchTested = false;
                return;
            }

            MatchTitle = deviceName ?? (match.Brand.Name + " " + match.Model.Name);
            MatchDetail =
                "Katalog: " + match.FullName +
                " (" + match.Model.Code + ") · codename " +
                (string.IsNullOrWhiteSpace(match.Model.Codename) ? "—" : match.Model.Codename) +
                " · " + match.Model.Platform;
            MatchConfidence = match.ConfidenceText + " · " + match.Source;
            MatchTested = match.Model.Tested;

            LogHub.Instance.Info(
                "Katalog eşleşmesi: " + match.Display + " (" + match.ConfidenceText + ")", "KATALOG");
        }

        private void ApplyFields(DeviceInfo info)
        {
            Fields.Clear();
            Fields.Add(new DeviceField("Model", info.Model));
            Fields.Add(new DeviceField("Pazarlama adı", info.MarketName));
            Fields.Add(new DeviceField("Marka", info.Brand));
            Fields.Add(new DeviceField("Android", info.Android + " (SDK " + info.Sdk + ")"));
            Fields.Add(new DeviceField("Seri No", info.Serial));
            Fields.Add(new DeviceField("Yapı", info.Build));
            Fields.Add(new DeviceField("Önyükleme", info.Bootloader));
            Fields.Add(new DeviceField("Doğrulanmış", info.VerifiedBoot));
            Fields.Add(new DeviceField("FRP", info.Frp));
            Fields.Add(new DeviceField("Root", info.Root));
            Fields.Add(new DeviceField("Batarya", info.Battery));
            Fields.Add(new DeviceField("Depolama", info.Storage));
            Fields.Add(new DeviceField("Çözünürlük", info.Resolution));
        }

        private void PickImage()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Flash imajı seç",
                Filter = "İmaj (*.img)|*.img|Tüm dosyalar (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                ImagePath = dialog.FileName;
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

        private async Task<bool> RebootAsync(string mode)
        {
            var serial = await AdbSerialAsync();
            if (serial == null)
            {
                return false;
            }

            _adb.Serial = serial;

            if (mode == "poweroff")
            {
                var off = await _adb.RunRawAsync("shell reboot -p");

                if (off.Success)
                {
                    LogHub.Instance.Ok("Cihaz kapatılıyor: " + serial, "CİHAZ");
                    StatusText = "Cihaz kapatılıyor";
                    return true;
                }

                LogHub.Instance.Error("Kapatma başarısız: " + off.StandardError, "CİHAZ");
                return false;
            }

            var result = await _adb.RebootAsync(mode);

            if (!result.Success)
            {
                LogHub.Instance.Error(
                    "Yeniden başlatma başarısız: " + (result.StandardError ?? "çıkış " + result.ExitCode),
                    "CİHAZ");
                return false;
            }

            LogHub.Instance.Ok(
                (mode == null ? "Sistem" : mode) + " yeniden başlatılıyor: " + serial, "CİHAZ");
            StatusText = "Yeniden başlatılıyor";
            return true;
        }

        private async Task<bool> WipeDataAsync()
        {
            if (!Confirm("Verileri sil (fastboot -w) tüm kullanıcı verilerini siler.\n\nDevam edilsin mi?"))
            {
                return false;
            }

            var serial = await FastbootSerialAsync();

            if (serial == null)
            {
                LogHub.Instance.Warn("Veri silme için cihaz fastboot modunda olmalı.", "CİHAZ");
                return false;
            }

            StatusText = "Veriler siliniyor...";
            var result = await _fastboot.RunRawAsync("-w");

            if (!result.Success && result.CombinedOutput.IndexOf("OKAY", StringComparison.Ordinal) < 0)
            {
                LogHub.Instance.Error("fastboot -w başarısız: " + result.CombinedOutput, "CİHAZ");
                return false;
            }

            LogHub.Instance.Ok("Veriler silindi: " + serial, "CİHAZ");
            return true;
        }

        private async Task<bool> WipeCacheAsync()
        {
            if (!Confirm("Cache bölümü silinecek.\n\nDevam edilsin mi?"))
            {
                return false;
            }

            var serial = await FastbootSerialAsync();

            if (serial == null)
            {
                LogHub.Instance.Warn("Cache temizliği için cihaz fastboot modunda olmalı.", "CİHAZ");
                return false;
            }

            var result = await _fastboot.EraseAsync("cache");

            if (!result.Success)
            {
                LogHub.Instance.Error("Cache silinemedi: " + result.CombinedOutput, "CİHAZ");
                return false;
            }

            LogHub.Instance.Ok("Cache temizlendi: " + serial, "CİHAZ");
            return true;
        }

        private async Task<bool> BackupSdcardAsync()
        {
            var serial = await AdbSerialAsync();

            if (serial == null)
            {
                return false;
            }

            _adb.Serial = serial;

            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var folder = Path.Combine(desktop, "KCTools-Yedek", serial);
            Directory.CreateDirectory(folder);

            StatusText = "Depolama kopyalanıyor...";
            Progress = 40;

            var result = await _adb.PullAsync("/sdcard/", folder);

            if (!result.Success)
            {
                LogHub.Instance.Error(
                    "Yedekleme başarısız: " + (result.StandardError ?? "çıkış " + result.ExitCode),
                    "CİHAZ");
                return false;
            }

            Progress = 100;
            LogHub.Instance.Ok("Depolama yedeklendi: " + folder, "CİHAZ");
            StatusText = "Yedek: " + folder;
            return true;
        }

        private async Task<bool> SaveLogcatAsync()
        {
            var serial = await AdbSerialAsync();

            if (serial == null)
            {
                return false;
            }

            _adb.Serial = serial;

            var result = await _adb.LogcatDumpAsync(5000);

            if (!result.Success)
            {
                LogHub.Instance.Error("Logcat alınamadı.", "CİHAZ");
                return false;
            }

            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "KCTools-Yedek");

            Directory.CreateDirectory(folder);

            var path = Path.Combine(folder, "logcat-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
            File.WriteAllText(path, result.StandardOutput);

            LogHub.Instance.Ok("Logcat kaydedildi: " + path, "CİHAZ");
            StatusText = "Logcat: " + path;
            return true;
        }

        private async Task<bool> SideloadAsync()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Sideload ZIP seç",
                Filter = "Güncelleme paketi (*.zip)|*.zip|Tüm dosyalar (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() != true)
            {
                return false;
            }

            if (!Confirm("Sideload için cihaz recovery/sideload modunda olmalı.\n\n" +
                         Path.GetFileName(dialog.FileName) + " yüklensin mi?"))
            {
                return false;
            }

            var serial = await AdbSerialAsync();

            if (serial == null)
            {
                LogHub.Instance.Warn("Sideload için ADB cihazı gerekli (recovery modunda).", "CİHAZ");
                return false;
            }

            _adb.Serial = serial;
            StatusText = "Sideload başlatılıyor...";

            var result = await _adb.RunRawAsync("sideload \"" + dialog.FileName + "\"");

            if (!result.Success)
            {
                LogHub.Instance.Error(
                    "Sideload başarısız: " + (result.StandardError ?? result.CombinedOutput), "CİHAZ");
                return false;
            }

            LogHub.Instance.Ok("Sideload tamamlandı: " + Path.GetFileName(dialog.FileName), "CİHAZ");
            return true;
        }

        private async Task<bool> UnlockAsync()
        {
            if (!Confirm("Bootloader kilidi açılınca cihazdaki TÜM veriler silinir ve garanti etkilenebilir.\n\n" +
                         "Devam edilsin mi?"))
            {
                return false;
            }

            var serial = await FastbootSerialAsync();

            if (serial == null)
            {
                LogHub.Instance.Warn("Kilit açma için cihaz fastboot modunda olmalı.", "CİHAZ");
                return false;
            }

            var result = await _fastboot.UnlockAsync(true);

            if (!result.Success)
            {
                result = await _fastboot.UnlockAsync(false);
            }

            if (!result.Success)
            {
                LogHub.Instance.Error("Kilit açma reddedildi: " + result.CombinedOutput, "CİHAZ");
                return false;
            }

            LogHub.Instance.Ok("Kilit açma komutu gönderildi — cihaz ekranını onaylayın.", "CİHAZ");
            return true;
        }

        private async Task<bool> SecurityAsync()
        {
            var serial = await AdbSerialAsync();

            if (serial == null)
            {
                return false;
            }

            _adb.Serial = serial;

            var props = await _adb.GetPropsAsync();

            string Get(string key) => props.TryGetValue(key, out var value) ? value : "—";

            LogHub.Instance.Info("Doğrulanmış önyükleme : " + Get("ro.boot.verifiedbootstate"), "CİHAZ");
            LogHub.Instance.Info("Bootloader kilidi   : " +
                                 (Get("ro.boot.flash.locked") == "1" ? "Kilitli" : "Açık"), "CİHAZ");
            LogHub.Instance.Info("FRP bölümü          : " +
                                 (string.IsNullOrWhiteSpace(Get("ro.frp.pst")) ? "—" : Get("ro.frp.pst")),
                "CİHAZ");
            LogHub.Instance.Info("Geliştirici modu    : " + Get("ro.debuggable"), "CİHAZ");

            StatusText = "Güvenlik durumu günlüğe yazıldı";
            return true;
        }

        private async Task<bool> FrpResetAsync()
        {
            if (!Confirm("FRP bölümü silinecek (fastboot erase frp).\n\n" +
                         "Bootloader kilidi açık olmalı; aksi halde cihaz komutu reddeder.\n\nDevam edilsin mi?"))
            {
                return false;
            }

            var serial = await FastbootSerialAsync();

            if (serial == null)
            {
                LogHub.Instance.Warn("FRP sıfırlama için cihaz fastboot modunda olmalı.", "CİHAZ");
                return false;
            }

            var result = await _fastboot.EraseAsync("frp");

            if (!result.Success)
            {
                LogHub.Instance.Error("FRP silinemedi: " + result.CombinedOutput, "CİHAZ");
                return false;
            }

            LogHub.Instance.Ok("FRP bölümü silindi: " + serial, "CİHAZ");
            return true;
        }

        private async Task<bool> ActiveSlotAsync()
        {
            var serial = await AdbSerialAsync();
            var slot = (string)null;

            if (serial != null)
            {
                _adb.Serial = serial;

                var props = await _adb.GetPropsAsync();
                props.TryGetValue("ro.boot.slot_suffix", out slot);

                if (string.IsNullOrWhiteSpace(slot))
                {
                    slot = (await _adb.ShellAsync("getprop ro.boot.slot_suffix")).StandardOutput?.Trim();
                }
            }

            if (string.IsNullOrWhiteSpace(slot))
            {
                var fastbootSerial = await FastbootSerialAsync();

                if (fastbootSerial != null)
                {
                    var result = await _fastboot.RunRawAsync("getvar current-slot");
                    var match = System.Text.RegularExpressions.Regex.Match(
                        result.CombinedOutput, @"current-slot:\s*([ab])");

                    if (match.Success)
                    {
                        slot = "_" + match.Groups[1].Value;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(slot))
            {
                LogHub.Instance.Warn("Aktif slot okunamadı (A/B olmayan cihaz olabilir).", "CİHAZ");
                return false;
            }

            LogHub.Instance.Ok("Aktif slot: " + slot.TrimStart('_').ToUpperInvariant(), "CİHAZ");
            StatusText = "Aktif slot: " + slot.TrimStart('_').ToUpperInvariant();
            return true;
        }

        private async Task<bool> ListPartitionsAsync()
        {
            var serial = await AdbSerialAsync();

            if (serial == null)
            {
                return false;
            }

            _adb.Serial = serial;

            var result = await _adb.ShellAsync("ls /dev/block/by-name");

            if (!result.Success || result.StandardOutput.IndexOf("No such", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                result = await _adb.ShellAsync("ls /dev/block/bootdevice/by-name");
            }

            var names = (result.StandardOutput ?? string.Empty)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(n => n.Trim())
                .Where(n => n.Length > 0)
                .ToList();

            if (names.Count == 0)
            {
                LogHub.Instance.Warn("Bölüm listesi okunamadı (root gerekebilir).", "CİHAZ");
                return false;
            }

            LogHub.Instance.Info("Bölüm sayısı: " + names.Count, "CİHAZ");

            foreach (var name in names.Take(40))
            {
                LogHub.Instance.Output(name, "CİHAZ");
            }

            StatusText = names.Count + " bölüm listelendi";
            return true;
        }

        private async Task<bool> NetworkRepairAsync()
        {
            var serial = await AdbSerialAsync();

            if (serial == null)
            {
                return false;
            }

            _adb.Serial = serial;

            var commands = new[]
            {
                "svc wifi enable",
                "svc data enable",
                "settings put global airplane_mode_on 0",
                "settings put global wifi_on 1",
                "settings put global mobile_data 1"
            };

            foreach (var command in commands)
            {
                var result = await _adb.ShellAsync(command);

                if (!result.Success)
                {
                    LogHub.Instance.Warn("Atlandı: " + command, "CİHAZ");
                }
            }

            LogHub.Instance.Ok("Ağ ayarları yeniden etkinleştirildi.", "CİHAZ");
            StatusText = "Ağ onarımı tamamlandı";
            return true;
        }

        private async Task<bool> CriticalBackupAsync()
        {
            var serial = await AdbSerialAsync();

            if (serial == null)
            {
                return false;
            }

            _adb.Serial = serial;

            var root = await _adb.ShellAsync("which su");

            if (string.IsNullOrWhiteSpace(root.StandardOutput))
            {
                LogHub.Instance.Error(
                    "Kritik veri yedeği için root gerekli (su bulunamadı).", "CİHAZ");
                return false;
            }

            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "KCTools-Yedek", serial, "critical");

            Directory.CreateDirectory(folder);

            var partitions = new[] { "nvram", "nvdata", "persist", "modem", "modemst1", "modemst2", "fsg" };
            var ok = 0;

            foreach (var partition in partitions)
            {
                StatusText = "Kritik bölüm: " + partition;

                var dd = await _adb.RunRawAsync(
                    "shell \"su -c dd if=/dev/block/by-name/" + partition +
                    " of=/sdcard/" + partition + ".img\"");

                if (!dd.Success)
                {
                    LogHub.Instance.Warn("Bölüm atlandı: " + partition, "CİHAZ");
                    continue;
                }

                var pull = await _adb.PullAsync("/sdcard/" + partition + ".img", folder);

                if (pull.Success)
                {
                    ok++;
                    LogHub.Instance.Ok("Yedeklendi: " + partition + ".img", "CİHAZ");
                }
            }

            if (ok == 0)
            {
                LogHub.Instance.Error("Kritik bölümler yedeklenemedi.", "CİHAZ");
                return false;
            }

            LogHub.Instance.Ok(ok + " kritik bölüm yedeklendi: " + folder, "CİHAZ");
            StatusText = ok + " bölüm yedeklendi";
            return true;
        }

        private async Task<bool> FlashImageAsync()
        {
            if (string.IsNullOrWhiteSpace(ImagePath) || !File.Exists(ImagePath))
            {
                LogHub.Instance.Warn("Önce bir imaj dosyası seçin.", "CİHAZ");
                return false;
            }

            if (!Confirm("Bölüm: " + Partition + "\nİmaj: " + Path.GetFileName(ImagePath) +
                         "\n\nBu bölümün üzerine yazılacak. Devam edilsin mi?"))
            {
                return false;
            }

            var serial = await FastbootSerialAsync();

            if (serial == null)
            {
                LogHub.Instance.Warn("Flash için cihaz fastboot modunda olmalı.", "CİHAZ");
                return false;
            }

            StatusText = "Flash yazılıyor: " + Partition;

            var result = await _fastboot.FlashAsync(Partition, ImagePath);

            if (!result.Success)
            {
                LogHub.Instance.Error(
                    "Flash başarısız: " + (result.StandardError ?? result.CombinedOutput), "CİHAZ");
                return false;
            }

            LogHub.Instance.Ok(
                "Flash tamamlandı: " + Path.GetFileName(ImagePath) + " → " + Partition, "CİHAZ");
            return true;
        }

        private async Task<bool> FlashFolderAsync()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Firmware klasörü seç (boot.img, recovery.img ...)"
            };

            if (dialog.ShowDialog() != true)
            {
                return false;
            }

            var files = Directory.GetFiles(dialog.FolderName, "*.img");
            var plan = FirmwareFlashPlan.Build(files);

            if (plan.Count == 0)
            {
                LogHub.Instance.Warn(
                    "Klasörde bilinen bölüm adlarına sahip .img dosyası bulunamadı.", "CİHAZ");
                return false;
            }

            var preview = string.Join(
                "\n", plan.Select(entry => "• " + entry.Display).Take(15));

            if (!Confirm("Aşağıdaki bölümler sırayla yazılacak:\n\n" + preview +
                         "\n\nCihaz fastboot modunda olmalı. Devam edilsin mi?"))
            {
                return false;
            }

            var serial = await FastbootSerialAsync();

            if (serial == null)
            {
                LogHub.Instance.Warn("Flash için cihaz fastboot modunda olmalı.", "CİHAZ");
                return false;
            }

            var success = 0;

            for (var i = 0; i < plan.Count; i++)
            {
                var entry = plan[i];
                Progress = (i * 100.0) / plan.Count;
                StatusText = entry.Partition + " yazılıyor (" + (i + 1) + "/" + plan.Count + ")...";

                var result = await _fastboot.FlashAsync(
                    entry.Partition, Path.Combine(dialog.FolderName, entry.FileName));

                if (result.Success)
                {
                    success++;
                    LogHub.Instance.Ok("Flash: " + entry.Display, "CİHAZ");
                }
                else
                {
                    LogHub.Instance.Error(
                        "Flash hatası (" + entry.Partition + "): " + result.CombinedOutput, "CİHAZ");
                }
            }

            Progress = 100;
            StatusText = success + "/" + plan.Count + " bölüm yazıldı";
            return success == plan.Count;
        }

        private async Task<string> AdbSerialAsync()
        {
            var devices = await _adb.ListDevicesAsync();
            var online = devices.FirstOrDefault(d => d.IsOnline);

            if (online == null)
            {
                LogHub.Instance.Warn("ADB cihazı bulunamadı. USB hata ayıklama açık mı?", "CİHAZ");
                return null;
            }

            return online.Serial;
        }

        private async Task<string> FastbootSerialAsync()
        {
            var serials = await _fastboot.ListSerialsAsync();

            if (serials.Count == 0)
            {
                return null;
            }

            _fastboot.Serial = serials[0];
            return serials[0];
        }
    }
}
