using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace KCTools.Services
{
    /// <summary>fastboot getvar çıktısını anahtar/değer sözlüğüne çevirir.</summary>
    public static class FastbootOutput
    {
        private static readonly Regex BootloaderPattern =
            new Regex(@"^\(bootloader\)\s*([^:]+):\s*(.*)$", RegexOptions.Compiled);

        private static readonly Regex PlainPattern =
            new Regex(@"^([A-Za-z0-9_\-\.]+):\s*(.*)$", RegexOptions.Compiled);

        /// <summary>
        /// "(bootloader) current-slot: a" ve "serialno: XYZ" satırlarını okur.
        /// Aynı anahtar tekrar ederse ilk değer korunur.
        /// </summary>
        public static Dictionary<string, string> Parse(string output)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(output))
            {
                return values;
            }

            foreach (var raw in output.Split('\n'))
            {
                var line = raw.Trim().TrimEnd('\r');
                if (line.Length == 0 || line == "..." || line.StartsWith("Finished"))
                {
                    continue;
                }

                var match = BootloaderPattern.Match(line);
                if (!match.Success)
                {
                    match = PlainPattern.Match(line);
                }

                if (!match.Success)
                {
                    continue;
                }

                var key = match.Groups[1].Value.Trim();
                var value = Normalize(match.Groups[2].Value);

                if (key.Length == 0 || values.ContainsKey(key))
                {
                    continue;
                }

                values[key] = value;
            }

            return values;
        }

        public static string First(Dictionary<string, string> values, params string[] keys)
        {
            if (values == null || keys == null)
            {
                return null;
            }

            foreach (var key in keys)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }

        /// <summary>fastboot uzun değerleri "..." ile kısaltır; kırpma izini temizler.</summary>
        public static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var text = value.Trim();

            while (text.EndsWith("...", StringComparison.Ordinal))
            {
                text = text.Substring(0, text.Length - 3).TrimEnd();
            }

            return text;
        }

        public static bool IsUnlocked(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var text = value.Trim().ToLowerInvariant();
            return text == "yes" || text == "true" || text == "1" || text.Contains("unlocked")
                   || text.Contains("açık");
        }
    }
}
