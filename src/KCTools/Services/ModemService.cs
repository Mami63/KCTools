using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using KCTools.Models;

namespace KCTools.Services
{
    /// <summary>
    /// AT komut seti üzerinden modem bilgisi okur: IMEI, model, yazılım, SIM, IMSI,
    /// operatör, şebeke kaydı, sinyal ve batarya. Her komut kendi zaman aşımıyla çalışır.
    /// </summary>
    public sealed class ModemService
    {
        private static readonly Regex ImeiPattern = new Regex(@"(?<!\d)(\d{15})(?!\d)", RegexOptions.Compiled);
        private static readonly Regex QuotedPattern = new Regex("\"([^\"]*)\"", RegexOptions.Compiled);

        private readonly AtCommandService _at;

        public ModemService(AtCommandService at)
        {
            _at = at ?? throw new ArgumentNullException(nameof(at));
        }

        public async Task<ModemInfo> ReadAllAsync(CancellationToken cancellationToken = default)
        {
            var info = new ModemInfo
            {
                Imei = await ReadImeiAsync(cancellationToken) ?? "—",
                Model = await ReadModelAsync(cancellationToken) ?? "—",
                Firmware = await ReadFirmwareAsync(cancellationToken) ?? "—",
                Imsi = await ReadImsiAsync(cancellationToken) ?? "—",
                SimState = await ReadSimStateAsync(cancellationToken) ?? "—",
                Operator = await ReadOperatorAsync(cancellationToken) ?? "—",
                Registration = await ReadRegistrationAsync(cancellationToken) ?? "—",
                Battery = await ReadBatteryAsync(cancellationToken) ?? "—"
            };

            info.ImeiValid = IsValidImei(info.Imei);

            var signal = await ReadSignalAsync(cancellationToken);
            info.SignalRaw = signal;
            info.SignalDbm = signal >= 0 && signal != 99 ? -113 + (2 * signal) : 0;

            return info;
        }

        public async Task<string> ReadImeiAsync(CancellationToken cancellationToken = default)
        {
            foreach (var command in new[] { "AT+CGSN", "AT+GSN" })
            {
                var response = await _at.SendAsync(command, TimeSpan.FromSeconds(3), cancellationToken)
                    .ConfigureAwait(false);

                if (!response.Success)
                {
                    continue;
                }

                foreach (var line in response.Lines)
                {
                    var digits = OnlyDigits(line);

                    if (digits.Length == 15)
                    {
                        return digits;
                    }
                }
            }

            var ati = await _at.SendAsync("ATI", TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);

            if (ati.Success)
            {
                foreach (var line in ati.Lines)
                {
                    var match = ImeiPattern.Match(line);

                    if (match.Success)
                    {
                        return match.Groups[1].Value;
                    }
                }
            }

            return null;
        }

