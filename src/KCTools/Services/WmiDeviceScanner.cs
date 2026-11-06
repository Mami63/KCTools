using System;
using System.Collections.Generic;
using System.Globalization;
using System.Management;
using KCTools.Models;

namespace KCTools.Services
{
    /// <summary>
    /// Win32_PnPEntity üzerinden anlık aygıt envanteri çıkarır.
    /// Yalnızca ilgilenilen aygıtlar (Android / Qualcomm / MediaTek / seri port) döner.
    /// </summary>
    internal static class WmiDeviceScanner
    {
        private const string Query =
            "SELECT DeviceID, PNPDeviceID, Name, Caption, Description, Service, Manufacturer, Status " +
            "FROM Win32_PnPEntity";

        public static List<DetectedDevice> Scan(bool includeAllUsb)
        {
            var found = new List<DetectedDevice>();

            using (var searcher = new ManagementObjectSearcher(Query))
            using (var collection = searcher.Get())
            {
                foreach (ManagementObject item in collection)
                {
                    using (item)
                    {
                        try
                        {
                            AddIfRelevant(item, found, includeAllUsb);
                        }
                        catch (ManagementException)
                        {
                            // Bazı sanal aygıtlar eksik özellik döndürür; atlanır.
                        }
                        catch (InvalidOperationException)
                        {
                            // WMI nesnesi bu arada kaldırıldıysa atlanır.
                        }
                    }
                }
            }

            return found;
        }

        private static void AddIfRelevant(ManagementObject item, List<DetectedDevice> found, bool includeAllUsb)
        {
            var name = Read(item, "Name") ?? Read(item, "Caption") ?? Read(item, "Description");
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            var instanceId = Read(item, "PNPDeviceID") ?? Read(item, "DeviceID");
            var service = Read(item, "Service");
            var caption = Read(item, "Caption");

            var kind = DeviceClassifier.Classify(name, instanceId, service);
            var port = DeviceClassifier.ExtractPort(name)
                       ?? DeviceClassifier.ExtractPort(caption)
                       ?? DeviceClassifier.ExtractPort(instanceId);

            if (!includeAllUsb && kind == DeviceKind.Unknown && port == null)
            {
                return;
            }

            DeviceClassifier.TryParseVidPid(instanceId, out var vid, out var pid);

            found.Add(new DetectedDevice
            {
                Key = string.IsNullOrWhiteSpace(instanceId) ? "NAME:" + name : instanceId,
                InstanceId = instanceId,
                Name = name,
                Kind = kind,
                Port = port,
                Serial = DeviceClassifier.ExtractSerial(instanceId),
                Vid = vid,
                Pid = pid,
                Service = service,
                Manufacturer = Read(item, "Manufacturer"),
                Status = Read(item, "Status"),
                IsConnected = true,
                Source = "wmi"
            });
        }

        private static string Read(ManagementBaseObject item, string property)
        {
            try
            {
                var value = item[property];
                return value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
            }
            catch (ManagementException)
            {
                return null;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }
}
