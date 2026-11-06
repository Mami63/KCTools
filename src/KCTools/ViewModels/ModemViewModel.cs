using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using KCTools.Common;
using KCTools.Models;
using KCTools.Services;

namespace KCTools.ViewModels
{
    /// <summary>
    /// Modem / Diag sayfası: AT komutları, modem bilgisi (IMEI, SIM, sinyal),
    /// diag portu ve QCN dosyası düzenleme.
    /// </summary>
    public sealed class ModemViewModel : PageViewModel, IDisposable
    {
        private readonly AtCommandService _at = new AtCommandService();
        private readonly DiagService _diag = new DiagService();
        private readonly ModemService _modem;
        private readonly AdbClient _adb = new AdbClient();

        private QcnDocument _qcn;
        private bool _isPortOpen;
        private ComPortOption _selectedPort;
        private ComPortOption _diagPort;
        private int _selectedBaud = 115200;
        private int _selectedDiagBaud = 921600;
        private string _commandText = "ATI";
        private string _diagHexText = "7E 00 00 00 00 00 7E";
        private string _portStateText = "Kapalı";
        private string _diagStateText = "Kapalı";
        private string _qcnPath = "Dosya seçilmedi";
        private string _qcnSummary = "QCN dosyası yüklenmedi";
        private string _qcnHints = string.Empty;
        private string _qcnSearchImei = string.Empty;
        private string _newImei = string.Empty;
        private QcnMatch _selectedMatch;

        public ModemViewModel()
            : base("modem", "Modem / Diag", "AT komutları, diag portu ve QCN yedek/düzenleme")
        {
            _modem = new ModemService(_at);

            Ports = new ObservableCollection<ComPortOption>();
            BaudRates = new ObservableCollection<int> { 9600, 19200, 38400, 57600, 115200, 230400, 460800, 921600 };
            DiagBaudRates = new ObservableCollection<int> { 115200, 230400, 460800, 921600 };
            InfoFields = new ObservableCollection<DeviceField>
            {
                new DeviceField("IMEI"),
                new DeviceField("Model"),
                new DeviceField("Firmware"),
                new DeviceField("IMSI"),
                new DeviceField("SIM durumu"),
                new DeviceField("Operatör"),
                new DeviceField("Şebeke kaydı"),
                new DeviceField("Sinyal"),
                new DeviceField("Batarya")
            };
            QcnMatches = new ObservableCollection<QcnMatch>();

            Tiles.Add(new ActionTile("port", "Portu Aç / Kapat", "AT portunu bağla veya çöz", "Icon.Plug"));
            Tiles.Add(new ActionTile("info", "Bilgi Oku", "IMEI · model · yazılım · SIM", "Icon.Chip"));
            Tiles.Add(new ActionTile("imei", "IMEI Oku", "AT+CGSN + Luhn doğrulaması", "Icon.Shield"));
            Tiles.Add(new ActionTile("sim", "SIM Durumu", "CPIN · COPS · CREG", "Icon.Terminal"));
            Tiles.Add(new ActionTile("signal", "Sinyal", "CSQ → dBm", "Icon.Layers"));
            Tiles.Add(new ActionTile("diag", "Diag Modu", "AT ile diag moduna geç", "Icon.Bolt"));

            TogglePortCommand = new RelayCommand(_ => { _ = TogglePortAsync(); }, _ => !IsBusy);
            RefreshPortsCommand = new RelayCommand(_ => RefreshPorts());
            AutoDetectCommand = new RelayCommand(_ => { _ = AutoDetectPortAsync(); }, _ => !IsBusy);
            SendCommand = new RelayCommand(_ => { _ = SendAtAsync(null); }, _ => !IsBusy);
            QuickCommand = new RelayCommand(parameter => { _ = SendAtAsync(parameter as string); }, _ => !IsBusy);
            OpenDiagCommand = new RelayCommand(_ => ToggleDiag());
            SendHexCommand = new RelayCommand(_ => { _ = SendHexAsync(); }, _ => _diag.IsOpen);
            BrowseQcnCommand = new RelayCommand(_ => _ = BrowseQcnAsync());
            SearchImeiCommand = new RelayCommand(_ => _ = SearchImeiAsync(), _ => _qcn != null);
            ApplyImeiCommand = new RelayCommand(
                _ => _ = ApplyImeiAsync(), _ => _qcn != null && SelectedMatch != null);
            SaveQcnCommand = new RelayCommand(_ => _ = SaveQcnAsync(), _ => _qcn != null && _qcn.IsDirty);

            _at.Urc += (_, line) => LogHub.Instance.Info("URC: " + line, "MODEM");

            RefreshPorts();
        }

