using System;
using System.Text.RegularExpressions;

namespace KCTools.LicenseServer.Logging
{
    /// <summary>Log satırlarındaki parola/token/anahtar değerlerini maskeler.</summary>
    public static class SensitiveDataRedactor
    {
        private static readonly Regex[] Patterns =
        {
            new Regex(
                @"(?i)\b(password|pwd)\s*=\s*[^;]*",
                RegexOptions.Compiled),

            new Regex(
                @"(?i)(x-admin-key\s*:\s*)\S+",
                RegexOptions.Compiled),

            new Regex(
                @"(?i)(authorization\s*:\s*bearer\s+)\S+",
                RegexOptions.Compiled),

            new Regex(
                @"(?i)\bbearer\s+[A-Za-z0-9\-._~+/]{8,}=*",
                RegexOptions.Compiled),

            new Regex(
                @"(?i)(client_secret\s*=\s*)[^&\s]+",
                RegexOptions.Compiled),

            new Regex(
                @"""(password|secret|access_token|refresh_token|token)""\s*:\s*""[^""]*""",
                RegexOptions.Compiled),

            new Regex(
                @"\beyJ[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{10,}",
                RegexOptions.Compiled)
        };

        public static string Redact(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            var result = value;

            result = Patterns[0].Replace(result, match =>
            {
                var separator = match.Value.IndexOf('=');
                return separator >= 0
                    ? match.Value.Substring(0, separator + 1) + "***"
                    : "password=***";
            });

            result = Patterns[1].Replace(result,
                match => match.Groups[1].Value + "***");

            result = Patterns[2].Replace(result,
                match => match.Groups[1].Value + "***");

            result = Patterns[3].Replace(result, "Bearer ***");

            result = Patterns[4].Replace(result,
                match => match.Groups[1].Value + "***");

            result = Patterns[5].Replace(result,
                match => "\"" + match.Groups[1].Value + "\":\"***\"");

            result = Patterns[6].Replace(result, "***JWT***");

            return result;
        }
    }
}
