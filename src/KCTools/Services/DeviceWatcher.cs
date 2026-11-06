using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using KCTools.Models;

namespace KCTools.Services
{
    public sealed class DeviceChangedEventArgs : EventArgs
    {
        public DeviceChangedEventArgs(DetectedDevice device)
        {
            Device = device;
        }

        public DetectedDevice Device { get; }
    }

    public sealed class DeviceWatcherErrorEventArgs : EventArgs
    {
        public DeviceWatcherErrorEventArgs(string message)
        {
            Message = message;
        }

        public string Message { get; }
    }

    /// <summary>
    /// USB / COM değişimlerini <see cref="ManagementEventWatcher"/> ile anlık izler.
    ///
    /// Tasarım:
    ///  • __InstanceCreationEvent / __InstanceDeletionEvent + TargetInstance ISA 'Win32_PnPEntity'
    ///    olayları yakalanır, 400 ms debounce ile tek bir taramaya indirgenir.
    ///  • Her tarama Win32_PnPEntity envanterini çıkarır, registry COM listesiyle birleştirir
    ///    ve önceki anlık görüntüyle fark alır.
    ///  • Fark: DeviceConnected / DeviceDisconnected + DevicesChanged olayları.
    ///  • WMI olayları kaçarsa 15 sn'lik güvenlik taraması devreye girer; WMI hiç açılamazsa
    ///    3 sn'lik periyodik taramaya düşer.
    ///  • Olaylar, nesnenin oluşturulduğu SynchronizationContext'e post edilir; böylece
    ///    UI koleksiyonları doğru iş parçacığında güncellenir.
    /// </summary>
    public sealed class DeviceWatcher : IDisposable
    {
        public const int DebounceMilliseconds = 400;
        public const int SafetyScanMilliseconds = 15000;
        public const int FallbackScanMilliseconds = 3000;

        private readonly object _gate = new object();
        private readonly SynchronizationContext _context;
        private readonly Dispatcher _dispatcher;
        private readonly Timer _debounceTimer;
        private readonly Timer _safetyTimer;
        private readonly Dictionary<string, DetectedDevice> _known =
            new Dictionary<string, DetectedDevice>(StringComparer.OrdinalIgnoreCase);

        private IReadOnlyList<DetectedDevice> _devices = Array.Empty<DetectedDevice>();
        private ManagementEventWatcher _creationWatcher;
        private ManagementEventWatcher _deletionWatcher;
        private DateTime _lastErrorLog = DateTime.MinValue;
        private int _scanning;
        private bool _disposed;

