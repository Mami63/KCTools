using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using KCTools.Common;
using KCTools.Models;
using KCTools.Services;

namespace KCTools.ViewModels
{
    /// <summary>
    /// Ana pencere: navigasyon, canlı cihaz listesi, oturum yönetimi ve durum çubuğu.
    /// Cihaz algılama <see cref="DeviceWatcher"/> tarafından arka planda yürütülür;
    /// olaylar UI iş parçacığına post edilir.
    /// </summary>
    public sealed class MainViewModel : ObservableObject, IDisposable
    {
        private readonly QualcommViewModel _qualcomm;
        private readonly MtkViewModel _mtk;
        private readonly FastbootViewModel _fastboot;
        private readonly AdbViewModel _adb;
        private readonly ModemViewModel _modem;
        private readonly ImeiViewModel _imei;
        private readonly DeviceViewModel _device;
        private readonly CatalogViewModel _catalog;
        private readonly RootViewModel _root;
        private readonly SettingsViewModel _settings;
        private readonly DeviceWatcher _watcher;
        private readonly PhoneProfileService _phoneProfileService = new PhoneProfileService();
        private IDisposable _navSubscription;

        private PhoneProfile _phoneProfile;
        private bool _profileLoading;

        private NavItem _selectedNav;
        private object _currentPage;
        private DetectedDevice _selectedDevice;
        private DetectedDevice _connectedDevice;
        private bool _isConnected;
        private bool _isConnecting;
        private bool _disposed;

        public MainViewModel()
        {
            Toolchain.Instance.Refresh(false);

            _qualcomm = new QualcommViewModel();
            _mtk = new MtkViewModel();
            _fastboot = new FastbootViewModel();
            _adb = new AdbViewModel();
            _modem = new ModemViewModel();
            _imei = new ImeiViewModel();
            _device = new DeviceViewModel();
            _catalog = new CatalogViewModel();
            _root = new RootViewModel();
            _settings = new SettingsViewModel();

            NavItems = new ObservableCollection<NavItem>
            {
                new NavItem("device", "Cihaz", "Bilgi · İşlemler",
                    "Bağlı telefonun bilgileri, format/recovery/yedekleme/flash işlemleri",
                    "Icon.Chip", _device),
                new NavItem("catalog", "Katalog", "Marka · Model",
                    "Marka/model veritabanı, codename ve platform bilgisi, otomatik eşleştirme",
                    "Icon.Layers", _catalog),                new NavItem("root", "Root", "Magisk · Boot",
                    "Tek tık Magisk kurulumu, yamalı boot flash ve root kaldırma — exploit yok",
                    "Icon.Shield", _root),
                new NavItem("qualcomm", "Qualcomm", "EDL · Firehose",
                    "EDL 9008, Firehose programlayıcı, FRP ve yedekleme iş akışları",
                    "Icon.Chip", _qualcomm),
                new NavItem("mtk", "MediaTek", "BROM · Scatter",
                    "BROM/Preloader modu, scatter tabanlı yazma ve NVRAM yedekleri",
                    "Icon.Layers", _mtk),
                new NavItem("fastboot", "Fastboot", "Bootloader",
                    "Bootloader kilidi, slot yönetimi ve ham komut konsolu",
                    "Icon.Bolt", _fastboot),
                new NavItem("adb", "ADB", "Cihaz · Kabuk",
                    "Cihaz listesi, paket kurulumu ve kabuk komutları",
                    "Icon.Terminal", _adb),
                new NavItem("modem", "Modem", "AT · DIAG · QCN",
                    "AT komutları, diag portu ve QCN yedek/düzenleme",
                    "Icon.Plug", _modem),
                new NavItem("imei", "IMEI Onarımı", "Luhn · QCN",
                    "IMEI okuma, doğrulama ve QCN üzerinde yedekli onarım",
                    "Icon.Shield", _imei),
                new NavItem("settings", "Ayarlar", "Uygulama",
                    "Görünüm, günlük ve lisans tercihleri",
                    "Icon.Sliders", _settings)
            };

            Devices = new ObservableCollection<DetectedDevice>();

            _selectedNav = NavItems[0];
            _currentPage = _device;

            _navSubscription = EventAggregator.Instance.Subscribe<NavigationRequestedEvent>(
                OnNavigationRequested);

            _watcher = new DeviceWatcher();
            _watcher.DeviceConnected += OnDeviceConnected;
            _watcher.DeviceDisconnected += OnDeviceDisconnected;
            _watcher.DevicesChanged += OnDevicesChanged;

            RefreshDevicesCommand = new RelayCommand(_ => _watcher.RefreshNow(), _ => !IsConnecting);
            ConnectCommand = new RelayCommand(
                async _ => await ConnectAsync(),
                _ => !IsConnecting && (IsConnected || SelectedDevice != null));

            RecoveryCommand = new RelayCommand(async _ => await EnterRecoveryAsync(), _ => !IsConnecting);

            LogHub.Instance.Rule("KC TOOLS " + AppSettings.Version + " · CİHAZ SERVİSİ");
            LogHub.Instance.Info("Android Servis İstasyonu başlatıldı.", "SİSTEM");
            LogHub.Instance.Info($"Lisans: {License.Summary} — {License.Detail}", "LİSANS");
            LogHub.Instance.Info(
                "Araçlar — adb: " + (Toolchain.Instance.HasAdb ? "hazır" : "yok") +
                " · fastboot: " + (Toolchain.Instance.HasFastboot ? "hazır" : "yok"), "ARAÇ");

            _watcher.Start();
            DeviceMonitor.Instance.Start();
            SyncDevices();

            _ = Toolchain.Instance.RefreshVersionsAsync();
            _ = InitializeLicenseAsync();
            _ = EnvironmentCheckService.Instance.RunAsync();

            LogHub.Instance.Info("Hazır. USB cihazı bağlandığında otomatik algılanır.", "SİSTEM");
        }

