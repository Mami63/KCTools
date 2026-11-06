using System;
using System.Text.RegularExpressions;
using KCTools.Models;

namespace KCTools.Services
{
    /// <summary>
    /// PnP adı, örnek kimliği ve sürücü servisine bakarak aygıtın
    /// servis modunu (EDL, BROM, ADB, Fastboot ...) belirler.
    /// </summary>
    public static class DeviceClassifier
    {
        private static readonly Regex ParenthesizedPort =
            new Regex(@"\((COM\d+)\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex AnyPort =
            new Regex(@"\b(COM\d+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex VidPattern =
            new Regex(@"VID_([0-9A-F]{4})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex PidPattern =
            new Regex(@"PID_([0-9A-F]{4})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Windows PnP bilgisinden mod çıkarır.</summary>
        public static DeviceKind Classify(string name, string instanceId, string service)
        {
            var text = ((name ?? string.Empty) + " | " + (instanceId ?? string.Empty) + " | " +
                        (service ?? string.Empty)).ToUpperInvariant();

            TryParseVidPid(instanceId, out var vid, out var pid);

            // 1) Android önyükleme modları — ad en güvenilir sinyaldir.
            if (Contains(text, "FASTBOOT") || Contains(text, "BOOTLOADER INTERFACE") ||
                Contains(text, "ANDROID BOOTLOADER") || Contains(text, "FASTBOOTD"))
            {
                return DeviceKind.Fastboot;
            }

            if (Contains(text, "ADB") || Contains(text, "ANDROID DEBUG"))
            {
                return DeviceKind.Adb;
            }

            // 2) Qualcomm: VID 05C6 + bilinen PID'ler.
            if (vid == "05C6")
            {
                if (pid == "9008" || pid == "900E" || pid == "9091" || pid == "9006")
                {
                    return DeviceKind.QualcommEdl;
                }

                if (pid == "901D" || pid == "9024" || pid == "9025")
                {
                    return DeviceKind.QualcommDiag;
                }

                return Contains(text, "DIAG") ? DeviceKind.QualcommDiag : DeviceKind.Qualcomm;
            }

            if (Contains(text, "QDLOADER") || Contains(text, "QUALCOMM HS-USB") ||
                Contains(text, "SAHARA") || Contains(text, "9008"))
            {
                return Contains(text, "DIAG") ? DeviceKind.QualcommDiag : DeviceKind.QualcommEdl;
            }

            // 3) MediaTek: VID 0E8D + preloader/BROM adları.
            if (vid == "0E8D")
            {
                if (Contains(text, "BROM"))
                {
                    return DeviceKind.MtkBrom;
                }

                if (pid == "0003" || pid == "2000" || pid == "2001" || pid == "201C")
                {
                    return DeviceKind.MtkPreloader;
                }

                return DeviceKind.Mtk;
            }

            if (Contains(text, "BROM"))
            {
                return DeviceKind.MtkBrom;
            }

            if (Contains(text, "PRELOADER") || Contains(text, "MTK USB") || Contains(text, "MEDIATEK USB"))
            {
                return DeviceKind.MtkPreloader;
            }

            // 4) Google / Pixel USB kimlikleri.
            if (vid == "18D1")
            {
                return pid == "4EE2" || pid == "D00D" ? DeviceKind.Fastboot : DeviceKind.Adb;
            }

            // 5) Android bileşik aygıtları ve MTP.
            if (Contains(text, "MTP") || Contains(text, "PTP"))
            {
                return DeviceKind.Android;
            }

            if (Contains(text, "ANDROID") && Contains(text, "INTERFACE"))
            {
                return Contains(text, "ADB") ? DeviceKind.Adb : DeviceKind.Android;
            }

            // 6) Seri port (COM içeren her şey son çare olarak Serial).
            if (ExtractPort(name) != null || Contains(text, "USB SERIAL") || Contains(text, "SERIAL PORT"))
            {
                return DeviceKind.Serial;
            }

            return DeviceKind.Unknown;
        }

        /// <summary>"... (COM4)" ya da "COM4" metninden port adını çıkarır.</summary>
        public static string ExtractPort(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var match = ParenthesizedPort.Match(text);
            if (match.Success)
            {
                return match.Groups[1].Value.ToUpperInvariant();
            }

            if (text.Length > 200)
            {
                return null;
            }

            match = AnyPort.Match(text);
            return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
        }

        /// <summary>USB\VID_x&amp;PID_y\SERI gibi kimliklerden seri numarasını alır.</summary>
        public static string ExtractSerial(string instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                return null;
            }

            var parts = instanceId.Split('\\');
            var last = parts[parts.Length - 1].Trim();

            if (last.Length == 0 || last.IndexOf('&') >= 0 || last.StartsWith("{"))
            {
                return null;
            }

            return last;
        }

        public static bool TryParseVidPid(string instanceId, out string vid, out string pid)
        {
            vid = null;
            pid = null;

            if (string.IsNullOrWhiteSpace(instanceId))
            {
                return false;
            }

            var v = VidPattern.Match(instanceId);
            var p = PidPattern.Match(instanceId);

            if (!v.Success || !p.Success)
            {
                return false;
            }

            vid = v.Groups[1].Value.ToUpperInvariant();
            pid = p.Groups[1].Value.ToUpperInvariant();
            return true;
        }

        private static bool Contains(string text, string value)
        {
            return text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
