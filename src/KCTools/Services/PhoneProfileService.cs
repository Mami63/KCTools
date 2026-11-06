using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KCTools.Models;

namespace KCTools.Services
{
    /// <summary>
    /// Telefon bağlandığında ADB üzerinden model / üretici / Android sürümü okur.
    /// Cihaz yetkisizse veya adb yoksa null döner (arayüz "model bekleniyor" gösterir).
    /// </summary>
    public sealed class PhoneProfileService
    {
        private readonly AdbClient _adb = new AdbClient();

        public async Task<PhoneProfile> ReadAsync(
            string serial = null,
            CancellationToken cancellationToken = default)
        {
            if (!Toolchain.Instance.HasAdb)
            {
                return null;
            }

            _adb.Serial = string.IsNullOrWhiteSpace(serial) ? null : serial;

            var props = await _adb.GetPropsAsync(cancellationToken).ConfigureAwait(false);
            if (props == null || props.Count == 0)
            {
                return null;
            }

            var profile = new PhoneProfile
            {
                Serial = Value(props, "ro.serialno", "ro.boot.serialno", "ro.product.serial"),
                Manufacturer = Value(props, "ro.product.manufacturer", "ro.product.brand"),
                Model = Value(props, "ro.product.model", "ro.product.device"),
                Device = Value(props, "ro.product.device"),
                AndroidVersion = Value(props, "ro.build.version.release"),
                Sdk = Value(props, "ro.build.version.sdk"),
                MarketName = Value(
                    props,
                    "ro.product.marketname",
                    "ro.product.odm.marketname",
                    "ro.config.marketing_name",
                    "ro.product.vendor.marketname")
            };

            if (string.IsNullOrWhiteSpace(profile.Model) ||
                profile.Model == "—")
            {
                return null;
            }

            return profile;
        }

        public async Task<PhoneProfile> ReadFirstOnlineAsync(CancellationToken cancellationToken = default)
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

            return await ReadAsync(online.Serial, cancellationToken).ConfigureAwait(false);
        }

        private static string Value(System.Collections.Generic.Dictionary<string, string> props, params string[] keys)
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
    }
}