        public ObservableCollection<ComPortOption> Ports { get; }

        public ObservableCollection<int> BaudRates { get; }

        public ObservableCollection<int> DiagBaudRates { get; }

        public ObservableCollection<DeviceField> InfoFields { get; }

        public ObservableCollection<QcnMatch> QcnMatches { get; }

        public ICommand TogglePortCommand { get; }

        public ICommand RefreshPortsCommand { get; }

        public ICommand AutoDetectCommand { get; }

        public ICommand SendCommand { get; }

        public ICommand QuickCommand { get; }

        public ICommand OpenDiagCommand { get; }

        public ICommand SendHexCommand { get; }

        public ICommand BrowseQcnCommand { get; }

        public ICommand SearchImeiCommand { get; }

        public ICommand ApplyImeiCommand { get; }

        public ICommand SaveQcnCommand { get; }

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

        public ComPortOption DiagPort
        {
            get => _diagPort;
            set => Set(ref _diagPort, value);
        }

        public int SelectedBaud
        {
            get => _selectedBaud;
            set => Set(ref _selectedBaud, value);
        }

        public int SelectedDiagBaud
        {
            get => _selectedDiagBaud;
            set => Set(ref _selectedDiagBaud, value);
        }

        public bool IsPortOpen
        {
            get => _isPortOpen;
            private set
            {
                if (Set(ref _isPortOpen, value))
                {
                    OnPropertyChanged(nameof(PortButtonText));
                    OnPropertyChanged(nameof(PortStateText));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public string PortButtonText => IsPortOpen ? "Portu Kapat" : "Portu Aç";

        public string PortStateText
        {
            get => _portStateText;
            private set => Set(ref _portStateText, value);
        }

        public string PortHint =>
            SelectedPort == null ? "Port bulunamadı — cihazı bağlayıp yenileyin."
                : SelectedPort.Name + " · " + SelectedPort.Detail;

        public string DiagStateText
        {
            get => _diagStateText;
            private set => Set(ref _diagStateText, value);
        }

        public string CommandText
        {
            get => _commandText;
            set => Set(ref _commandText, value);
        }

        public string DiagHexText
        {
            get => _diagHexText;
            set => Set(ref _diagHexText, value);
        }

        public string QcnPath
        {
            get => _qcnPath;
            private set
            {
                if (Set(ref _qcnPath, value))
                {
                    OnPropertyChanged(nameof(QcnFileName));
                }
            }
        }

        public string QcnFileName =>
            string.IsNullOrWhiteSpace(QcnPath) || QcnPath == "Dosya seçilmedi"
                ? "Dosya seçilmedi"
                : Path.GetFileName(QcnPath);

        public string QcnSummary
        {
            get => _qcnSummary;
            private set => Set(ref _qcnSummary, value);
        }

        public string QcnHints
        {
            get => _qcnHints;
            private set => Set(ref _qcnHints, value);
        }

        public string QcnSearchImei
        {
            get => _qcnSearchImei;
            set => Set(ref _qcnSearchImei, value);
        }

        public string NewImei
        {
            get => _newImei;
            set => Set(ref _newImei, value);
        }

        public QcnMatch SelectedMatch
        {
            get => _selectedMatch;
            set
            {
                if (Set(ref _selectedMatch, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public override void OnDeviceAttached(DetectedDevice device)
        {
            RefreshPorts();
        }

        public override void OnDeviceDetached()
        {
            RefreshPorts();
        }

        public void Dispose()
        {
            _at.Dispose();
            _diag.Dispose();
        }

        public void RefreshPorts()
        {
            var previous = SelectedPort?.Name;
            var previousDiag = DiagPort?.Name;

            Ports.Clear();

            foreach (var port in PortWatcher.Scan())
            {
                Ports.Add(new ComPortOption { Name = port.Name, Detail = port.FriendlyName });
            }

            SelectedPort = Ports.FirstOrDefault(p =>
                                string.Equals(p.Name, previous, StringComparison.OrdinalIgnoreCase))
                            ?? Ports.FirstOrDefault();

            DiagPort = Ports.FirstOrDefault(p =>
                           string.Equals(p.Name, previousDiag, StringComparison.OrdinalIgnoreCase))
                       ?? Ports.FirstOrDefault(p =>
                           string.Equals(p.Name, SelectedPort?.Name, StringComparison.OrdinalIgnoreCase) == false);

            OnPropertyChanged(nameof(PortHint));

            LogHub.Instance.Info(Ports.Count == 0
                ? "AT için seri port bulunamadı."
                : Ports.Count + " seri port bulundu: " + string.Join(", ", Ports.Select(p => p.Name)), "MODEM");
        }

        protected override async Task<bool> ExecuteActionAsync(string actionKey, string actionTitle)
        {
            switch (actionKey)
            {
                case "port":
                    await TogglePortAsync();
                    return IsPortOpen;

                case "info":
                    return await ReadInfoAsync();

                case "imei":
                    return await ReadImeiAsync();

                case "sim":
                    return await ReadSimAsync();

                case "signal":
                    return await ReadSignalAsync();

                case "diag":
                    return await EnterDiagModeAsync();

                default:
                    LogHub.Instance.Warn("Bilinmeyen modem işlemi: " + actionKey, "MODEM");
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
                LogHub.Instance.Info("AT portu kapatıldı.", "MODEM");
                return;
            }

            if (SelectedPort == null)
            {
                LogHub.Instance.Warn("Port seçilmedi.", "MODEM");
                return;
            }

            if (_diag.IsOpen && string.Equals(_diag.PortName, SelectedPort.Name, StringComparison.OrdinalIgnoreCase))
            {
                _diag.Close();
                DiagStateText = "Kapalı";
            }

            StatusText = "AT portu açılıyor: " + SelectedPort.Name;
            var opened = await _at.OpenAsync(SelectedPort.Name, SelectedBaud);

            IsPortOpen = opened;
            PortStateText = opened
                ? SelectedPort.Name + " · " + SelectedBaud + " · açık"
                : "Açılamadı";

            StatusText = PortStateText;
        }

        private async Task SendAtAsync(string commandOverride)
        {
            var command = string.IsNullOrWhiteSpace(commandOverride)
                ? (CommandText ?? string.Empty).Trim()
                : commandOverride.Trim();

            if (command.Length == 0)
            {
                LogHub.Instance.Warn("AT komutu boş.", "MODEM");
                return;
            }

            if (!_at.IsOpen)
            {
                LogHub.Instance.Warn("AT portu açık değil.", "MODEM");
                return;
            }

            StatusText = command + " gönderiliyor...";
            var response = await _at.SendAsync(command, TimeSpan.FromSeconds(8));

            if (response.Lines.Count > 0)
            {
                LogHub.Instance.Info(command + " → " + response.Text, "MODEM");
            }

            StatusText = command + " → " + (response.Success ? "OK" : response.Final);

            if (!response.Success)
            {
                LogHub.Instance.Warn(command + " → " + response.Final, "MODEM");
            }
        }

        private async Task<bool> ReadInfoAsync()
        {
            if (!_at.IsOpen)
            {
                if (await ReadInfoViaAdbAsync())
                {
                    return true;
                }

                LogHub.Instance.Warn(
                    "AT portu açık değil ve ADB cihazı yok. Modem portunu açın veya telefonu ADB ile bağlayın.",
                    "MODEM");
                return false;
            }

            StatusText = "Modem bilgisi okunuyor...";
            var info = await _modem.ReadAllAsync();
            Apply(info);

            LogHub.Instance.Ok(
                "IMEI " + info.Imei + " · model " + info.Model + " · sinyal " + info.SignalText, "MODEM");

            StatusText = "Modem bilgisi okundu";
            return true;
        }

        /// <summary>AT portu yokken model/IMEI/firmware bilgisini ADB getprop ile doldurur.</summary>
        private async Task<bool> ReadInfoViaAdbAsync()
        {
            if (!Toolchain.Instance.HasAdb)
            {
                return false;
            }

            try
            {
                var devices = await _adb.ListDevicesAsync();
                var online = devices.FirstOrDefault(d => d.IsOnline);

                if (online == null)
                {
                    return false;
                }

                _adb.Serial = online.Serial;
                var props = await _adb.GetPropsAsync();

                string P(params string[] keys)
                {
                    foreach (var key in keys)
                    {
                        if (props.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                        {
                            return value.Trim();
                        }
                    }

                    return "—";
                }

                var imei = ModemService.OnlyDigits(
                    P("ro.ril.miui.imei0", "ro.ril.oem.imei", "ro.vendor.oem.imei"));

                InfoFields[0].Value = imei.Length == 15
                    ? imei + (ModemService.IsValidImei(imei) ? "  · geçerli" : "  · geçersiz (Luhn)")
                    : "—";
                InfoFields[1].Value = P("ro.product.model");
                InfoFields[2].Value = P("ro.build.display.id", "ro.build.id");
                InfoFields[3].Value = "—";
                InfoFields[4].Value = "ADB (AT portu yok)";
                InfoFields[5].Value = P("gsm.operator.alpha", "gsm.sim.operator.alpha");
                InfoFields[6].Value = "—";
                InfoFields[7].Value = "—";
                InfoFields[8].Value = "—";

                LogHub.Instance.Ok(
                    "Modem bilgisi ADB ile okundu (AT portu yok) · model " + InfoFields[1].Value +
                    " · IMEI " + (imei.Length == 15 ? "okundu" : "alınamadı"), "MODEM");

                StatusText = "Bilgi ADB ile okundu";
                return true;
            }
            catch (Exception ex)
            {
                LogHub.Instance.Warn("ADB ile modem bilgisi alınamadı: " + ex.Message, "MODEM");
                return false;
            }
        }

        /// <summary>AT portu yokken IMEI'yi ADB getprop anahtarlarından okur.</summary>
        private async Task<string> ReadImeiViaAdbAsync()
        {
            if (!Toolchain.Instance.HasAdb)
            {
                return null;
            }

            try
            {
                var devices = await _adb.ListDevicesAsync();
                var online = devices.FirstOrDefault(d => d.IsOnline);

                if (online == null)
                {
                    return null;
                }

                _adb.Serial = online.Serial;
                var props = await _adb.GetPropsAsync();

                var keys = new[]
                {
                    "ro.ril.miui.imei0", "ro.ril.miui.imei1",
                    "ro.ril.oem.imei", "ro.ril.oem.imei1",
                    "ro.vendor.oem.imei", "persist.radio.imei"
                };

                foreach (var key in keys)
                {
                    if (!props.TryGetValue(key, out var value))
                    {
                        continue;
                    }

                    var digits = ModemService.OnlyDigits(value);

                    if (digits.Length == 15)
                    {
                        return digits;
                    }
                }

                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private async Task AutoDetectPortAsync()
        {
            if (Ports.Count == 0)
            {
                LogHub.Instance.Warn(
                    "Hiç COM portu yok. Cihazı modem/diag moduna alın veya USB sürücüsünü kurun.", "MODEM");
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
                LogHub.Instance.Ok("AT portu bulundu: " + option.Name, "MODEM");
                return;
            }

            StatusText = "AT portu bulunamadı";
            LogHub.Instance.Warn("Hiçbir COM portu AT komutuna yanıt vermedi.", "MODEM");
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

        private async Task<bool> ReadImeiAsync()
        {
            if (!_at.IsOpen)
            {
                var adbImei = await ReadImeiViaAdbAsync();

                if (adbImei == null)
                {
                    LogHub.Instance.Warn("AT portu açık değil ve ADB ile IMEI alınamadı.", "MODEM");
                    return false;
                }

                var adbValid = ModemService.IsValidImei(adbImei);
                InfoFields[0].Value = adbImei + (adbValid ? "  · geçerli" : "  · geçersiz (Luhn)");
                LogHub.Instance.Ok(
                    "IMEI (ADB): " + adbImei + (adbValid ? " · Luhn geçerli" : " · Luhn hatası"), "MODEM");
                StatusText = "IMEI: " + adbImei + " (ADB)";
                return true;
            }

            StatusText = "IMEI okunuyor...";
            var imei = await _modem.ReadImeiAsync();

            if (string.IsNullOrWhiteSpace(imei))
            {
                InfoFields[0].Value = "—";
                StatusText = "IMEI alınamadı";
                LogHub.Instance.Warn("IMEI alınamadı (AT+CGSN / AT+GSN yanıtsız).", "MODEM");
                return false;
            }

            var valid = ModemService.IsValidImei(imei);
            InfoFields[0].Value = imei + (valid ? "  · geçerli" : "  · geçersiz (Luhn)");

            LogHub.Instance.Ok("IMEI: " + imei + (valid ? " (Luhn geçerli)" : " (Luhn hatası!)"), "MODEM");
            StatusText = "IMEI: " + imei;
            return true;
        }

        private async Task<bool> ReadSimAsync()
        {
            if (!_at.IsOpen)
            {
                LogHub.Instance.Warn("AT portu açık değil.", "MODEM");
                return false;
            }

            StatusText = "SIM bilgisi okunuyor...";

            InfoFields[4].Value = await _modem.ReadSimStateAsync() ?? "—";
            InfoFields[5].Value = await _modem.ReadOperatorAsync() ?? "—";
            InfoFields[6].Value = await _modem.ReadRegistrationAsync() ?? "—";
            InfoFields[3].Value = await _modem.ReadImsiAsync() ?? "—";

            LogHub.Instance.Ok(
                "SIM: " + InfoFields[4].Value + " · operatör: " + InfoFields[5].Value +
                " · şebeke: " + InfoFields[6].Value, "MODEM");

            StatusText = "SIM durumu okundu";
            return true;
        }

        private async Task<bool> ReadSignalAsync()
        {
            if (!_at.IsOpen)
            {
                if (await ReadSignalViaAdbAsync())
                {
                    return true;
                }

                LogHub.Instance.Warn(
                    "Sinyal okunamadı: modem (AT) portu yok ve ADB telephony verisi alınamadı. " +
                    "Cihazı modem/diag moduna alın.", "MODEM");
                StatusText = "Sinyal alınamadı (AT/ADB)";
                return false;
            }

            StatusText = "Sinyal okunuyor...";
            var rssi = await _modem.ReadSignalAsync();

            if (rssi < 0)
            {
                InfoFields[7].Value = "—";
                StatusText = "Sinyal alınamadı";
                return false;
            }

            var dbm = rssi == 99 ? 0 : -113 + (2 * rssi);
            InfoFields[7].Value = rssi + " / 31 · " + dbm + " dBm";

            LogHub.Instance.Ok("Sinyal: " + InfoFields[7].Value, "MODEM");
            StatusText = InfoFields[7].Value;
            return true;
        }

        /// <summary>AT portu yokken sinyali ADB `dumpsys telephony.registry` ile okur.</summary>
        private async Task<bool> ReadSignalViaAdbAsync()
        {
            try
            {
                var devices = await _adb.ListDevicesAsync();
                var online = devices.FirstOrDefault(d => d.IsOnline);

                if (online == null)
                {
                    return false;
                }

                StatusText = "Sinyal okunuyor (ADB)...";
                _adb.Serial = online.Serial;

                var snapshot = await TelephonyReader.ReadAsync(_adb);

                if (snapshot == null)
                {
                    return false;
                }

                InfoFields[7].Value = snapshot.Text;
                StatusText = "Sinyal: " + snapshot.Text;

                LogHub.Instance.Ok(
                    "Sinyal (ADB): " + snapshot.Text + " · operatör " + snapshot.Operator +
                    " · " + snapshot.Technology, "MODEM");

                return true;
            }
            catch (Exception ex)
            {
                LogHub.Instance.Warn("ADB sinyal okuma hatası: " + ex.Message, "MODEM");
                return false;
            }
        }

        private async Task<bool> EnterDiagModeAsync()
        {
            if (!_at.IsOpen)
            {
                LogHub.Instance.Warn("Diag modu için önce AT portunu açın.", "MODEM");
                return false;
            }

            StatusText = "Diag modu deneniyor...";
            var accepted = await _diag.TryEnterDiagModeAsync(_at);

            if (accepted == null)
            {
                StatusText = "Diag modu açılamadı";
                return false;
            }

            StatusText = "Diag modu: " + accepted;
            RefreshPorts();
            return true;
        }

        private void ToggleDiag()
        {
            if (_diag.IsOpen)
            {
                _diag.Close();
                DiagStateText = "Kapalı";
                CommandManager.InvalidateRequerySuggested();
                return;
            }

            if (DiagPort == null)
            {
                LogHub.Instance.Warn("Diag portu seçilmedi.", "DIAG");
                return;
            }

            try
            {
                _diag.Open(DiagPort.Name, SelectedDiagBaud);
                DiagStateText = DiagPort.Name + " · " + SelectedDiagBaud + " · açık";
                StatusText = "Diag portu açıldı";
            }
            catch (Exception ex)
            {
                LogHub.Instance.Error("Diag portu açılamadı: " + ex.Message, "DIAG");
                DiagStateText = "Açılamadı";
            }

            CommandManager.InvalidateRequerySuggested();
        }

        private async Task SendHexAsync()
        {
            try
            {
                var count = await _diag.SendHexAsync(DiagHexText);
                StatusText = count + " bayt diag portuna gönderildi";
            }
            catch (Exception ex)
            {
                LogHub.Instance.Error("Diag gönderimi başarısız: " + ex.Message, "DIAG");
                StatusText = "Gönderim hatası";
            }
        }

        private async Task BrowseQcnAsync()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "QCN dosyası seç",
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

                // Büyük dosyalarda UI donmasın: okuma ve analiz arka planda.
                var document = await Task.Run(() => QcnDocument.Load(dialog.FileName));

                _qcn = document;
                QcnPath = dialog.FileName;
                QcnSummary = document.Size.ToString("N0") + " bayt · SHA-256 " + ShortHash(document.Sha256);
                QcnHints = string.Join(Environment.NewLine, document.Hints);
                QcnMatches.Clear();
                SelectedMatch = null;
                StatusText = "QCN yüklendi: " + document.FileName;
            }
            catch (Exception ex)
            {
                LogHub.Instance.Error("QCN yüklenemedi: " + ex.Message, "QCN");
                StatusText = "QCN yüklenemedi";
            }

            CommandManager.InvalidateRequerySuggested();
        }

        private async Task SearchImeiAsync()
        {
            if (_qcn == null)
            {
                return;
            }

            var imei = ModemService.OnlyDigits(QcnSearchImei);

            if (imei.Length != 15)
            {
                LogHub.Instance.Warn("Aranacak IMEI 15 hane olmalı.", "QCN");
                return;
            }

            StatusText = "QCN içinde aranıyor...";

            var matches = await Task.Run(() => _qcn.FindImei(imei));

            QcnMatches.Clear();
            foreach (var match in matches)
            {
                QcnMatches.Add(match);
            }

            SelectedMatch = matches.FirstOrDefault();
            NewImei = string.IsNullOrWhiteSpace(NewImei) ? imei : NewImei;

            QcnSummary = matches.Count == 0
                ? "Bu IMEI dosyada bulunamadı"
                : matches.Count + " eşleşme (ASCII/BCD)";

            LogHub.Instance.Info(
                "QCN içinde " + matches.Count + " eşleşme bulundu: " +
                string.Join(", ", matches.Select(m => m.Kind + "@" + m.OffsetText)), "QCN");

            StatusText = matches.Count + " eşleşme";
        }

        private async Task ApplyImeiAsync()
        {
            if (_qcn == null || SelectedMatch == null)
            {
                return;
            }

            var match = SelectedMatch;
            var newImei = NewImei;

            var result = await Task.Run(() =>
            {
                var success = _qcn.ReplaceImei(match, newImei, out var error);
                return (success, error);
            });

            if (result.success)
            {
                QcnSummary = "Değişiklik hazır — kaydetmek için 'Yedekli Kaydet'";
                LogHub.Instance.Ok(
                    "IMEI güncellendi (" + match.Kind + " @ " + match.OffsetText +
                    "). Kaydetmeden önce yedek alınacak.", "QCN");
                StatusText = "QCN değişikliği hazır";
                CommandManager.InvalidateRequerySuggested();
            }
            else
            {
                LogHub.Instance.Error("Değişiklik uygulanamadı: " + result.error, "QCN");
            }
        }

        private async Task SaveQcnAsync()
        {
            if (_qcn == null)
            {
                return;
            }

            try
            {
                StatusText = "QCN kaydediliyor...";
                var backup = await Task.Run(() => _qcn.Save());

                QcnSummary = backup == null
                    ? "Değişiklik yok"
                    : "Kaydedildi · yedek: " + Path.GetFileName(backup);

                StatusText = "QCN kaydedildi";
                CommandManager.InvalidateRequerySuggested();
            }
            catch (Exception ex)
            {
                LogHub.Instance.Error("QCN kaydedilemedi: " + ex.Message, "QCN");
                StatusText = "Kaydetme hatası";
            }
        }

        private void Apply(ModemInfo info)
        {
            InfoFields[0].Value = info.Imei == "—"
                ? "—"
                : info.Imei + (info.ImeiValid ? "  · geçerli" : "  · geçersiz (Luhn)");

            InfoFields[1].Value = info.Model;
            InfoFields[2].Value = info.Firmware;
            InfoFields[3].Value = info.Imsi;
            InfoFields[4].Value = info.SimState;
            InfoFields[5].Value = info.Operator;
            InfoFields[6].Value = info.Registration;
            InfoFields[7].Value = info.SignalText;
            InfoFields[8].Value = info.Battery;
        }

        private static string ShortHash(string hash)
        {
            if (string.IsNullOrWhiteSpace(hash) || hash.Length < 12)
            {
                return "—";
            }

            return hash.Substring(0, 12) + "…";
        }

        protected override string GetActionTitle(string actionKey)
        {
            switch (actionKey)
            {
                case "port": return "Port Bağlantısı";
                case "info": return "Bilgi Oku";
                case "imei": return "IMEI Oku";
                case "sim": return "SIM Durumu";
                case "signal": return "Sinyal";
                case "diag": return "Diag Modu";
                default: return "Modem işlemi";
            }
        }

        protected override IReadOnlyList<string> GetSteps(string actionKey)
        {
            switch (actionKey)
            {
                case "port":
                    return Steps("Seri port açılıyor...", "AT el sıkışması...", "Yankı kapatılıyor...");

                case "info":
                    return Steps("AT+CGSN...", "AT+CGMM...", "AT+CGMR...", "AT+CPIN?...", "AT+CSQ...");

                case "imei":
                    return Steps("IMEI isteniyor...", "Luhn doğrulaması yapılıyor...");

                case "sim":
                    return Steps("CPIN sorgulanıyor...", "COPS okunuyor...", "CREG kontrol ediliyor...");

                case "signal":
                    return Steps("AT+CSQ gönderiliyor...", "dBm hesaplanıyor...");

                case "diag":
                    return Steps("AT+QCDM deneniyor...", "AT$QCDMG deneniyor...", "Port listesi yenileniyor...");

                default:
                    return Steps("İşlem hazırlanıyor...", "Tamamlandı.");
            }
        }
    }
}
