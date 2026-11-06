using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using KCTools.Common;
using KCTools.Models;
using KCTools.Protocol;
using KCTools.Security;
using KCTools.Services;

namespace KCTools.ViewModels
{
    /// <summary>MediaTek (BROM / Preloader / Scatter) modülü.</summary>
    public sealed class MtkViewModel : PageViewModel
    {
        private string _scatterPath = string.Empty;
        private string _deviceName = "—";
        private string _daPath = string.Empty;
        private string _daSummary = "DA dosyası seçilmedi";
        private string _daPlan = "DA seçildiğinde blok planı burada görünür.";
        private string _chipset = "MT6789";
        private string _daUploadSummary = "DA yüklenmedi.";

        public MtkViewModel()
            : base("mtk", "MediaTek", "BROM/Preloader modu, scatter tabanlı yazma ve NVRAM yedekleri")
        {
            Tiles.Add(new ActionTile("brom", "BROM Moduna Al", "Preloader el sıkışması ile BROM'a geç", "Icon.Bolt"));
            Tiles.Add(new ActionTile("preloader", "Preloader Yükle", "Ön yükleyiciyi yeniden yaz", "Icon.Download"));
            Tiles.Add(new ActionTile("scatter", "Scatter Flash", "Seçili scatter ile imaj yaz", "Icon.Layers"));
            Tiles.Add(new ActionTile("nvram", "NVRAM Yedek", "IMEI / kalibrasyon verisini sakla", "Icon.Folder"));
            Tiles.Add(new ActionTile("format", "Format All", "Tüm bölümleri temizle (dikkat)", "Icon.Sliders"));
            Tiles.Add(new ActionTile("readback", "Readback", "Canlı bölüm dökümü al", "Icon.Terminal"));

            BrowseDaCommand = new RelayCommand(_ => BrowseDa());
            InspectDaCommand = new RelayCommand(_ => { _ = RunAsync("da"); }, _ => !IsBusy);
            DownloadDaCommand = new RelayCommand(
                _ => { _ = RunAsync("da-download"); }, _ => !IsBusy);
            UploadDaCommand = new RelayCommand(
                _ => { _ = RunAsync("da-upload"); }, _ => !IsBusy);
        }

        public ICommand BrowseDaCommand { get; }

        public ICommand InspectDaCommand { get; }

        public ICommand DownloadDaCommand { get; }

        public ICommand UploadDaCommand { get; }

        public string DaPath
        {
            get => _daPath;
            private set
            {
                if (Set(ref _daPath, value))
                {
                    OnPropertyChanged(nameof(DaFileName));
                }
            }
        }

        public string DaFileName =>
            string.IsNullOrWhiteSpace(DaPath) ? "DA dosyası seçilmedi" : Path.GetFileName(DaPath);

        public string DaSummary
        {
            get => _daSummary;
            private set => Set(ref _daSummary, value);
        }

        public string DaPlan
        {
            get => _daPlan;
            private set => Set(ref _daPlan, value);
        }

        public string ScatterPath
        {
            get => _scatterPath;
            private set => Set(ref _scatterPath, value);
        }

        public bool HasScatter => !string.IsNullOrWhiteSpace(ScatterPath);

        public string Chipset
        {
            get => _chipset;
            set => Set(ref _chipset, value);
        }

        public string DaUploadSummary
        {
            get => _daUploadSummary;
            private set => Set(ref _daUploadSummary, value);
        }

        /// <summary>Oturum açık olan cihazın kısa etiketi ("COM5 · MTK Preloader").</summary>
        public string DeviceName
        {
            get => _deviceName;
            private set => Set(ref _deviceName, value);
        }

        public override void OnDeviceAttached(DetectedDevice device)
        {
            DeviceName = device == null ? "—" : device.ShortDisplay;
        }

        public override void OnDeviceDetached()
        {
            DeviceName = "—";
        }

        public string ScatterDisplay =>
            HasScatter ? ScatterPath : "Scatter dosyası seçilmedi";

