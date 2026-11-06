using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using KCTools.Common;
using KCTools.Models;
using KCTools.Protocol;
using KCTools.Security;
using KCTools.Services;

namespace KCTools.ViewModels
{
    /// <summary>Qualcomm (EDL / Firehose) modülü.</summary>
    public sealed class QualcommViewModel : PageViewModel
    {
        private string _firehoseAction = "NOP";
        private string _firehoseFileName = "gpt_main0.bin";
        private string _firehoseStartSector = "0";
        private string _firehoseSectorCount = "1";
        private string _firehosePhysicalPartition = "0";
        private string _firehoseImagePath = string.Empty;
        private string _firehoseXmlPreview = "Parametreleri seçip \"XML Üret\" düğmesine basın.";
        private string _firehoseDataPreview = "—";
        private string _rawProgramPath = string.Empty;
        private string _romDirectory = string.Empty;
        private string _chipset = "SM8650";
        private string _loaderPath = string.Empty;
        private string _flashSummary = "ROM klasörü seçilmedi.";

        public QualcommViewModel()
            : base("qualcomm", "Qualcomm", "EDL 9008, Firehose programlayıcı, FRP ve yedekleme iş akışları")
        {
            DeviceFields = new ObservableCollection<DeviceField>
            {
                new DeviceField("Aygıt"),
                new DeviceField("Seri numarası"),
                new DeviceField("Mod"),
                new DeviceField("Sürücü servisi"),
                new DeviceField("Port"),
                new DeviceField("Durum")
            };

            Tiles.Add(new ActionTile("edl", "EDL Moduna Al", "9008 modunda Sahara el sıkışması", "Icon.Bolt"));
            Tiles.Add(new ActionTile("unlock", "Bootloader Kilidi", "OEM kilidini doğrula ve aç", "Icon.Shield"));
            Tiles.Add(new ActionTile("frp", "FRP Sıfırlama", "Fabrika sıfırlama korumasını kaldır", "Icon.Chip"));
            Tiles.Add(new ActionTile("flash", "Firehose Flash", "Ham XML + imaj ile bölüm yaz", "Icon.Plug"));
            Tiles.Add(new ActionTile("backup", "Tam Yedek", "Bölümleri tek dosyada topla", "Icon.Download"));
            Tiles.Add(new ActionTile("info", "Bölüm Bilgisi", "GPT tablosunu ve alanları oku", "Icon.Terminal"));

            BuildFirehoseXmlCommand = new RelayCommand(_ => BuildFirehoseXml());
            SimulateFirehoseCommand = new RelayCommand(
                _ => { _ = RunAsync("firehose"); }, _ => !IsBusy);
            BrowseFirehoseImageCommand = new RelayCommand(_ => BrowseFirehoseImage());
            BrowseRawProgramCommand = new RelayCommand(_ => _ = BrowseRawProgramAsync());
            FlashRomCommand = new RelayCommand(
                _ => { _ = RunAsync("flash-rom"); }, _ => !IsBusy);
            FlashRomUsbCommand = new RelayCommand(
                _ => { _ = RunAsync("flash-rom-usb"); }, _ => !IsBusy);
            DownloadLoaderCommand = new RelayCommand(
                _ => { _ = RunAsync("loader"); }, _ => !IsBusy);
        }

        public ObservableCollection<string> FirehoseActions { get; } = new ObservableCollection<string>
        {
            "NOP", "Configure", "Read", "Write", "Erase"
        };

        public ICommand BuildFirehoseXmlCommand { get; }

        public ICommand SimulateFirehoseCommand { get; }

        public ICommand BrowseFirehoseImageCommand { get; }

        public ICommand BrowseRawProgramCommand { get; }

        public ICommand FlashRomCommand { get; }

        public ICommand FlashRomUsbCommand { get; }

        public ICommand DownloadLoaderCommand { get; }

        public string FirehoseActionText
        {
            get => _firehoseAction;
            set
            {
                if (Set(ref _firehoseAction, value))
                {
                    OnPropertyChanged(nameof(FirehoseIsWrite));
                }
            }
        }

        public bool FirehoseIsWrite =>
            string.Equals(FirehoseActionText, "Write", StringComparison.OrdinalIgnoreCase);

        public string FirehoseFileName
        {
            get => _firehoseFileName;
            set => Set(ref _firehoseFileName, value);
        }

        public string FirehoseStartSector
        {
            get => _firehoseStartSector;
            set => Set(ref _firehoseStartSector, value);
        }

