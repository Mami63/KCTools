using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using KCTools.Common;
using Microsoft.Win32;

namespace KCTools.Services
{
    /// <summary>Tek bir ortam kontrolü sonucu.</summary>
    public sealed class EnvironmentCheckItem
    {
        public string Name { get; set; } = string.Empty;

        public bool Ok { get; set; }

        public string Detail { get; set; } = string.Empty;

        public string StatusText => Ok ? "HAZIR" : "EKSİK";
    }

    /// <summary>
    /// Açılışta çalışan ortam denetimi: adb/fastboot, ADB WinUSB DLL'leri,
    /// Qualcomm QDLoader 9008 ve MediaTek Preloader/BROM sürücüleri (registry),
    /// yönetici hakları, .NET sürümü ve günlük klasörü yazma izni.
    /// </summary>
    public sealed class EnvironmentCheckService : ObservableObject
    {
        private static readonly string[] QualcommUsbIds =
        {
            "VID_05C6&PID_9008", "VID_05C6&PID_9091", "VID_05C6&PID_900E", "VID_05C6&PID_9006"
        };

        private static readonly string[] MtkUsbIds =
        {
            "VID_0E8D&PID_0003", "VID_0E8D&PID_2000", "VID_0E8D&PID_2001", "VID_0E8D&PID_201C"
        };

        private static readonly string[] QualcommServices = { "qcusbser", "qcser", "qcusb9106" };
        private static readonly string[] MtkServices = { "usb2ser", "mtkcdc", "mtk_usb" };

        private bool _isRunning;
        private DateTime? _lastRun;

        private EnvironmentCheckService()
        {
        }

        public static EnvironmentCheckService Instance { get; } = new EnvironmentCheckService();

        public ObservableCollection<EnvironmentCheckItem> Items { get; } =
            new ObservableCollection<EnvironmentCheckItem>();

        public bool IsRunning
        {
            get => _isRunning;
            private set => Set(ref _isRunning, value);
        }

        public DateTime? LastRun
        {
            get => _lastRun;
            private set
            {
                if (Set(ref _lastRun, value))
                {
                    OnPropertyChanged(nameof(LastRunText));
                }
            }
        }

        public string LastRunText =>
            LastRun.HasValue ? LastRun.Value.ToString("HH:mm:ss") : "henüz çalışmadı";

        public async Task RunAsync(bool log = true)
        {
            if (IsRunning)
            {
                return;
            }

            IsRunning = true;

            try
            {
                var items = await Task.Run(() => Probe()).ConfigureAwait(true);

                Items.Clear();
                foreach (var item in items)
                {
                    Items.Add(item);
                }

                LastRun = DateTime.Now;

                if (log)
                {
                    var missing = items.Where(i => !i.Ok).ToList();

                    if (missing.Count == 0)
                    {
                        LogHub.Instance.Ok("Ortam kontrolü: tüm bileşenler hazır.", "ORTAM");
                    }
                    else
                    {
                        LogHub.Instance.Warn(
                            "Ortam kontrolü: " + missing.Count + " eksik — " +
                            string.Join(", ", missing.Select(i => i.Name)), "ORTAM");
                    }

                    foreach (var item in items)
                    {
                        LogHub.Instance.Info(
                            item.Name + ": " + (item.Ok ? "hazır" : "eksik") + " · " + item.Detail,
                            "ORTAM");
                    }
                }
            }
            finally
            {
                IsRunning = false;
            }
        }