        public LogHub Log => LogHub.Instance;

        public LicenseService License => LicenseService.Instance;

        public ObservableCollection<NavItem> NavItems { get; }

        /// <summary>Canlı algılama listesi (UI iş parçacığında güncellenir).</summary>
        public ObservableCollection<DetectedDevice> Devices { get; }

        public ICommand RefreshDevicesCommand { get; }

        public ICommand ConnectCommand { get; }

        public ICommand RecoveryCommand { get; }

        /// <summary>Bağlı telefondan ADB ile okunan model bilgisi (chip olarak gösterilir).</summary>
        public string PhoneProfileText => _phoneProfile?.Detail ?? "Model bekleniyor";

        public string PhoneProfileShortText => _phoneProfile?.ShortText ?? "Model bekleniyor";

        public bool HasPhoneProfile => _phoneProfile != null;

        public string VersionText => "v" + AppSettings.Version + " · " + AppSettings.Build;

        public NavItem SelectedNav
        {
            get => _selectedNav;
            set
            {
                if (!Set(ref _selectedNav, value) || value == null)
                {
                    return;
                }

                CurrentPage = value.View;
            }
        }

        public object CurrentPage
        {
            get => _currentPage;
            private set => Set(ref _currentPage, value);
        }

        public DetectedDevice SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                if (!Set(ref _selectedDevice, value))
                {
                    return;
                }

                OnPropertyChanged(nameof(StatusPortText));
                OnPropertyChanged(nameof(StatusModeText));
                OnPropertyChanged(nameof(StatusCaption));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool HasDevices => Devices.Count > 0;

        public bool IsDeviceListEmpty => Devices.Count == 0;

        public string DeviceCountText => Devices.Count == 0 ? "yok" : Devices.Count + " cihaz";

        /// <summary>Durum çubuğundaki yeşil nokta: cihaz takılı ya da oturum açık.</summary>
        public bool IsLinkAlive => IsConnected || HasDevices;

        public DetectedDevice ActiveDevice => _connectedDevice ?? SelectedDevice;

