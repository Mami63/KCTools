using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Input;
using KCTools.Common;
using KCTools.Models;
using KCTools.Services;

namespace KCTools.ViewModels
{
    /// <summary>
    /// IMEI onarımı: modem portundan IMEI okuma, Luhn doğrulama ve QCN dosyası
    /// üzerinde ASCII/BCD eşleşmelerini yedekli olarak değiştirme.
    /// </summary>
    public sealed class ImeiViewModel : PageViewModel, IDisposable
    {
        private readonly AtCommandService _at = new AtCommandService();
        private readonly ModemService _modem;
        private readonly AdbClient _adb = new AdbClient();
        private readonly FastbootClient _fastboot = new FastbootClient();

        private QcnDocument _qcn;
        private ComPortOption _selectedPort;
        private int _selectedBaud = 115200;
        private bool _isPortOpen;
        private string _portStateText = "Kapalı";
        private string _currentImei = "—";
        private string _currentImeiStatus = "IMEI okunmadı";
        private string _newImei = string.Empty;
        private string _newImeiStatus = "15 hane girin";
        private bool _confirmed;
        private string _qcnFileName = "QCN dosyası seçilmedi";
        private string _qcnSummary = "Onarım için QCN yedeği gerekir";
        private QcnMatch _selectedMatch;
        private QcnBackup _selectedBackup;

        public ImeiViewModel()
            : base("imei", "IMEI Onarımı", "IMEI okuma, Luhn doğrulama ve QCN üzerinde güvenli değişiklik")
        {
            _modem = new ModemService(_at);

            Ports = new ObservableCollection<ComPortOption>();
            BaudRates = new ObservableCollection<int> { 9600, 19200, 38400, 57600, 115200, 230400, 460800, 921600 };
            QcnMatches = new ObservableCollection<QcnMatch>();
            Backups = new ObservableCollection<QcnBackup>();

            RefreshPortsCommand = new RelayCommand(_ => RefreshPorts());
            AutoDetectCommand = new RelayCommand(_ => { _ = AutoDetectPortAsync(); }, _ => !IsBusy);
            TogglePortCommand = new RelayCommand(_ => { _ = TogglePortAsync(); }, _ => !IsBusy);
            ReadImeiCommand = new RelayCommand(_ => { _ = RunAsync("read"); }, _ => !IsBusy);
            BrowseQcnCommand = new RelayCommand(_ => { _ = BrowseQcnAsync(); });
            RefreshBackupsCommand = new RelayCommand(_ => RefreshBackups());
            RestoreBackupCommand = new RelayCommand(
                _ => { _ = RestoreBackupAsync(); }, _ => _qcn != null && SelectedBackup != null);
            VerifyCommand = new RelayCommand(_ => { _ = VerifyAsync(); }, _ => _qcn != null);
            SearchQcnCommand = new RelayCommand(_ => { _ = SearchQcnAsync(); }, _ => _qcn != null);
            FixLuhnCommand = new RelayCommand(_ => FixLuhn());
            RepairCommand = new RelayCommand(_ => { _ = RunAsync("repair"); }, _ => CanRepair && !IsBusy);

            RefreshPorts();
        }

        public ObservableCollection<ComPortOption> Ports { get; }

        public ObservableCollection<int> BaudRates { get; }

        public ObservableCollection<QcnMatch> QcnMatches { get; }

        public ObservableCollection<QcnBackup> Backups { get; }

        public ICommand RefreshPortsCommand { get; }

        public ICommand AutoDetectCommand { get; }

        public ICommand TogglePortCommand { get; }

        public ICommand ReadImeiCommand { get; }

        public ICommand BrowseQcnCommand { get; }

        public ICommand RefreshBackupsCommand { get; }

        public ICommand RestoreBackupCommand { get; }

        public ICommand VerifyCommand { get; }

