using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace KCTools.Services
{
    /// <summary>ADB `dumpsys telephony.registry` çıktısından sinyal özeti.</summary>
    public sealed class SignalSnapshot
    {
        public int Level { get; set; } = -1;

        public int Rsrp { get; set; } = int.MinValue;

        public int Rsrq { get; set; } = int.MinValue;

        public int Snr { get; set; } = int.MinValue;

        public string Operator { get; set; } = "—";

        public string Technology { get; set; } = "—";

        public string Text
        {
            get
            {
                if (Level < 0 && Rsrp == int.MinValue)
                {
                    return "—";
                }

                var parts = new System.Collections.Generic.List<string>();

                if (Level >= 0)
                {
                    parts.Add(Level + "/4");
                }

                if (Rsrp != int.MinValue)
                {
                    parts.Add("RSRP " + Rsrp + " dBm");
                }

                if (Rsrq != int.MinValue)
                {
                    parts.Add("RSRQ " + Rsrq + " dB");
                }

                if (Snr != int.MinValue)
                {
                    parts.Add("SNR " + Snr + " dB");
                }

                if (Technology != "—")
                {
                    parts.Add(Technology);
                }

                if (Operator != "—")
                {
                    parts.Add(Operator);
                }

                return string.Join(" · ", parts);
            }
        }
    }

    /// <summary>
    /// AT portu yokken sinyal bilgisini ADB üzerinden okur.
    /// Android 10+ `dumpsys telephony.registry` çıktısı shell için okunabilir.
    /// </summary>
    public static class TelephonyReader
    {
        private static readonly Regex BlockPattern = new Regex(
            @"mSignalStrength=SignalStrength:\{(.*?)\}\s*(?:\r?\n|$)",
            RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex LtePattern = new Regex(
            @"mLte=CellSignalStrengthLte:\s*rssi=(-?\d+)\s+rsrp=(-?\d+)\s+rsrq=(-?\d+)\s+rssnr=(-?\d+).*?level=(\d+)",
            RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex OperatorPattern = new Regex(
            @"mOperatorAlphaLong=([^,}]+)",
            RegexOptions.Compiled);

        private static readonly Regex TechPattern = new Regex(
            @"getRilDataRadioTechnology=\d+\((\w+)\)",
            RegexOptions.Compiled);

        public static async Task<SignalSnapshot> ReadAsync(
            AdbClient adb,
            CancellationToken cancellationToken = default)
        {
            if (adb == null)
            {
                return null;
            }

            var result = await adb.ShellAsync("dumpsys telephony.registry", cancellationToken)
                .ConfigureAwait(false);

            if (result == null || !result.Success)
            {
                return null;
            }

            return Parse(result.StandardOutput);
        }

        public static SignalSnapshot Parse(string dumpsys)
        {
            if (string.IsNullOrWhiteSpace(dumpsys))
            {
                return null;
            }

            var snapshot = new SignalSnapshot();

            foreach (Match block in BlockPattern.Matches(dumpsys))
            {
                var content = block.Groups[1].Value;

                if (content.IndexOf("mLte=invalid", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }

                var lte = LtePattern.Match(content);

                if (!lte.Success)
                {
                    continue;
                }

                if (int.TryParse(lte.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rsrp))
                {
                    snapshot.Rsrp = rsrp;
                }

                if (int.TryParse(lte.Groups[3].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rsrq))
                {
                    snapshot.Rsrq = rsrq;
                }

                if (int.TryParse(lte.Groups[4].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var snr))
                {
                    snapshot.Snr = snr;
                }

                if (int.TryParse(lte.Groups[5].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var level))
                {
                    snapshot.Level = level;
                }

                break;
            }

            var operatorMatch = OperatorPattern.Match(dumpsys);

            if (operatorMatch.Success)
            {
                var value = operatorMatch.Groups[1].Value.Trim();

                if (value.Length > 0 && !string.Equals(value, "null", StringComparison.OrdinalIgnoreCase))
                {
                    snapshot.Operator = value;
                }
            }

            var techMatch = TechPattern.Match(dumpsys);

            if (techMatch.Success)
            {
                snapshot.Technology = techMatch.Groups[1].Value;
            }

            return snapshot.Level < 0 && snapshot.Rsrp == int.MinValue ? null : snapshot;
        }
    }
}