        public DeviceWatcher()
        {
            _context = SynchronizationContext.Current;

            // WPF'te SynchronizationContext her zaman hazır olmayabilir; Dispatcher yedeği.
            if (_context == null && Application.Current != null)
            {
                _dispatcher = Application.Current.Dispatcher;
            }

            _debounceTimer = new Timer(OnDebounceElapsed, null, Timeout.Infinite, Timeout.Infinite);
            _safetyTimer = new Timer(OnSafetyTick, null, Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>Yeni bir aygıt bağlandı (COM4 Connected (Qualcomm EDL)).</summary>
        public event EventHandler<DeviceChangedEventArgs> DeviceConnected;

        /// <summary>Aygıt ayrıldı; olaydaki kaydın IsConnected değeri false'dır.</summary>
        public event EventHandler<DeviceChangedEventArgs> DeviceDisconnected;

        /// <summary>Liste içeriği değişti (ekleme, kaldırma veya ad/port güncellemesi).</summary>
        public event EventHandler DevicesChanged;

        /// <summary>WMI/tarama hatası; arayüz loglaması için.</summary>
        public event EventHandler<DeviceWatcherErrorEventArgs> Error;

        public bool IsRunning { get; private set; }

        /// <summary>ManagementEventWatcher'lar aktif mi? Değilse periyodik tarama kullanılır.</summary>
        public bool WmiActive => _creationWatcher != null && _deletionWatcher != null;

        public DateTime LastScanTime { get; private set; }

        /// <summary>Bilinmeyen tüm USB aygıtlarını da listeye kat (varsayılan: kapalı).</summary>
        public bool IncludeAllUsb { get; set; }

        public IReadOnlyList<DetectedDevice> Devices
        {
            get
            {
                lock (_gate)
                {
                    return _devices;
                }
            }
        }

        public void Start()
        {
            if (_disposed || IsRunning)
            {
                return;
            }

            IsRunning = true;
            TryStartWmi();

            var interval = WmiActive ? SafetyScanMilliseconds : FallbackScanMilliseconds;
            _safetyTimer.Change(interval, interval);

            LogHub.Instance.Info(
                "Cihaz izleyici başlatıldı (" +
                (WmiActive ? "WMI olayları + 15 sn güvenlik taraması" : "periyodik tarama") + ").",
                "CİHAZ");

            // İlk tarama arka planda: pencere açılışı WMI gecikmesinden etkilenmesin.
            ThreadPool.QueueUserWorkItem(_ => RefreshNow());
        }

        public void Stop()
        {
            if (!IsRunning)
            {
                return;
            }

            IsRunning = false;
            _debounceTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _safetyTimer.Change(Timeout.Infinite, Timeout.Infinite);
            StopWmi();
            LogHub.Instance.Info("Cihaz izleyici durduruldu.", "CİHAZ");
        }

        /// <summary>Anında yeniden tarama (arayüzdeki "Yenile" düğmesi).</summary>
        public void RefreshNow()
        {
            if (_disposed || !IsRunning || Interlocked.Exchange(ref _scanning, 1) == 1)
            {
                return;
            }

            try
            {
                List<DetectedDevice> scanned;

                try
                {
                    scanned = WmiDeviceScanner.Scan(IncludeAllUsb);
                }
                catch (Exception ex)
                {
                    ReportError("WMI taraması başarısız: " + ex.Message);
                    scanned = new List<DetectedDevice>();
                }

                MergeRegistryPorts(scanned);
                Apply(scanned);
            }
            finally
            {
                Interlocked.Exchange(ref _scanning, 0);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Stop();
            _debounceTimer.Dispose();
            _safetyTimer.Dispose();
        }

        // ---------------- WMI ----------------

        private void TryStartWmi()
        {
            try
            {
                var scope = new ManagementScope(@"\\.\root\cimv2");
                scope.Connect();

                _creationWatcher = CreateWatcher(scope, "__InstanceCreationEvent");
                _deletionWatcher = CreateWatcher(scope, "__InstanceDeletionEvent");

                _creationWatcher.Start();
                _deletionWatcher.Start();
            }
            catch (Exception ex)
            {
                StopWmi();
                LogHub.Instance.Warn(
                    "WMI izleyici başlatılamadı, periyodik taramaya geçildi: " + ex.Message, "CİHAZ");
            }
        }

        private ManagementEventWatcher CreateWatcher(ManagementScope scope, string eventClass)
        {
            var query = new WqlEventQuery(
                eventClass,
                TimeSpan.FromSeconds(2),
                "TargetInstance ISA 'Win32_PnPEntity'");

            var watcher = new ManagementEventWatcher(scope, query);
            watcher.EventArrived += OnWmiEventArrived;
            return watcher;
        }

        private void StopWmi()
        {
            StopWatcher(ref _creationWatcher);
            StopWatcher(ref _deletionWatcher);
        }

        private void StopWatcher(ref ManagementEventWatcher watcher)
        {
            if (watcher == null)
            {
                return;
            }

            try
            {
                watcher.EventArrived -= OnWmiEventArrived;
                watcher.Stop();
            }
            catch (Exception)
            {
                // Kapanışta WMI hataları yok sayılır.
            }
            finally
            {
                try
                {
                    watcher.Dispose();
                }
                catch (Exception)
                {
                    // yok sayılır
                }

                watcher = null;
            }
        }

        private void OnWmiEventArrived(object sender, EventArrivedEventArgs e)
        {
            // Olay içeriği (TargetInstance) kullanılmıyor: tek bir tam tarama,
            // art arda gelen ekleme/silme olaylarını doğru şekilde birleştirir.
            ScheduleRefresh();
        }

        private void ScheduleRefresh()
        {
            if (_disposed || !IsRunning)
            {
                return;
            }

            try
            {
                _debounceTimer.Change(DebounceMilliseconds, Timeout.Infinite);
            }
            catch (ObjectDisposedException)
            {
                // kapanış yarışı
            }
        }

        private void OnDebounceElapsed(object state)
        {
            RefreshNow();
        }

        private void OnSafetyTick(object state)
        {
            RefreshNow();
        }

        // ---------------- Tarama / fark ----------------

        private static void MergeRegistryPorts(List<DetectedDevice> scanned)
        {
            try
            {
                foreach (var port in PortWatcher.Scan())
                {
                    var exists = scanned.Any(d =>
                        string.Equals(d.Port, port.Name, StringComparison.OrdinalIgnoreCase));

                    if (exists)
                    {
                        continue;
                    }

                    scanned.Add(new DetectedDevice
                    {
                        Key = "REG:" + port.Name,
                        InstanceId = port.Device,
                        Name = port.FriendlyName + " (" + port.Name + ")",
                        Kind = DeviceKind.Serial,
                        Port = port.Name,
                        IsConnected = true,
                        Source = "registry"
                    });
                }
            }
            catch (Exception)
            {
                // Registry erişilemezse yalnızca WMI sonuçları kullanılır.
            }
        }

        private void Apply(List<DetectedDevice> scanned)
        {
            List<DetectedDevice> added;
            List<DetectedDevice> removed;
            bool metadataChanged = false;

            lock (_gate)
            {
                var next = new Dictionary<string, DetectedDevice>(StringComparer.OrdinalIgnoreCase);
                foreach (var device in scanned)
                {
                    if (!string.IsNullOrWhiteSpace(device.Key) && !next.ContainsKey(device.Key))
                    {
                        next[device.Key] = device;
                    }
                }

                added = next.Values.Where(d => !_known.ContainsKey(d.Key)).ToList();
                removed = _known.Values.Where(d => !next.ContainsKey(d.Key)).ToList();

                foreach (var pair in next)
                {
                    if (!_known.TryGetValue(pair.Key, out var previous))
                    {
                        continue;
                    }

                    if (!string.Equals(previous.Name, pair.Value.Name, StringComparison.Ordinal) ||
                        !string.Equals(previous.Port, pair.Value.Port, StringComparison.Ordinal) ||
                        !string.Equals(previous.Status, pair.Value.Status, StringComparison.Ordinal))
                    {
                        metadataChanged = true;
                    }
                }

                _known.Clear();
                foreach (var pair in next)
                {
                    _known[pair.Key] = pair.Value;
                }

                _devices = next.Values
                    .OrderBy(d => d.SortOrder)
                    .ThenBy(d => d.Port ?? "ZZZ", StringComparer.OrdinalIgnoreCase)
                    .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                LastScanTime = DateTime.Now;
            }

            foreach (var device in removed)
            {
                var snapshot = device.AsDisconnected();
                Raise(() => DeviceDisconnected?.Invoke(this, new DeviceChangedEventArgs(snapshot)));
            }

            foreach (var device in added)
            {
                Raise(() => DeviceConnected?.Invoke(this, new DeviceChangedEventArgs(device)));
            }

            if (added.Count > 0 || removed.Count > 0 || metadataChanged)
            {
                Raise(() => DevicesChanged?.Invoke(this, EventArgs.Empty));
            }
        }

        private void ReportError(string message)
        {
            Raise(() => Error?.Invoke(this, new DeviceWatcherErrorEventArgs(message)));

            if ((DateTime.Now - _lastErrorLog).TotalSeconds < 30)
            {
                return;
            }

            _lastErrorLog = DateTime.Now;
            LogHub.Instance.Warn(message, "CİHAZ");
        }

        /// <summary>Olayı yakalandığı UI bağlamına aktarır.</summary>
        private void Raise(Action action)
        {
            var context = _context;
            if (context != null)
            {
                try
                {
                    context.Post(_ => InvokeSafely(action), null);
                    return;
                }
                catch (InvalidOperationException)
                {
                    // bağlam kapanmış; aşağıdaki yollara düşülür
                }
            }

            var dispatcher = _dispatcher;
            if (dispatcher != null)
            {
                try
                {
                    dispatcher.BeginInvoke(new Action(() => InvokeSafely(action)));
                    return;
                }
                catch (InvalidOperationException)
                {
                    // dispatcher kapanmış
                }
            }

            InvokeSafely(action);
        }

        private static void InvokeSafely(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                LogHub.Instance.Error("Cihaz olayı işlenemedi: " + ex.Message, "CİHAZ");
            }
        }
    }
}