        public async Task<string> ReadModelAsync(CancellationToken cancellationToken = default)
        {
            foreach (var command in new[] { "AT+CGMM", "AT+GMM" })
            {
                var value = await ReadFirstDataLineAsync(command, cancellationToken).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }

        public async Task<string> ReadFirmwareAsync(CancellationToken cancellationToken = default)
        {
            foreach (var command in new[] { "AT+CGMR", "AT+GMR" })
            {
                var value = await ReadFirstDataLineAsync(command, cancellationToken).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }

        public async Task<string> ReadImsiAsync(CancellationToken cancellationToken = default)
        {
            var response = await _at.SendAsync("AT+CIMI", TimeSpan.FromSeconds(3), cancellationToken)
                .ConfigureAwait(false);

            if (!response.Success)
            {
                return null;
            }

            foreach (var line in response.Lines)
            {
                var digits = OnlyDigits(line);

                if (digits.Length >= 14 && digits.Length <= 16)
                {
                    return digits;
                }
            }

            return null;
        }

        public async Task<string> ReadSimStateAsync(CancellationToken cancellationToken = default)
        {
            var response = await _at.SendAsync("AT+CPIN?", TimeSpan.FromSeconds(3), cancellationToken)
                .ConfigureAwait(false);

            return response.ValueAfterColon("+CPIN");
        }

        public async Task<string> ReadOperatorAsync(CancellationToken cancellationToken = default)
        {
            var response = await _at.SendAsync("AT+COPS?", TimeSpan.FromSeconds(5), cancellationToken)
                .ConfigureAwait(false);

            var value = response.ValueAfterColon("+COPS");

            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var match = QuotedPattern.Match(value);

            return match.Success ? match.Groups[1].Value : value;
        }

        public async Task<string> ReadRegistrationAsync(CancellationToken cancellationToken = default)
        {
            var response = await _at.SendAsync("AT+CREG?", TimeSpan.FromSeconds(3), cancellationToken)
                .ConfigureAwait(false);

            var value = response.ValueAfterColon("+CREG");

            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var parts = value.Split(',');
            var status = parts.Length > 1 ? parts[1].Trim() : value.Trim();

            switch (status)
            {
                case "0": return "Kayıtlı değil";
                case "1": return "Ev şebekesinde";
                case "2": return "Aranıyor";
                case "3": return "Reddedildi";
                case "4": return "Bilinmiyor";
                case "5": return "Dolaşımda";
                default: return status;
            }
        }

        public async Task<int> ReadSignalAsync(CancellationToken cancellationToken = default)
        {
            var response = await _at.SendAsync("AT+CSQ", TimeSpan.FromSeconds(3), cancellationToken)
                .ConfigureAwait(false);

            var value = response.ValueAfterColon("+CSQ");

            if (string.IsNullOrWhiteSpace(value))
            {
                return -1;
            }

            var first = value.Split(',')[0].Trim();

            return int.TryParse(first, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rssi)
                ? rssi
                : -1;
        }

        public async Task<string> ReadBatteryAsync(CancellationToken cancellationToken = default)
        {
            var response = await _at.SendAsync("AT+CBC", TimeSpan.FromSeconds(3), cancellationToken)
                .ConfigureAwait(false);

            var value = response.ValueAfterColon("+CBC");

            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var parts = value.Split(',');

            if (parts.Length < 3)
            {
                return value.Trim();
            }

            var percent = parts[1].Trim();
            var voltage = parts[2].Trim();

            if (int.TryParse(voltage, out var millivolts) && millivolts > 0)
            {
                return percent + "% · " + (millivolts / 1000.0).ToString("0.00", CultureInfo.InvariantCulture) + " V";
            }

            return percent + "%";
        }

        /// <summary>15 haneli IMEI'nin Luhn sağlamasını doğrular.</summary>
        public static bool IsValidImei(string imei)
        {
            var digits = OnlyDigits(imei);

            if (digits.Length != 15)
            {
                return false;
            }

            var sum = 0;

            for (var i = 0; i < 15; i++)
            {
                var digit = digits[14 - i] - '0';

                if (i % 2 == 1)
                {
                    digit *= 2;

                    if (digit > 9)
                    {
                        digit -= 9;
                    }
                }

                sum += digit;
            }

            return sum % 10 == 0;
        }

        public static string OnlyDigits(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var builder = new System.Text.StringBuilder(text.Length);

            foreach (var c in text)
            {
                if (char.IsDigit(c))
                {
                    builder.Append(c);
                }
            }

            return builder.ToString();
        }

        /// <summary>14 haneli gövde için 15. Luhn kontrol hanesini hesaplar.</summary>
        public static int ComputeLuhnCheckDigit(string body14)
        {
            var digits = OnlyDigits(body14);

            if (digits.Length == 0)
            {
                return 0;
            }

            var sum = 0;
            var doubleIt = true;

            for (var i = digits.Length - 1; i >= 0; i--)
            {
                var digit = digits[i] - '0';

                if (doubleIt)
                {
                    digit *= 2;

                    if (digit > 9)
                    {
                        digit -= 9;
                    }
                }

                doubleIt = !doubleIt;
                sum += digit;
            }

            return (10 - (sum % 10)) % 10;
        }

        /// <summary>
        /// Girilen IMEI'yi normalize eder ve doğrular; son hane hatalıysa
        /// doğru kontrol hanesini önerir.
        /// </summary>
        public static bool TryNormalizeImei(
            string input,
            out string imei,
            out string error,
            out string suggestion)
        {
            imei = OnlyDigits(input);
            error = null;
            suggestion = null;

            if (imei.Length != 15)
            {
                error = "IMEI 15 hane olmalı (girilen: " + imei.Length + ").";
                return false;
            }

            if (IsValidImei(imei))
            {
                return true;
            }

            var expected = ComputeLuhnCheckDigit(imei.Substring(0, 14));
            suggestion = imei.Substring(0, 14) + expected;
            error = "Luhn hatası — doğru IMEI: " + suggestion;
            return false;
        }

        private async Task<string> ReadFirstDataLineAsync(string command, CancellationToken cancellationToken)
        {
            var response = await _at.SendAsync(command, TimeSpan.FromSeconds(3), cancellationToken)
                .ConfigureAwait(false);

            if (!response.Success)
            {
                return null;
            }

            return response.Lines.FirstOrDefault(line =>
                !line.StartsWith("+", StringComparison.Ordinal) &&
                !line.Equals("OK", StringComparison.OrdinalIgnoreCase));
        }
    }
}
