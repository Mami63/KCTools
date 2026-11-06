using System;
using System.Collections.Generic;
using System.Linq;
using KCTools.Models;
using Microsoft.Win32;

namespace KCTools.Services
{
    /// <summary>
    /// COM portlarını ek NuGet paketi olmadan (registry üzerinden) tarar.
    /// WMI/System.Management bağımlılığı yoktur.
    /// </summary>
    public static class PortWatcher
    {
        public static IReadOnlyList<PortInfo> Scan()
        {
            var result = new List<PortInfo>();

            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM"))
                {
                    if (key == null)
                    {
                        return result;
                    }

                    foreach (var valueName in key.GetValueNames())
                    {
                        var portName = key.GetValue(valueName) as string;
                        if (string.IsNullOrWhiteSpace(portName))
                        {
                            continue;
                        }

                        result.Add(new PortInfo(portName.Trim(), valueName, Describe(valueName)));
                    }
                }
            }
            catch (Exception ex)
            {
                LogHub.Instance.Warn("Port taraması başarısız: " + ex.Message, "PORT");
            }

            return result
                .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string Describe(string devicePath)
        {
            if (string.IsNullOrEmpty(devicePath))
            {
                return "Seri port";
            }

            if (devicePath.IndexOf("VCP", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "USB Sanal COM";
            }

            if (devicePath.IndexOf("BthModem", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Bluetooth Modem";
            }

            if (devicePath.IndexOf("Serial", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Seri Arayüz";
            }

            var last = devicePath.Split('\\').LastOrDefault();
            return string.IsNullOrWhiteSpace(last) ? "Seri port" : last;
        }
    }
}