        public void SetScatterPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            ScatterPath = path;
            OnPropertyChanged(nameof(HasScatter));
            OnPropertyChanged(nameof(ScatterDisplay));
            LogHub.Instance.Ok("Scatter dosyası seçildi: " + Path.GetFileName(path), "MTK");
        }

        protected override void OnCompleted(string actionKey)
        {
            if (actionKey == "scatter" && !HasScatter)
            {
                LogHub.Instance.Warn("Scatter dosyası seçilmedi — varsayılan akış kullanıldı.", "MTK");
            }
        }

        protected override async Task<bool> ExecuteActionAsync(string actionKey, string actionTitle)
        {
            if (string.Equals(actionKey, "da", StringComparison.OrdinalIgnoreCase))
            {
                return await InspectDaAsync();
            }

            if (string.Equals(actionKey, "da-download", StringComparison.OrdinalIgnoreCase))
            {
                return await DownloadDaAsync();
            }

            if (string.Equals(actionKey, "da-upload", StringComparison.OrdinalIgnoreCase))
            {
                return await UploadDaAsync();
            }

            return await base.ExecuteActionAsync(actionKey, actionTitle);
        }

        private void BrowseDa()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "MTK Download Agent (DA) seç",
                Filter = "DA dosyası (*.bin)|*.bin|Tüm dosyalar (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                DaPath = dialog.FileName;
                LogHub.Instance.Info("DA seçildi: " + dialog.FileName, "MTK");
            }
        }

        private async Task<bool> InspectDaAsync()
        {
            if (string.IsNullOrWhiteSpace(DaPath) || !File.Exists(DaPath))
            {
                LogHub.Instance.Warn("Önce bir DA dosyası seçin.", "MTK");
                return false;
            }

            StatusText = "DA dosyası inceleniyor...";

            var path = DaPath;

            // DA okuma + checksum bloğu arka planda (büyük DA dosyalarında UI donmasın).
            var inspected = await Task.Run(() =>
            {
                var data = File.ReadAllBytes(path);
                return (Header: MtkBromTools.InspectDa(data), Blocks: MtkBromTools.PlanUpload(data));
            });

            var header = inspected.Header;
            var blocks = inspected.Blocks;

            DaSummary = header.Summary;
            DaPlan = MtkBromTools.DescribePlan(blocks);

            LogHub.Instance.Ok("DA: " + header.Summary, "MTK");
            LogHub.Instance.Info(
                "Yükleme planı: " + blocks.Count + " blok × " + MtkBromTools.DefaultBlockSize +
                " bayt · her blok için 16 bit checksum hesaplandı. BROM aktarımı 6. parçada.", "MTK");

            StatusText = header.IsValid ? "DA doğrulandı" : "MMM imzası bulunamadı";
            return header.IsValid;
        }

        private async Task<bool> DownloadDaAsync()
        {
            var chipset = (Chipset ?? string.Empty).Trim().ToUpperInvariant();

            if (chipset.Length < 2)
            {
                LogHub.Instance.Warn("Chipset adı gerekli (ör. MT6789).", "MTK");
                return false;
            }

            StatusText = "DA kataloğu sorgulanıyor...";

            using var repository = new LoaderRepositoryService(new OidcOptions().ApiBase);

            byte[] bytes;

            try
            {
                LogHub.Instance.Step("DA kataloğu: " + chipset, "MTK");
                bytes = await repository.GetLoaderAsync(chipset);
            }
            catch (Exception ex)
            {
                LogHub.Instance.Error(
                    "DA indirilemedi (" + chipset + "): " + ex.Message, "MTK");
                StatusText = "DA indirilemedi";
                return false;
            }

            if (bytes == null || bytes.Length == 0)
            {
                LogHub.Instance.Warn("Bu chipset için DA bulunamadı: " + chipset, "MTK");
                return false;
            }

            DaPath = repository.LastCachedPath ?? string.Empty;
            DaSummary = "Sunucudan indirildi · " + bytes.Length.ToString("N0") + " bayt";
            DaUploadSummary = "DA indirildi: " + DaFileName;

            LogHub.Instance.Ok(
                "DA indirildi: " + DaFileName + " (" + bytes.Length.ToString("N0") + " bayt)", "MTK");

            StatusText = "DA hazır";
            return true;
        }

        private async Task<bool> UploadDaAsync()
        {
            if (string.IsNullOrWhiteSpace(DaPath) || !File.Exists(DaPath))
            {
                LogHub.Instance.Warn("Önce DA dosyası seçin veya indirin.", "MTK");
                return false;
            }

            var path = DaPath;
            StatusText = "DA BROM'a gönderiliyor (simülatör)...";

            var lastMilestone = 0;

            var progress = new Progress<int>(value =>
            {
                UpdateOnUi(() => Progress = value);

                if (value < 100 && value / 25 > lastMilestone)
                {
                    lastMilestone = value / 25;
                    LogHub.Instance.Step("DA blok yazma %" + (lastMilestone * 25), "MTK");
                }
            });

            var da = await Task.Run(() => File.ReadAllBytes(path));

            var result = await Task.Run(async () =>
            {
                using var engine = new MtkBromEngine(new MtkBromSimulatorTransport());
                return await engine.UploadDaAsync(da, progress);
            });

            if (!result.Success)
            {
                DaUploadSummary = "Hata: " + result.Error;
                LogHub.Instance.Error("DA yüklenemedi: " + result.Error, "MTK");
                StatusText = "DA yüklenemedi";
                return false;
            }

            DaUploadSummary = result.Blocks + " blok · " + result.Bytes.ToString("N0") + " bayt";
            LogHub.Instance.Ok(
                "DA simülatörde yüklendi: " + DaUploadSummary + " (gerçek BROM donanım testi yapılmadı).",
                "MTK");

            StatusText = "DA yüklendi (simülatör)";
            return true;
        }
        protected override string GetActionTitle(string actionKey)
        {
            switch (actionKey)
            {
                case "brom": return "BROM Moduna Al";
                case "preloader": return "Preloader Yükle";
                case "scatter": return "Scatter Flash";
                case "nvram": return "NVRAM Yedek";
                case "format": return "Format All";
                case "readback": return "Readback";
                case "da-download": return "DA İndir";
                case "da-upload": return "DA Yükle (Simülatör)";
                default: return "MediaTek işlemi";
            }
        }

        protected override IReadOnlyList<string> GetSteps(string actionKey)
        {
            switch (actionKey)
            {
                case "brom":
                    return Steps(
                        "Cihaz kapatılıp USB'ye bağlanmalı...",
                        "Preloader portu dinleniyor...",
                        "BROM el sıkışması kuruluyor...",
                        "BROM modu aktif (demo).");

                case "preloader":
                    return Steps(
                        "Preloader bölümü doğrulanıyor...",
                        "Hedef adres hesaplanıyor...",
                        "Preloader yazılıyor...",
                        "Yazma doğrulandı.");

                case "scatter":
                    return Steps(
                        "Scatter dosyası ayrıştırılıyor...",
                        "Bölüm haritası oluşturuluyor...",
                        "İmajlar aktarılıyor (%40)...",
                        "İmajlar aktarılıyor (%80)...",
                        "Flash tamamlandı, cihaz yeniden başlatılıyor...");

                case "nvram":
                    return Steps(
                        "NVRAM bölümü okunuyor...",
                        "IMEI alanları maskeleniyor...",
                        "Yedek dosyası yazılıyor...",
                        "NVRAM yedeği hazır.");

                case "format":
                    return Steps(
                        "Uyarı: geri alınamaz işlem onaylandı...",
                        "Bölüm tablosu temizleniyor...",
                        "Dosya sistemi yeniden oluşturuluyor...",
                        "Format tamamlandı.");

                case "readback":
                    return Steps(
                        "Okuma adresi belirleniyor...",
                        "Bloklar aktarılıyor (%30)...",
                        "Bloklar aktarılıyor (%75)...",
                        "Döküm dosyası yazıldı.");

                default:
                    return Steps("İşlem hazırlanıyor...", "Tamamlandı.");
            }
        }
    }
}