        public bool IsConnected
        {
            get => _isConnected;
            private set
            {
                if (!Set(ref _isConnected, value))
                {
                    return;
                }

                OnPropertyChanged(nameof(StatusPortText));
                OnPropertyChanged(nameof(StatusModeText));
                OnPropertyChanged(nameof(StatusCaption));
                OnPropertyChanged(nameof(ConnectButtonText));
                OnPropertyChanged(nameof(IsLinkAlive));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool IsConnecting
        {
            get => _isConnecting;
            private set
            {
                if (!Set(ref _isConnecting, value))
                {
                    return;
                }

                OnPropertyChanged(nameof(StatusCaption));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public string ConnectButtonText => IsConnected ? "Bağlantıyı Kes" : "Bağlan";

        public string StatusPortText
        {
            get
            {
                var device = ActiveDevice;
                return device == null ? "Port yok" : device.PortLabel + " · " + device.ModeLabel;
            }
        }

        public string StatusModeText => ActiveDevice?.ModeShort ?? "—";

        public string StatusCaption =>
            IsConnecting ? "Bağlanıyor..." :
            IsConnected ? "Bağlı" :
            HasDevices ? "Cihaz hazır" : "Cihaz yok";

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _watcher.DeviceConnected -= OnDeviceConnected;
            _watcher.DeviceDisconnected -= OnDeviceDisconnected;
            _watcher.DevicesChanged -= OnDevicesChanged;
            _watcher.Dispose();
            DeviceMonitor.Instance.Stop();
            _modem.Dispose();
            _imei.Dispose();
            _device.Dispose();
            _catalog.Dispose();
            _root.Dispose();
            _navSubscription?.Dispose();
        }

        /// <summary>HWID + kayıtlı oturumu yükler, lisansı arka planda doğrular.</summary>
        private async System.Threading.Tasks.Task InitializeLicenseAsync()
        {
            try
            {
                await License.InitializeAsync();
            }
            catch (Exception ex)
            {
                LogHub.Instance.Error("Lisans başlatılamadı: " + ex.Message, "LİSANS");
            }
        }

        // ---------------- cihaz olayları ----------------

        private void OnDeviceConnected(object sender, DeviceChangedEventArgs e)
        {
            LogHub.Instance.Ok(e.Device.Display, "CİHAZ");
            _modem.RefreshPorts();
            _ = TryLoadProfileAsync(e.Device);

            EventAggregator.Instance.Publish(new DeviceStateChangedEvent
            {
                Device = e.Device,
                Connected = true
            });
        }

        private void OnDeviceDisconnected(object sender, DeviceChangedEventArgs e)
        {
            LogHub.Instance.Warn(e.Device.Display, "CİHAZ");
            _modem.RefreshPorts();

            EventAggregator.Instance.Publish(new DeviceStateChangedEvent
            {
                Device = e.Device,
                Connected = false
            });
        }

        private void OnDevicesChanged(object sender, EventArgs e)
        {
            SyncDevices();
        }

        /// <summary>Watcher anlık görüntüsünü UI koleksiyonuna yansıtır, seçimi korur.</summary>
        private void SyncDevices()
        {
            if (_disposed)
            {
                return;
            }

            var selectedKey = SelectedDevice?.Key;
            var snapshot = _watcher.Devices;

            Devices.Clear();
            foreach (var device in snapshot)
            {
                Devices.Add(device);
            }

            OnPropertyChanged(nameof(HasDevices));
            OnPropertyChanged(nameof(IsDeviceListEmpty));
            OnPropertyChanged(nameof(DeviceCountText));

            SelectedDevice =
                Devices.FirstOrDefault(d => string.Equals(d.Key, selectedKey, StringComparison.OrdinalIgnoreCase))
                ?? Devices.FirstOrDefault();

            if (IsConnected && _connectedDevice != null &&
                !Devices.Any(d => string.Equals(d.Key, _connectedDevice.Key, StringComparison.OrdinalIgnoreCase)))
            {
                CloseSession("cihaz ayrıldı");
            }

            OnPropertyChanged(nameof(ActiveDevice));
            OnPropertyChanged(nameof(IsLinkAlive));
            OnPropertyChanged(nameof(StatusPortText));
            OnPropertyChanged(nameof(StatusModeText));
            OnPropertyChanged(nameof(StatusCaption));

            if (Devices.Count == 0)
            {
                ClearPhoneProfile();
            }
        }

        /// <summary>Bağlı telefondan model bilgisini okur (ADB; fastboot ise product).</summary>
        private async Task TryLoadProfileAsync(DetectedDevice device)
        {
            if (device == null || _profileLoading)
            {
                return;
            }

            if (device.Kind != DeviceKind.Adb &&
                device.Kind != DeviceKind.Android &&
                device.Kind != DeviceKind.Fastboot)
            {
                return;
            }

            _profileLoading = true;

            try
            {
                PhoneProfile profile = null;

                if (device.Kind == DeviceKind.Fastboot)
                {
                    var fastboot = new FastbootClient();
                    var serials = await fastboot.ListSerialsAsync();

                    if (serials.Count > 0)
                    {
                        fastboot.Serial = serials[0];
                        var info = await fastboot.ReadInfoAsync();

                        if (info != null && info.Product != "—")
                        {
                            profile = new PhoneProfile
                            {
                                Serial = info.Serial,
                                Model = info.Product,
                                Manufacturer = string.Empty,
                                Mode = "Fastboot",
                                Bootloader = info.BootloaderVersion
                            };
                        }
                    }
                }
                else
                {
                    profile = await _phoneProfileService.ReadFirstOnlineAsync();
                }

                if (profile == null)
                {
                    LogHub.Instance.Info(
                        "Model okunamadı — cihazda USB hata ayıklama/RSA onayı gerekli.", "CİHAZ");
                    return;
                }

                _phoneProfile = profile;
                OnPropertyChanged(nameof(PhoneProfileText));
                OnPropertyChanged(nameof(PhoneProfileShortText));
                OnPropertyChanged(nameof(HasPhoneProfile));

                LogHub.Instance.Ok("Model algılandı: " + profile.Detail, "CİHAZ");
            }
            catch (Exception ex)
            {
                LogHub.Instance.Warn("Model algılanamadı: " + ex.Message, "CİHAZ");
            }
            finally
            {
                _profileLoading = false;
            }
        }

        private void OnNavigationRequested(NavigationRequestedEvent e)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.Key))
            {
                return;
            }

            var item = NavItems.FirstOrDefault(
                n => string.Equals(n.Key, e.Key, StringComparison.OrdinalIgnoreCase));

            if (item != null)
            {
                SelectedNav = item;
            }
        }

