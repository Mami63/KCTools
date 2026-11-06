using System;
using System.Text;
using System.Text.Json;

namespace KCTools.Security
{
    /// <summary>JWT yük (payload) alanları.</summary>
    public sealed class JwtClaims
    {
        public string Subject { get; set; }

        public string Hwid { get; set; }

        public string Plan { get; set; }

        public int Credits { get; set; }

        public string Issuer { get; set; }

        public string Jti { get; set; }

        public DateTimeOffset? ExpiresAt { get; set; }

        public DateTimeOffset? IssuedAt { get; set; }

        public bool IsExpired => ExpiresAt.HasValue && ExpiresAt.Value <= DateTimeOffset.UtcNow;

        public TimeSpan? Remaining =>
            ExpiresAt.HasValue ? ExpiresAt.Value - DateTimeOffset.UtcNow : (TimeSpan?)null;
    }

    /// <summary>
    /// JWT'yi imza doğrulaması yapmadan okur (imza doğrulaması sunucunun işidir;
    /// istemci yalnızca talep alanlarını ve son kullanma tarihini görmek için çözer).
    /// </summary>
    public static class JwtInspector
    {
        public static JwtClaims Read(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            var parts = token.Split('.');

            if (parts.Length != 3)
            {
                return null;
            }

            try
            {
                var json = Base64UrlDecode(parts[1]);

                using (var document = JsonDocument.Parse(json))
                {
                    var root = document.RootElement;

                    var claims = new JwtClaims
                    {
                        Subject = GetString(root, "sub"),
                        Hwid = GetString(root, "hwid"),
                        Plan = GetString(root, "plan"),
                        Credits = GetInt(root, "credits"),
                        Issuer = GetString(root, "iss"),
                        Jti = GetString(root, "jti")
                    };

                    var exp = GetLong(root, "exp");

                    if (exp > 0)
                    {
                        claims.ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(exp);
                    }

                    var iat = GetLong(root, "iat");

                    if (iat > 0)
                    {
                        claims.IssuedAt = DateTimeOffset.FromUnixTimeSeconds(iat);
                    }

                    return claims;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static string Base64UrlEncode(byte[] data)
        {
            return Convert.ToBase64String(data)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        public static string Base64UrlDecode(string value)
        {
            var text = value.Replace('-', '+').Replace('_', '/');

            switch (text.Length % 4)
            {
                case 2:
                    text += "==";
                    break;

                case 3:
                    text += "=";
                    break;
            }

            return Encoding.UTF8.GetString(Convert.FromBase64String(text));
        }

        private static string GetString(JsonElement root, string name)
        {
            return root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        private static int GetInt(JsonElement root, string name)
        {
            return root.TryGetProperty(name, out var value) && value.TryGetInt32(out var number)
                ? number
                : 0;
        }

        private static long GetLong(JsonElement root, string name)
        {
            return root.TryGetProperty(name, out var value) && value.TryGetInt64(out var number)
                ? number
                : 0;
        }
    }
}