        private static List<EnvironmentCheckItem> Probe()
        {
            var items = new List<EnvironmentCheckItem>();

            var isAdmin = false;

            try
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    isAdmin = new WindowsPrincipal(identity)
                        .IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch (Exception)
            {
                // yok sayılır
            }

            items.Add(new EnvironmentCheckItem
            {
                Name = "Yönetici hakları",
                Ok = isAdmin,
                Detail = isAdmin ? "yükseltilmiş oturum" : "sürücü işlemleri için yönetici gerekir"
            });

            var toolchain = Toolchain.Instance;

            items.Add(new EnvironmentCheckItem
            {
                Name = "adb.exe",
                Ok = toolchain.HasAdb,
                Detail = toolchain.AdbPathDisplay
            });

            items.Add(new EnvironmentCheckItem
            {
                Name = "fastboot.exe",
                Ok = toolchain.HasFastboot,
                Detail = toolchain.FastbootPathDisplay
            });

            var adbFolder = toolchain.HasAdb
                ? System.IO.Path.GetDirectoryName(toolchain.AdbPath)
                : null;

            var winUsbOk = adbFolder != null &&
                           System.IO.File.Exists(System.IO.Path.Combine(adbFolder, "AdbWinApi.dll")) &&
                           System.IO.File.Exists(System.IO.Path.Combine(adbFolder, "AdbWinUsbApi.dll"));

            items.Add(new EnvironmentCheckItem
            {
                Name = "ADB WinUSB DLL'leri",
                Ok = winUsbOk,
                Detail = winUsbOk ? "AdbWinApi.dll + AdbWinUsbApi.dll" : "adb.exe yanında bulunamadı"
            });

            var qualcommUsb = FindUsbDevice(QualcommUsbIds);
            var qualcommService = FindService(QualcommServices);
            var qualcommOk = qualcommUsb != null || qualcommService != null;

            items.Add(new EnvironmentCheckItem
            {
                Name = "Qualcomm QDLoader 9008",
                Ok = qualcommOk,
                Detail = qualcommOk
                    ? "kayıt: " + (qualcommUsb ?? qualcommService)
                    : "VID_05C6&PID_9008 kaydı ve qcusbser servisi yok"
            });

            var mtkUsb = FindUsbDevice(MtkUsbIds);
            var mtkService = FindService(MtkServices);
            var mtkOk = mtkUsb != null || mtkService != null;

            items.Add(new EnvironmentCheckItem
            {
                Name = "MediaTek Preloader / BROM",
                Ok = mtkOk,
                Detail = mtkOk
                    ? "kayıt: " + (mtkUsb ?? mtkService)
                    : "VID_0E8D&PID_0003/2000 kaydı ve usb2ser servisi yok"
            });

            items.Add(new EnvironmentCheckItem
            {
                Name = ".NET çalışma zamanı",
                Ok = Environment.Version.Major >= 8,
                Detail = Environment.Version + " · " + Environment.OSVersion.VersionString
            });

            var writable = CanWriteLocalAppData();

            items.Add(new EnvironmentCheckItem
            {
                Name = "Günlük klasörü",
                Ok = writable,
                Detail = writable ? "%LOCALAPPDATA%\\KCTools yazılabilir" : "yazma izni yok"
            });

            return items;
        }

        private static string FindUsbDevice(IEnumerable<string> ids)
        {
            try
            {
                using (var usb = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB"))
                {
                    if (usb == null)
                    {
                        return null;
                    }

                    foreach (var name in usb.GetSubKeyNames())
                    {
                        foreach (var id in ids)
                        {
                            if (name.StartsWith(id, StringComparison.OrdinalIgnoreCase))
                            {
                                return name;
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // registry erişimi engelliyse "bulunamadı" sayılır
            }

            return null;
        }

        private static string FindService(IEnumerable<string> names)
        {
            foreach (var name in names)
            {
                try
                {
                    using (var key = Registry.LocalMachine.OpenSubKey(
                               @"SYSTEM\CurrentControlSet\Services\" + name))
                    {
                        if (key != null)
                        {
                            return "servis " + name;
                        }
                    }
                }
                catch (Exception)
                {
                    // yok sayılır
                }
            }

            return null;
        }

        private static bool CanWriteLocalAppData()
        {
            try
            {
                var folder = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "KCTools");

                System.IO.Directory.CreateDirectory(folder);
                var probe = System.IO.Path.Combine(folder, ".write-test");
                System.IO.File.WriteAllText(probe, "ok");
                System.IO.File.Delete(probe);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public string ToText()
        {
            var builder = new StringBuilder();

            foreach (var item in Items)
            {
                builder.AppendLine(item.Name + ": " + (item.Ok ? "hazır" : "eksik") + " — " + item.Detail);
            }

            return builder.ToString();
        }
    }
}