        private void ClearPhoneProfile()
        {
            if (_phoneProfile == null)
            {
                return;
            }

            _phoneProfile = null;
            OnPropertyChanged(nameof(PhoneProfileText));
            OnPropertyChanged(nameof(PhoneProfileShortText));
            OnPropertyChanged(nameof(HasPhoneProfile));
        }

        /// <summary>Tek tık: ADB (yoksa fastboot) ile recovery moduna yeniden başlatır.</summary>
        private async Task EnterRecoveryAsync()
        {
            LogHub.Instance.Rule("HIZLI İŞLEM · RECOVERY");

            try
            {
                var adb = new AdbClient();
                var devices = await adb.ListDevicesAsync();
                var online = devices.FirstOrDefault(d => d.IsOnline);

                if (online != null)
                {
                    adb.Serial = online.Serial;
                    var result = await adb.RebootAsync("recovery");

                    if (result.Success)
                    {
                        LogHub.Instance.Ok(
                            "Recovery moduna geçiliyor: " + online.Serial, "ADB");
                        return;
                    }

                    LogHub.Instance.Warn(
                        "adb reboot recovery başarısız: " +
                        (string.IsNullOrWhiteSpace(result.StandardError)
                            ? "çıkış " + result.ExitCode
                            : result.StandardError.Trim()),
                        "ADB");
                }

                var fastboot = new FastbootClient();
                var serials = await fastboot.ListSerialsAsync();

                if (serials.Count > 0)
                {
                    fastboot.Serial = serials[0];
                    var result = await fastboot.RebootAsync("recovery");

                    if (result.Success)
                    {
                        LogHub.Instance.Ok("Fastboot ile recovery: " + serials[0], "FASTBOOT");
                        return;
                    }
                }

                LogHub.Instance.Warn(
                    "Recovery için ADB veya fastboot cihazı bulunamadı.", "CİHAZ");
            }
            catch (Exception ex)
            {
                LogHub.Instance.Error("Recovery hatası: " + ex.Message, "CİHAZ");
            }
        }

        private async Task ConnectAsync()
        {
            if (IsConnecting)
            {
                return;
            }

            if (IsConnected)
            {
                CloseSession("kullanıcı isteği");
                return;
            }

            var device = SelectedDevice;
            if (device == null)
            {
                LogHub.Instance.Error("Bağlanacak cihaz yok. USB cihazı bağlayın — algılama açık.", "CİHAZ");
                return;
            }

            IsConnecting = true;
            LogHub.Instance.Rule(device.ShortDisplay + " · OTURUM AÇILIYOR");

            try
            {
                await Task.Delay(180);
                LogHub.Instance.Step("Aygıt: " + device.Name, "CİHAZ");

                await Task.Delay(180);
                LogHub.Instance.Step("Örnek kimliği: " + (device.InstanceId ?? "—"), "CİHAZ");

                await Task.Delay(180);
                LogHub.Instance.Step(
                    "Servis: " + (device.Service ?? "—") +
                    " · VID/PID: " + (device.Vid ?? "—") + "/" + (device.Pid ?? "—"),
                    "CİHAZ");

                await Task.Delay(200);

                _connectedDevice = device;
                IsConnected = true;

                _qualcomm.OnDeviceAttached(device);
                _mtk.OnDeviceAttached(device);
                _fastboot.OnDeviceAttached(device);
                _adb.OnDeviceAttached(device);

                EventAggregator.Instance.Publish(new DeviceStateChangedEvent
                {
                    Device = device,
                    Connected = true
                });

                LogHub.Instance.Ok(device.Display, "CİHAZ");
                LogHub.Instance.Info("Oturum açıldı; cihaz bilgileri okunuyor.", "CİHAZ");
            }
            finally
            {
                IsConnecting = false;
            }
        }

        private void CloseSession(string reason)
        {
            var device = _connectedDevice;
            _connectedDevice = null;
            IsConnected = false;

            _qualcomm.OnDeviceDetached();
            _mtk.OnDeviceDetached();
            _fastboot.OnDeviceDetached();
            _adb.OnDeviceDetached();

            if (device != null)
            {
                LogHub.Instance.Warn(device.ShortDisplay + " oturumu kapatıldı (" + reason + ").", "CİHAZ");
            }
        }
    }
}
