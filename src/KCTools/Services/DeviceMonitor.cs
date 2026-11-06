using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KCTools.Common;
using KCTools.Models;

namespace KCTools.Services
{
    /// <summary>
    /// ADB ve Fastboot cihazlarını periyodik olarak yoklar; yalnızca durum
    /// değiştiğinde <see cref="DevicePresenceEvent"/> yayınlar. Mevcut WMI
    /// tabanlı <see cref="DeviceWatcher"/>'ı değiştirmez, onu tamamlar.
    /// </summary>
    public sealed class DeviceMonitor : IDisposable
    {
        private static readonly Lazy<DeviceMonitor> Lazy =
            new Lazy<DeviceMonitor>(() => new DeviceMonitor());

        private readonly AdbClient _adb = new AdbClient();
        private readonly FastbootClient _fastboot = new FastbootClient();
        private readonly Dictionary<string, MonitoredDevice> _known =
            new Dictionary<string, MonitoredDevice>(StringComparer.OrdinalIgnoreCase);

        private readonly object _gate = new object();
        private CancellationTokenSource _cts;
        private Task _loop;
        private bool _errorLogged;

        private DeviceMonitor()
        {
        }

        public static DeviceMonitor Instance => Lazy.Value;

        public bool IsRunning => _loop != null && !_loop.IsCompleted;

        public IReadOnlyList<MonitoredDevice> Devices
        {
            get
            {
                lock (_gate)
                {
                    return _known.Values.ToList();
                }
            }
        }

        public void Start()
        {
            if (IsRunning)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            _loop = Task.Run(() => LoopAsync(_cts.Token));
        }

        public void Stop()
        {
            try
            {
                _cts?.Cancel();
            }
            catch (Exception)
            {
                // yok sayılır
            }
        }

        public void Dispose()
        {
            Stop();
        }

        private async Task LoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await PollAsync();
                }
                catch (Exception ex)
                {
                    if (!_errorLogged)
                    {
                        _errorLogged = true;
                        LogHub.Instance.Warn("Cihaz izleme hatası: " + ex.Message, "İZLEME");
                    }
                }

                try
                {
                    await Task.Delay(3000, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task PollAsync()
        {
            var current = new Dictionary<string, MonitoredDevice>(StringComparer.OrdinalIgnoreCase);

            if (Toolchain.Instance.HasAdb)
            {
                var devices = await _adb.ListDevicesAsync();

                foreach (var device in devices)
                {
                    current["ADB:" + device.Serial] = new MonitoredDevice
                    {
                        Serial = device.Serial,
                        Mode = "ADB",
                        State = device.State,
                        Model = string.IsNullOrWhiteSpace(device.Model) ? "—" : device.Model,
                        Product = string.IsNullOrWhiteSpace(device.Product)
                            ? (device.DeviceName ?? "—")
                            : device.Product
                    };
                }
            }

            if (Toolchain.Instance.HasFastboot)
            {
                var serials = await _fastboot.ListSerialsAsync();

                foreach (var serial in serials)
                {
                    current["FB:" + serial] = new MonitoredDevice
                    {
                        Serial = serial,
                        Mode = "Fastboot",
                        State = "device",
                        Model = "—",
                        Product = "—"
                    };
                }
            }

            List<MonitoredDevice> added;
            List<MonitoredDevice> removed;

            lock (_gate)
            {
                added = current.Where(pair => !_known.ContainsKey(pair.Key))
                    .Select(pair => pair.Value)
                    .ToList();

                removed = _known.Where(pair => !current.ContainsKey(pair.Key))
                    .Select(pair => pair.Value)
                    .ToList();

                _known.Clear();

                foreach (var pair in current)
                {
                    _known[pair.Key] = pair.Value;
                }
            }

            foreach (var device in added)
            {
                if (device.Mode == "ADB" && !device.IsOnline)
                {
                    LogHub.Instance.Warn(
                        "ADB cihazı yetkisiz/çevrimdışı: " + device.Serial +
                        " — telefonda RSA onayı gerekli.", "İZLEME");
                }
                else
                {
                    LogHub.Instance.Ok(
                        device.Mode + " cihazı bağlandı: " + device.Serial +
                        (device.Model != "—" ? " · " + device.Model : string.Empty), "İZLEME");
                }

                EventAggregator.Instance.Publish(new DevicePresenceEvent
                {
                    Device = device,
                    Connected = true
                });
            }

            foreach (var device in removed)
            {
                LogHub.Instance.Warn(device.Mode + " cihazı ayrıldı: " + device.Serial, "İZLEME");

                EventAggregator.Instance.Publish(new DevicePresenceEvent
                {
                    Device = device,
                    Connected = false
                });
            }
        }
    }
}