        public string FirehoseSectorCount
        {
            get => _firehoseSectorCount;
            set => Set(ref _firehoseSectorCount, value);
        }

        public string FirehosePhysicalPartition
        {
            get => _firehosePhysicalPartition;
            set => Set(ref _firehosePhysicalPartition, value);
        }

        public string FirehoseImagePath
        {
            get => _firehoseImagePath;
            private set
            {
                if (Set(ref _firehoseImagePath, value))
                {
                    OnPropertyChanged(nameof(FirehoseImageName));
                }
            }
        }

        public string FirehoseImageName =>
            string.IsNullOrWhiteSpace(FirehoseImagePath)
                ? "İmaj seçilmedi"
                : Path.GetFileName(FirehoseImagePath);

        public string FirehoseXmlPreview
        {
            get => _firehoseXmlPreview;
            private set => Set(ref _firehoseXmlPreview, value);
        }

        public string FirehoseDataPreview
        {
            get => _firehoseDataPreview;
            private set => Set(ref _firehoseDataPreview, value);
        }

        public string RawProgramPath => _rawProgramPath;

        public string RomDirectory => _romDirectory;

        public string RawProgramName =>
            string.IsNullOrWhiteSpace(_rawProgramPath)
                ? "rawprogram XML seçilmedi"
                : Path.GetFileName(_rawProgramPath);

        public string Chipset
        {
            get => _chipset;
            set => Set(ref _chipset, value);
        }

        public string LoaderName =>
            string.IsNullOrWhiteSpace(_loaderPath)
                ? "Loader indirilmedi"
                : Path.GetFileName(_loaderPath);

        public string FlashSummary
        {
            get => _flashSummary;
            private set => Set(ref _flashSummary, value);
        }

        public ObservableCollection<DeviceField> DeviceFields { get; }

        /// <summary>Cihaz oturumu açıldığında kartı gerçek algılama verisiyle doldurur.</summary>
        public override void OnDeviceAttached(DetectedDevice device)
        {
            if (device == null)
            {
                return;
            }

            DeviceFields[0].Value = string.IsNullOrWhiteSpace(device.Name) ? "—" : device.Name;
            DeviceFields[1].Value = device.Serial ?? "—";
            DeviceFields[2].Value = device.ModeLabel;
            DeviceFields[3].Value = device.Service ?? "—";
            DeviceFields[4].Value = device.PortLabel;
            DeviceFields[5].Value = device.Status ?? "—";
        }

        public override void OnDeviceDetached()
        {
            ResetProfile();
        }

        public void ResetProfile()
        {
            foreach (var field in DeviceFields)
            {
                field.Value = "—";
            }
        }

        protected override async Task<bool> ExecuteActionAsync(string actionKey, string actionTitle)
        {
            if (string.Equals(actionKey, "firehose", StringComparison.OrdinalIgnoreCase))
            {
                return await RunFirehoseSimulationAsync();
            }

            if (string.Equals(actionKey, "flash-rom", StringComparison.OrdinalIgnoreCase))
            {
                return await FlashRomAsync(useWinUsb: false);
            }

            if (string.Equals(actionKey, "flash-rom-usb", StringComparison.OrdinalIgnoreCase))
            {
                return await FlashRomAsync(useWinUsb: true);
            }

            if (string.Equals(actionKey, "loader", StringComparison.OrdinalIgnoreCase))
            {
                return await DownloadLoaderAsync();
            }

            return await base.ExecuteActionAsync(actionKey, actionTitle);
        }

        private void BuildFirehoseXml()
        {
            var request = BuildFirehoseRequest();
            FirehoseXmlPreview = FirehoseXml.Build(request);

            LogHub.Instance.Info(
                "Firehose XML üretildi (" + request.Action + " · " +
                FirehoseXmlPreview.Length + " karakter)", "FIREHOSE");
        }

        private void BrowseFirehoseImage()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Firehose imajı seç",
                Filter = "İmaj (*.img;*.bin)|*.img;*.bin|Tüm dosyalar (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            FirehoseImagePath = dialog.FileName;
            FirehoseFileName = Path.GetFileName(dialog.FileName);

            LogHub.Instance.Info(
                "Firehose imajı: " + dialog.FileName + " (" +
                new FileInfo(dialog.FileName).Length.ToString("N0") + " bayt)", "FIREHOSE");
        }

        private async Task BrowseRawProgramAsync()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "rawprogram XML seç",
                Filter = "Qualcomm rawprogram (*.xml)|*.xml|Tüm dosyalar (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                var path = dialog.FileName;

