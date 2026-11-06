using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using KCTools.Models;

namespace KCTools.Services
{
    /// <summary>
    /// Bağlı telefondan model, yapı, batarya, depolama, root ve güvenlik durumunu
    /// ADB üzerinden toplar. Cihaz yoksa null döner.
    /// </summary>
    public sealed class DeviceInfoService
    {
        private static readonly Regex BatteryPattern =
            new Regex(@"level:\s*(\d+)", RegexOptions.Compiled);

        private static readonly Regex SizePattern =
            new Regex(@"(\d{3,4}x\d{3,4})", RegexOptions.Compiled);

        private readonly AdbClient _adb = new AdbClient();

        public async Task<DeviceInfo> ReadAsync(CancellationToken cancellationToken = default)
        {
            if (!Toolchain.Instance.HasAdb)
            {
                return null;
            }

            var devices = await _adb.ListDevicesAsync(cancellationToken).ConfigureAwait(false);
            var online = devices.FirstOrDefault(d => d.IsOnline);

            if (online == null)
            {
                return null;
            }

            _adb.Serial = online.Serial;

            var props = await _adb.GetPropsAsync(cancellationToken).ConfigureAwait(false);

            var battery = await _adb.ShellAsync("dumpsys battery", cancellationToken).ConfigureAwait(false);
            var storage = await _adb.ShellAsync("df /data", cancellationToken).ConfigureAwait(false);
            var size = await _adb.ShellAsync("wm size", cancellationToken).ConfigureAwait(false);
            var root = await _adb.ShellAsync("which su", cancellationToken).ConfigureAwait(false);

            var info = new DeviceInfo
            {
                Model = Prop(props, "ro.product.model", "ro.product.device"),
                Brand = Prop(props, "ro.product.manufacturer", "ro.product.brand"),
                Android = Prop(props, "ro.build.version.release"),
                Sdk = Prop(props, "ro.build.version.sdk"),
                Serial = string.IsNullOrWhiteSpace(online.Serial) ? Prop(props, "ro.serialno") : online.Serial,
                Codename = Prop(props, "ro.product.device", "ro.product.product"),
                MarketName = Prop(
                    props,
                    "ro.product.marketname",
                    "ro.product.odm.marketname",
                    "ro.config.marketing_name",
                    "ro.product.vendor.marketname"),
                Build = Prop(props, "ro.build.display.id", "ro.build.id"),
                VerifiedBoot = Prop(props, "ro.boot.verifiedbootstate"),
                Frp = string.IsNullOrWhiteSpace(Prop(props, "ro.frp.pst", string.Empty)) ? "—" : "Aktif",
                Battery = ParseBattery(battery?.StandardOutput),
                Storage = ParseStorage(storage?.StandardOutput),
                Resolution = ParseSize(size?.StandardOutput),
                Root = !string.IsNullOrWhiteSpace(root?.StandardOutput) &&
                       root.StandardOutput.IndexOf("su", StringComparison.OrdinalIgnoreCase) >= 0
                    ? "Var (su)"
                    : "Yok"
            };

            var locked = Prop(props, "ro.boot.flash.locked", string.Empty);

            info.Bootloader = string.IsNullOrWhiteSpace(locked)
                ? "—"
                : locked == "1" ? "Kilitli" : "Açık";

            return info;
        }

        private static string Prop(Dictionary<string, string> props, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (props != null && props.TryGetValue(key, out var value) &&
                    !string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return "—";
        }

        private static string ParseBattery(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                return "—";
            }

            var match = BatteryPattern.Match(output);
            return match.Success ? match.Groups[1].Value + "%" : "—";
        }

        private static string ParseStorage(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                return "—";
            }

            foreach (var line in output.Split('\n'))
            {
                if (!line.Contains("/data") || !line.Contains("%"))
                {
                    continue;
                }

                var parts = Regex.Split(line.Trim(), @"\s+");

                if (parts.Length < 4)
                {
                    continue;
                }

                if (!double.TryParse(parts[1], out var totalKb) ||
                    !double.TryParse(parts[3], out var freeKb))
                {
                    continue;
                }

                return string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "{0:0.0} GB boş / {1:0.0} GB", freeKb / 1024 / 1024, totalKb / 1024 / 1024);
            }

            return "—";
        }

        private static string ParseSize(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                return "—";
            }

            var match = SizePattern.Match(output);
            return match.Success ? match.Groups[1].Value : "—";
        }
    }
}