        public QcnBackup SelectedBackup
        {
            get => _selectedBackup;
            set
            {
                if (Set(ref _selectedBackup, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public ICommand SearchQcnCommand { get; }

        public ICommand FixLuhnCommand { get; }

        public ICommand RepairCommand { get; }

        public ComPortOption SelectedPort
        {
            get => _selectedPort;
            set
            {
                if (Set(ref _selectedPort, value))
                {
                    OnPropertyChanged(nameof(PortHint));
                }
            }
        }

        public string PortHint =>
            SelectedPort == null ? "Port bulunamadı" : SelectedPort.Name + " · " + SelectedPort.Detail;

        public int SelectedBaud
        {
            get => _selectedBaud;
            set => Set(ref _selectedBaud, value);
        }

        public bool IsPortOpen
        {
            get => _isPortOpen;
            private set
            {
                if (Set(ref _isPortOpen, value))
                {
                    OnPropertyChanged(nameof(PortButtonText));
                }
            }
        }

        public string PortButtonText => IsPortOpen ? "Portu Kapat" : "Portu Aç";

        public string PortStateText
        {
            get => _portStateText;
            private set => Set(ref _portStateText, value);
        }

        public string CurrentImei
        {
            get => _currentImei;
            private set
            {
                if (Set(ref _currentImei, value))
                {
                    UpdateCurrentStatus();
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public string CurrentImeiStatus
        {
            get => _currentImeiStatus;
            private set => Set(ref _currentImeiStatus, value);
        }

        public string NewImei
        {
            get => _newImei;
            set
            {
                if (!Set(ref _newImei, value))
                {
                    return;
                }

                UpdateNewStatus();
                OnPropertyChanged(nameof(NewImeiValid));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool NewImeiValid => ModemService.IsValidImei(ModemService.OnlyDigits(NewImei));

        public string NewImeiStatus
        {
            get => _newImeiStatus;
            private set => Set(ref _newImeiStatus, value);
        }

        public bool Confirmed
        {
            get => _confirmed;
            set
            {
                if (Set(ref _confirmed, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool CanRepair =>
            Confirmed && _qcn != null && NewImeiValid &&
            ModemService.OnlyDigits(CurrentImei).Length == 15;

        public string QcnFileName
        {
            get => _qcnFileName;
            private set => Set(ref _qcnFileName, value);
        }

        public string QcnSummary
        {
            get => _qcnSummary;
            private set => Set(ref _qcnSummary, value);
        }

        public QcnMatch SelectedMatch
        {
            get => _selectedMatch;
            set => Set(ref _selectedMatch, value);
        }

        public void RefreshPorts()
        {
            var previous = SelectedPort?.Name;
            Ports.Clear();

            foreach (var port in PortWatcher.Scan())
            {
                Ports.Add(new ComPortOption { Name = port.Name, Detail = port.FriendlyName });
            }

            SelectedPort = Ports.FirstOrDefault(p =>
                                string.Equals(p.Name, previous, StringComparison.OrdinalIgnoreCase))
                            ?? Ports.FirstOrDefault();

            OnPropertyChanged(nameof(PortHint));
        }

        public override void OnDeviceAttached(DetectedDevice device)
        {
            RefreshPorts();
        }

        public void Dispose()
        {
            _at.Dispose();
        }

        protected override async Task<bool> ExecuteActionAsync(string actionKey, string actionTitle)
        {
            switch (actionKey)
            {
                case "read":
                    return await ReadImeiAsync();

                case "repair":
                    return await RepairAsync();

                default:
                    LogHub.Instance.Warn("Bilinmeyen IMEI işlemi: " + actionKey, "IMEI");
                    return false;
            }
        }

        private async Task TogglePortAsync()
        {
            if (_at.IsOpen)
            {
                _at.Close();
                IsPortOpen = false;
                PortStateText = "Kapalı";
                StatusText = "AT portu kapatıldı";
                return;
            }

            if (SelectedPort == null)
            {
                LogHub.Instance.Warn("IMEI okumak için bir modem portu seçin.", "IMEI");
                return;
            }

            StatusText = "AT portu açılıyor...";
            var opened = await _at.OpenAsync(SelectedPort.Name, SelectedBaud);

            IsPortOpen = opened;
            PortStateText = opened ? SelectedPort.Name + " · " + SelectedBaud : "Açılamadı";
            StatusText = PortStateText;
        }

        private async Task<bool> EnsurePortOpenAsync()
        {
            if (_at.IsOpen)
            {
                return true;
            }

            if (SelectedPort == null)
            {
                LogHub.Instance.Warn("Modem portu seçilmedi.", "IMEI");
                return false;
            }

            var opened = await _at.OpenAsync(SelectedPort.Name, SelectedBaud);
            IsPortOpen = opened;
            PortStateText = opened ? SelectedPort.Name + " · " + SelectedBaud : "Açılamadı";

            return opened;
        }

        private async Task<bool> ReadImeiAsync()
        {
            string imei = null;
            var source = "AT";

            // 1) AT portu varsa modem üzerinden oku.
            if (_at.IsOpen || SelectedPort != null)
            {
                if (await EnsurePortOpenAsync())
                {
                    StatusText = "IMEI okunuyor (AT)...";
                    imei = await _modem.ReadImeiAsync();
                }
            }

            // 2) AT yoksa/başarısızsa ADB getprop üzerinden dene (MIUI/vendor anahtarları).
            if (string.IsNullOrWhiteSpace(imei))
            {
                StatusText = "IMEI okunuyor (ADB)...";
                var adbResult = await ReadImeiViaAdbAsync();
                imei = adbResult.Primary;
                source = "ADB";

                if (!string.IsNullOrWhiteSpace(adbResult.Secondary))
                {
                    LogHub.Instance.Info("2. IMEI: " + adbResult.Secondary, "IMEI");
                }
            }

            // 3) ADB yoksa fastboot değişkenlerini dene (bazı üreticilerde açık olabilir).
            if (string.IsNullOrWhiteSpace(imei))
            {
                StatusText = "IMEI okunuyor (Fastboot)...";

                try
                {
                    var serials = await _fastboot.ListSerialsAsync();

                    if (serials.Count > 0)
                    {
                        _fastboot.Serial = serials[0];
                        var fastbootImei = await _fastboot.ReadImeiAsync();

                        if (!string.IsNullOrWhiteSpace(fastbootImei))
                        {
                            imei = fastbootImei;
                            source = "Fastboot";
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogHub.Instance.Warn("Fastboot IMEI okuma hatası: " + ex.Message, "IMEI");
                }
            }

            Progress = 100;

            if (string.IsNullOrWhiteSpace(imei))
            {
                CurrentImei = "—";
                CurrentImeiStatus = "IMEI alınamadı — Fastboot modunda okunamaz";
                LogHub.Instance.Warn(
                    "IMEI alınamadı: fastboot modunda IMEI değişkeni yok. " +
                    "Cihazı Android'e açıp ADB ile veya modem (AT) portuyla okuyun; " +
                    "QCN yedeği varsa ondan alın.", "IMEI");
                return false;
            }

            CurrentImei = imei;
            LogHub.Instance.Ok(
                "IMEI (" + source + "): " + imei +
                (ModemService.IsValidImei(imei) ? " · Luhn geçerli" : " · Luhn hatası"), "IMEI");

            StatusText = "IMEI: " + imei + " (" + source + ")";
            return true;
        }

        private async Task<(string Primary, string Secondary)> ReadImeiViaAdbAsync()
        {
            if (!Toolchain.Instance.HasAdb)
            {
                return (null, null);
            }

            try
            {
                var devices = await _adb.ListDevicesAsync();
                var online = devices.FirstOrDefault(d => d.IsOnline);

                if (online == null)
                {
                    return (null, null);
                }

                _adb.Serial = online.Serial;
                var props = await _adb.GetPropsAsync();

                var keys = new[]
                {
                    "ro.ril.miui.imei0", "ro.ril.miui.imei1",
                    "ro.ril.oem.imei", "ro.ril.oem.imei1", "ro.ril.oem.imei2",
                    "ro.vendor.oem.imei", "persist.radio.imei"
                };

                var found = new List<string>();

                foreach (var key in keys)
                {
                    if (!props.TryGetValue(key, out var value))
                    {
                        continue;
                    }

                    var digits = ModemService.OnlyDigits(value);

                    if (digits.Length == 15 && !found.Contains(digits))
                    {
                        found.Add(digits);
                    }
                }

                return (found.FirstOrDefault(), found.Skip(1).FirstOrDefault());
            }
            catch (Exception ex)
            {
                LogHub.Instance.Warn("ADB ile IMEI okunamadı: " + ex.Message, "IMEI");
                return (null, null);
            }
        }

        private async Task AutoDetectPortAsync()
        {
            if (Ports.Count == 0)
            {
                LogHub.Instance.Warn(
                    "Hiç COM portu yok. Cihazı modem/diag moduna alın veya USB sürücüsünü kurun.", "IMEI");
                return;
            }

            StatusText = "AT portu aranıyor...";

            foreach (var option in Ports)
            {
                if (!await ProbeAtAsync(option.Name))
                {
                    continue;
                }

                SelectedPort = option;
                IsPortOpen = false;
                PortStateText = option.Name + " · AT yanıt verdi";
                StatusText = "AT portu: " + option.Name;
                LogHub.Instance.Ok("AT portu bulundu: " + option.Name, "IMEI");
                return;
            }

            StatusText = "AT portu bulunamadı";
            LogHub.Instance.Warn("Hiçbir COM portu AT komutuna yanıt vermedi.", "IMEI");
        }

        private static async Task<bool> ProbeAtAsync(string portName)
        {
            try
            {
                using (var port = new System.IO.Ports.SerialPort(portName, 115200))
                {
                    port.ReadTimeout = 700;
                    port.WriteTimeout = 700;
                    port.Open();
                    await Task.Delay(120);
                    port.DiscardInBuffer();
                    port.Write("AT\r");
                    await Task.Delay(450);
                    var response = port.ReadExisting();
                    return response != null && response.Contains("OK");
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void UpdateCurrentStatus()
        {
            var digits = ModemService.OnlyDigits(CurrentImei);

            if (digits.Length != 15)
            {
                CurrentImeiStatus = "IMEI okunmadı";
                return;
            }

            CurrentImeiStatus = ModemService.IsValidImei(digits)
                ? digits + " · Luhn geçerli"
                : digits + " · Luhn hatası";
        }

        private void UpdateNewStatus()
        {
            var digits = ModemService.OnlyDigits(NewImei);

            if (digits.Length == 0)
            {
                NewImeiStatus = "15 hane girin";
                return;
            }

            if (digits.Length != 15)
            {
                NewImeiStatus = digits.Length + "/15 hane";
                return;
            }

            if (ModemService.IsValidImei(digits))
            {
                NewImeiStatus = "Geçerli IMEI (Luhn uyumlu)";
                return;
            }

            var suggestion = digits.Substring(0, 14) + ModemService.ComputeLuhnCheckDigit(digits.Substring(0, 14));
            NewImeiStatus = "Luhn hatası — öneri: " + suggestion;
        }

        private void FixLuhn()
        {
            var digits = ModemService.OnlyDigits(NewImei);

            if (digits.Length == 14)
            {
                NewImei = digits + ModemService.ComputeLuhnCheckDigit(digits);
                return;
            }

            if (digits.Length == 15)
            {
                NewImei = digits.Substring(0, 14) +
                          ModemService.ComputeLuhnCheckDigit(digits.Substring(0, 14));
                return;
            }

            LogHub.Instance.Warn("Luhn düzeltmesi için 14 veya 15 hane girin.", "IMEI");
        }

        private async Task BrowseQcnAsync()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "QCN yedeği seç",
                Filter = "QCN yedeği (*.qcn)|*.qcn|Yedek (*.bak)|*.bak|Tüm dosyalar (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                StatusText = "QCN yükleniyor...";

                var document = await Task.Run(() => QcnDocument.Load(dialog.FileName));

                _qcn = document;
                QcnFileName = Path.GetFileName(dialog.FileName) + " · " + document.Size.ToString("N0") + " bayt";
                QcnSummary = "QCN hazır — mevcut IMEI'yi arayın";
                QcnMatches.Clear();
                SelectedMatch = null;
                RefreshBackups();
                StatusText = "QCN yüklendi";

                CommandManager.InvalidateRequerySuggested();
            }
            catch (Exception ex)
            {
                LogHub.Instance.Error("QCN yüklenemedi: " + ex.Message, "IMEI");
                StatusText = "QCN yüklenemedi";
            }
        }

        private async Task SearchQcnAsync()
        {
            if (_qcn == null)
            {
                return;
            }

            var digits = ModemService.OnlyDigits(CurrentImei);

            if (digits.Length != 15)
            {
                LogHub.Instance.Warn("Önce mevcut IMEI'yi okuyun veya girin.", "IMEI");
                return;
            }

            StatusText = "QCN içinde aranıyor...";
            var matches = await Task.Run(() => _qcn.FindImei(digits));

            QcnMatches.Clear();
            foreach (var match in matches)
            {
                QcnMatches.Add(match);
            }

            SelectedMatch = matches.FirstOrDefault();
            QcnSummary = matches.Count == 0
                ? "Eşleşme yok — dosya bu IMEI'ye ait olmayabilir"
                : matches.Count + " eşleşme (ASCII/BCD)";

            StatusText = QcnSummary;
            LogHub.Instance.Info(QcnSummary, "IMEI");

            CommandManager.InvalidateRequerySuggested();
        }

        private void RefreshBackups()
        {
            Backups.Clear();

            if (_qcn == null)
            {
                return;
            }

            foreach (var path in QcnStore.ListBackups(_qcn.FilePath))
            {
                Backups.Add(new QcnBackup(path));
            }

            SelectedBackup = Backups.FirstOrDefault();
            LogHub.Instance.Info("QCN yedeği: " + Backups.Count + " adet", "IMEI");
        }

        private async Task RestoreBackupAsync()
        {
            if (_qcn == null || SelectedBackup == null)
            {
                return;
            }

            var owner = System.Windows.Application.Current?.MainWindow;
            var question = "Seçili yedek mevcut QCN dosyasının üzerine yazılacak.\n" +
                           "Önce mevcut dosyanın güvenlik kopyası alınır.\n\nDevam edilsin mi?";

            var answer = owner == null
                ? System.Windows.MessageBox.Show(
                    question, "QCN Geri Yükle",
                    System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning)
                : System.Windows.MessageBox.Show(
                    owner, question, "QCN Geri Yükle",
                    System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);

            if (answer != System.Windows.MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                StatusText = "Yedek geri yükleniyor...";

                var target = _qcn.FilePath;
                var backup = SelectedBackup.Path;

                await Task.Run(() => QcnStore.Restore(backup, target));

                _qcn = await Task.Run(() => QcnDocument.Load(target));
                QcnFileName = Path.GetFileName(target) + " · " + _qcn.Size.ToString("N0") + " bayt";
                QcnSummary = "Yedek geri yüklendi: " + Path.GetFileName(backup);
                QcnMatches.Clear();
                SelectedMatch = null;
                RefreshBackups();

                StatusText = "Yedek geri yüklendi";
                LogHub.Instance.Ok("QCN yedeği geri yüklendi: " + Path.GetFileName(backup), "IMEI");
            }
            catch (Exception ex)
            {
                StatusText = "Geri yükleme hatası";
                LogHub.Instance.Error("Yedek geri yüklenemedi: " + ex.Message, "IMEI");
            }
        }

        private async Task VerifyAsync()
        {
            if (_qcn == null)
            {
                return;
            }

            try
            {
                var path = _qcn.FilePath;

                var hash = await Task.Run(() =>
                {
                    using (var sha = SHA256.Create())
                    using (var stream = File.OpenRead(path))
                    {
                        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
                    }
                });

                var matches = string.Equals(hash, _qcn.Sha256, StringComparison.OrdinalIgnoreCase);

                StatusText = matches ? "QCN bütünlüğü doğrulandı" : "QCN SHA-256 farklı!";
                LogHub.Instance.Info(
                    "QCN doğrulama · " + (matches ? "eşleşiyor" : "FARKLI") +
                    " · SHA-256 " + hash.Substring(0, 16) + "…", "IMEI");
            }
            catch (Exception ex)
            {
                LogHub.Instance.Error("QCN doğrulanamadı: " + ex.Message, "IMEI");
            }
        }

        private async Task<bool> RepairAsync()
        {
            if (!Confirmed)
            {
                LogHub.Instance.Warn("Onarım için onay kutusunu işaretleyin.", "IMEI");
                return false;
            }

            if (_qcn == null)
            {
                LogHub.Instance.Warn("Önce QCN yedeği seçin.", "IMEI");
                return false;
            }

            if (!ModemService.TryNormalizeImei(NewImei, out var newImei, out var error, out _))
            {
                LogHub.Instance.Error("Yeni IMEI geçersiz: " + error, "IMEI");
                return false;
            }

            var current = ModemService.OnlyDigits(CurrentImei);

            if (current.Length != 15)
            {
                LogHub.Instance.Warn("Mevcut IMEI okunmadı; QCN içinde aranacak değer gerekli.", "IMEI");
                return false;
            }

            StatusText = "QCN içinde eşleşmeler aranıyor...";
            Progress = 20;

            var matches = await Task.Run(() => _qcn.FindImei(current));

            if (matches.Count == 0)
            {
                LogHub.Instance.Error(
                    "QCN içinde " + current + " bulunamadı. Dosya bu cihaza ait değilse onarım yapılamaz.", "IMEI");

                StatusText = "Eşleşme bulunamadı";
                return false;
            }

            Progress = 55;
            StatusText = "IMEI değiştiriliyor (" + matches.Count + " eşleşme)...";

            var replaced = await Task.Run(() =>
            {
                var count = 0;

                foreach (var match in matches)
                {
                    if (_qcn.ReplaceImei(match, newImei, out var replaceError))
                    {
                        count++;
                    }
                    else
                    {
                        LogHub.Instance.Warn(
                            "Eşleşme atlandı (" + match.Kind + " @ " + match.OffsetText + "): " + replaceError,
                            "IMEI");
                    }
                }

                return count;
            });

            if (replaced == 0)
            {
                StatusText = "Değişiklik uygulanamadı";
                return false;
            }

            Progress = 85;
            StatusText = "QCN yedekli kaydediliyor...";

            var backup = await Task.Run(() => _qcn.Save());
            Progress = 100;

            QcnSummary = "Onarıldı · " + replaced + " eşleşme · yedek: " + Path.GetFileName(backup);
            StatusText = "IMEI onarıldı";

            LogHub.Instance.Ok(
                "IMEI onarımı tamamlandı: " + current + " → " + newImei +
                " · " + replaced + " eşleşme · yedek: " + backup, "IMEI");

            LogHub.Instance.Warn(
                "Cihazı yeniden başlatıp IMEI'yi AT+CGSN ile tekrar doğrulayın.", "IMEI");

            CurrentImei = newImei;
            CommandManager.InvalidateRequerySuggested();

            return true;
        }

        protected override string GetActionTitle(string actionKey)
        {
            switch (actionKey)
            {
                case "read": return "IMEI Oku";
                case "repair": return "IMEI Onar";
                default: return "IMEI işlemi";
            }
        }

        protected override IReadOnlyList<string> GetSteps(string actionKey)
        {
            switch (actionKey)
            {
                case "read":
                    return Steps("AT portu açılıyor...", "AT+CGSN gönderiliyor...", "IMEI doğrulanıyor...");

                case "repair":
                    return Steps("QCN taranıyor...", "Eşleşmeler değiştiriliyor...", "Yedek alınarak kaydediliyor...");

                default:
                    return Steps("İşlem hazırlanıyor...", "Tamamlandı.");
            }
        }
    }
}