                // Dosya okuma + XML ayrıştırma arka planda (büyük rawprogram dosyaları UI'yı dondurmasın).
                var entries = await Task.Run(() =>
                    RawProgramParser.ParsePrograms(File.ReadAllText(path)));

                _rawProgramPath = path;
                _romDirectory = Path.GetDirectoryName(path) ?? string.Empty;
                OnPropertyChanged(nameof(RawProgramPath));
                OnPropertyChanged(nameof(RomDirectory));
                OnPropertyChanged(nameof(RawProgramName));

                FirehoseXmlPreview = entries.Count + " program kaydı · " + Path.GetFileName(path);
                FlashSummary = entries.Count + " program kaydı · " + _romDirectory;
                LogHub.Instance.Ok(entries.Count + " program kaydı bulundu.", "FIREHOSE");

                foreach (var entry in entries.Take(5))
                {
                    LogHub.Instance.Info(
                        entry.Label + " · " + entry.Filename + " · sektör " + entry.StartSector,
                        "FIREHOSE");
                }

                StatusText = entries.Count + " program kaydı";
            }
            catch (Exception ex)
            {
                LogHub.Instance.Error("rawprogram okunamadı: " + ex.Message, "FIREHOSE");
            }
        }

        private async Task<bool> RunFirehoseSimulationAsync()
        {
            var request = BuildFirehoseRequest();
            FirehoseXmlPreview = FirehoseXml.Build(request);
            StatusText = "Loopback simülasyonu: " + request.Action;

            LogHub.Instance.Info(
                "Loopback Firehose — cihaz yok, protokol simüle ediliyor.", "FIREHOSE");

            using (var client = new FirehoseClient(new LoopbackFirehoseTransport()) { SectorSize = 512 })
            {
                await client.OpenAsync();

                switch (request.Action)
                {
                    case FirehoseAction.Read:
                    {
                        var result = await client.ReadAsync(
                            request.FileName, request.PhysicalPartition, request.StartSector, request.SectorCount);

                        if (!result.Success)
                        {
                            return false;
                        }

                        FirehoseDataPreview =
                            result.Data.Length.ToString("N0") + " bayt · " +
                            Hex.ToHex(result.Data, 0, Math.Min(64, result.Data.Length));

                        LogHub.Instance.Ok(
                            result.Data.Length + " bayt veri alındı (simülasyon).", "FIREHOSE");

                        StatusText = result.Data.Length + " bayt okundu (simülasyon)";
                        return true;
                    }

                    case FirehoseAction.Write:
                    {
                        if (!string.IsNullOrWhiteSpace(FirehoseImagePath) && File.Exists(FirehoseImagePath))
                        {
                            var result = await client.WriteAsync(
                                FirehoseImagePath,
                                request.PhysicalPartition,
                                request.StartSector,
                                percent => UpdateOnUi(() => Progress = percent));

                            if (!result.Success)
                            {
                                return false;
                            }

                            FirehoseDataPreview = "Yazma akışı tamamlandı · " + FirehoseImageName;
                            StatusText = "İmaj yazıldı (simülasyon)";
                            return true;
                        }

                        var header = await client.WriteHeaderAsync(
                            request.FileName, request.PhysicalPartition, request.StartSector);

                        if (!header.Success)
                        {
                            return false;
                        }

                        LogHub.Instance.Warn(
                            "İmaj seçilmedi — yalnızca <write> komutu gönderildi; veri akışı için imaj seçin.",
                            "FIREHOSE");

                        StatusText = "Write komutu gönderildi (veri yok)";
                        return true;
                    }

                    case FirehoseAction.Erase:
                    {
                        var result = await client.EraseAsync(
                            request.PhysicalPartition, request.StartSector, request.SectorCount);

                        if (!result.Success)
                        {
                            return false;
                        }

                        StatusText = "Erase → ACK (simülasyon)";
                        return true;
                    }

                    case FirehoseAction.Configure:
                    {
                        var result = await client.ConfigureAsync("ufs");

                        if (!result.Success)
                        {
                            return false;
                        }

                        StatusText = "Configure → ACK (simülasyon)";
                        return true;
                    }

                    default:
                    {
                        var result = await client.NopAsync();

                        if (!result.Success)
                        {
                            return false;
                        }

                        FirehoseDataPreview = "NOP → ACK";
                        StatusText = "NOP → ACK (simülasyon)";
                        return true;
                    }
                }
            }
        }

        private async Task<bool> FlashRomAsync(bool useWinUsb)
        {
            if (string.IsNullOrWhiteSpace(_rawProgramPath) || !File.Exists(_rawProgramPath))
            {
                LogHub.Instance.Warn("Önce rawprogram XML seçin.", "FIREHOSE");
                return false;
            }

            if (string.IsNullOrWhiteSpace(_romDirectory))
            {
                LogHub.Instance.Warn("ROM klasörü belirlenemedi.", "FIREHOSE");
                return false;
            }

            var xmlPath = _rawProgramPath;
            var romDirectory = _romDirectory;

            StatusText = useWinUsb ? "WinUSB ROM flash hazırlanıyor..." : "ROM flash hazırlanıyor...";

            var xml = await Task.Run(() => File.ReadAllText(xmlPath));
            var lastMilestone = 0;

            var progress = new Progress<int>(value =>
            {
                UpdateOnUi(() => Progress = value);

                if (value < 100 && value / 25 > lastMilestone)
                {
                    lastMilestone = value / 25;
                    LogHub.Instance.Step(
                        "ROM yazma %" + (lastMilestone * 25) + " · " + Path.GetFileName(xmlPath),
                        "FIREHOSE");
                }
            });

            LogHub.Instance.Info(
                (useWinUsb ? "WinUSB" : "Loopback") + " Firehose ROM akışı: " +
                Path.GetFileName(xmlPath) + " · " + romDirectory, "FIREHOSE");

            try
            {
                FlashReport report;

                if (useWinUsb)
                {
                    // Cihaz açma/enumeration da UI thread dışında yapılır.
                    report = await Task.Run(async () =>
                    {
                        using var edl = WinUsbEdlDevice.Open();
                        using var client = new FirehoseClient(edl.Firehose);
                        var flasher = new FirehoseRomFlasher(client);

                        return await flasher.FlashAsync(xml, romDirectory, "ufs", progress);
                    });
                }
                else
                {
                    using var client = new FirehoseClient(new LoopbackFirehoseTransport());
                    var flasher = new FirehoseRomFlasher(client);

                    report = await Task.Run(async () =>
                        await flasher.FlashAsync(xml, romDirectory, "ufs", progress));
                }

                FlashSummary = report.FilesWritten + " dosya · " +
                               report.BytesWritten.ToString("N0") + " bayt · atlanan " +
                               report.Skipped.Count;

                foreach (var skipped in report.Skipped.Take(5))
                {
                    LogHub.Instance.Warn("Atlandı: " + skipped, "FIREHOSE");
                }

                if (!report.Success)
                {
                    LogHub.Instance.Error("ROM flash başarısız: " + report.Error, "FIREHOSE");
                    StatusText = "ROM flash başarısız";
                    return false;
                }

                LogHub.Instance.Ok(
                    report.FilesWritten + " dosya yazıldı (" + FlashSummary + ").", "FIREHOSE");
                StatusText = "ROM flash tamamlandı";
                return true;
            }
            catch (Exception ex)
            {
                if (useWinUsb && ex.Message.Contains("WinUSB EDL cihazı bulunamadı"))
                {
                    LogHub.Instance.Warn(
                        "Canlı WinUSB flash: EDL (9008) cihazı bulunamadı. " +
                        "Cihazı EDL moduna alıp tekrar deneyin.", "FIREHOSE");
                }
                else
                {
                    LogHub.Instance.Error("ROM flash hatası: " + ex.Message, "FIREHOSE");
                }

                StatusText = "ROM flash hatası";
                return false;
            }
        }

        private async Task<bool> DownloadLoaderAsync()
        {
            var chipset = (Chipset ?? string.Empty).Trim().ToUpperInvariant();

            if (chipset.Length < 2)
            {
                LogHub.Instance.Warn("Chipset adı gerekli (ör. SM8650).", "LOADER");
                return false;
            }

            StatusText = "Loader kataloğu sorgulanıyor...";

            using var repository = new LoaderRepositoryService(new OidcOptions().ApiBase);

            byte[] bytes;

            try
            {
                LogHub.Instance.Step("Loader kataloğu: " + chipset, "LOADER");
                bytes = await repository.GetLoaderAsync(chipset);
            }
            catch (Exception ex)
            {
                LogHub.Instance.Error(
                    "Loader indirilemedi (" + chipset + "): " + ex.Message, "LOADER");
                StatusText = "Loader indirilemedi";
                return false;
            }

            if (bytes == null || bytes.Length == 0)
            {
                LogHub.Instance.Warn("Loader bulunamadı: " + chipset, "LOADER");
                return false;
            }

            _loaderPath = repository.LastCachedPath ?? string.Empty;
            OnPropertyChanged(nameof(LoaderName));

            FlashSummary = chipset + " · " + bytes.Length.ToString("N0") + " bayt loader";
            LogHub.Instance.Ok(
                "Loader indirildi: " + LoaderName + " (" + bytes.Length.ToString("N0") + " bayt)",
                "LOADER");

            StatusText = "Loader hazır";
            return true;
        }
        private FirehoseRequest BuildFirehoseRequest()
        {
            var request = new FirehoseRequest
            {
                Action = ParseFirehoseAction(FirehoseActionText),
                FileName = string.IsNullOrWhiteSpace(FirehoseFileName) ? "partition.bin" : FirehoseFileName.Trim(),
                StartSector = ParseLong(FirehoseStartSector),
                SectorCount = (int)Math.Max(1, ParseLong(FirehoseSectorCount)),
                PhysicalPartition = (int)Math.Max(0, ParseLong(FirehosePhysicalPartition)),
                SectorSize = 512
            };

            if (request.Action == FirehoseAction.Write &&
                !string.IsNullOrWhiteSpace(FirehoseImagePath) && File.Exists(FirehoseImagePath))
            {
                request.FileName = Path.GetFileName(FirehoseImagePath);
            }

            return request;
        }

        private static FirehoseAction ParseFirehoseAction(string value)
        {
            switch ((value ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "CONFIGURE": return FirehoseAction.Configure;
                case "READ": return FirehoseAction.Read;
                case "WRITE": return FirehoseAction.Write;
                case "ERASE": return FirehoseAction.Erase;
                case "PATCH": return FirehoseAction.Patch;
                case "POWER": return FirehoseAction.Power;
                default: return FirehoseAction.Nop;
            }
        }

        private static long ParseLong(string value)
        {
            return long.TryParse((value ?? string.Empty).Trim(), out var result) ? result : 0;
        }

        protected override string GetActionTitle(string actionKey)
        {
            switch (actionKey)
            {
                case "edl": return "EDL Moduna Al";
                case "unlock": return "Bootloader Kilidi";
                case "frp": return "FRP Sıfırlama";
                case "flash": return "Firehose Flash";
                case "backup": return "Tam Yedek";
                case "info": return "Bölüm Bilgisi";
                case "flash-rom": return "ROM Flash (Loopback)";
                case "flash-rom-usb": return "ROM Flash (WinUSB)";
                case "loader": return "Loader İndir";
                default: return "Qualcomm işlemi";
            }
        }

        protected override IReadOnlyList<string> GetSteps(string actionKey)
        {
            switch (actionKey)
            {
                case "edl":
                    return Steps(
                        "USB arayüzü yeniden taranıyor...",
                        "Qualcomm HS-USB QDLoader 9008 bekleniyor...",
                        "Sahara protokolü el sıkışması yapılıyor...",
                        "Firehose yükleyici (prog_firehose_ddr.elf) gönderiliyor...",
                        "EDL oturumu hazır.");

                case "unlock":
                    return Steps(
                        "Cihaz kimliği okunuyor...",
                        "Fastboot arayüzü hazırlanıyor...",
                        "OEM yetkisi doğrulanıyor...",
                        "Kilit durumu: AÇIK (demo).");

                case "frp":
                    return Steps(
                        "FRP bölümü tespit ediliyor...",
                        "Mevcut bayrak yedekleniyor...",
                        "FRP bayrağı temizleniyor...",
                        "Cihaz yeniden başlatılıyor...");

                case "flash":
                    return Steps(
                        "Programlayıcı yükleniyor...",
                        "Ham XML dosyası ayrıştırılıyor...",
                        "Bölümler yazılıyor (%25)...",
                        "Bölümler yazılıyor (%70)...",
                        "Yazma doğrulanıyor...",
                        "Flash tamamlandı.");

                case "backup":
                    return Steps(
                        "GPT tablosu okunuyor...",
                        "Bölüm aralıkları hesaplanıyor...",
                        "Yedek akışı oluşturuluyor...",
                        "SHA-256 özeti hesaplanıyor...");

                case "info":
                    return Steps(
                        "Bölüm tablosu isteniyor...",
                        "Alan adları çözümleniyor...",
                        "Boyutlar okunuyor...",
                        "Tablo günlüğe yazıldı.");

                default:
                    return Steps("İşlem hazırlanıyor...", "Tamamlandı.");
            }
        }
    }
}
