using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace KCTools.Common
{
    /// <summary>Hex metin dönüşümleri ("7E 00 1A", "7E001A", "0x7E,0x00" biçimleri).</summary>
    public static class Hex
    {
        public static string ToHex(byte[] data, string separator = " ")
        {
            if (data == null || data.Length == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder(data.Length * 3);

            for (var i = 0; i < data.Length; i++)
            {
                if (i > 0 && !string.IsNullOrEmpty(separator))
                {
                    builder.Append(separator);
                }

                builder.Append(data[i].ToString("X2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        public static string ToHex(byte[] data, int offset, int count, string separator = " ")
        {
            if (data == null || offset < 0 || count <= 0 || offset >= data.Length)
            {
                return string.Empty;
            }

            var length = Math.Min(count, data.Length - offset);
            var slice = new byte[length];
            Buffer.BlockCopy(data, offset, slice, 0, length);
            return ToHex(slice, separator);
        }

        public static byte[] Parse(string text)
        {
            var bytes = new List<byte>();

            if (string.IsNullOrWhiteSpace(text))
            {
                return bytes.ToArray();
            }

            var cleaned = text
                .Replace("0x", string.Empty)
                .Replace("0X", string.Empty)
                .Replace(",", " ")
                .Replace("-", " ")
                .Replace(":", " ");

            var tokens = cleaned.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var token in tokens)
            {
                if (token.Length == 1)
                {
                    bytes.Add(byte.Parse(token + "0", NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                }
                else
                {
                    for (var i = 0; i + 1 < token.Length; i += 2)
                    {
                        bytes.Add(byte.Parse(token.Substring(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    }
                }
            }

            return bytes.ToArray();
        }

        public static bool TryParse(string text, out byte[] bytes)
        {
            try
            {
                bytes = Parse(text);
                return bytes.Length > 0;
            }
            catch (Exception)
            {
                bytes = Array.Empty<byte>();
                return false;
            }
        }

        /// <summary>IMEI'yi QCN/EFS dosyalarında görülen BCD biçimine çevirir.</summary>
        public static byte[] ImeiToBcd(string imei)
        {
            var digits = new List<byte>();

            foreach (var c in imei ?? string.Empty)
            {
                if (char.IsDigit(c))
                {
                    digits.Add((byte)(c - '0'));
                }
            }

            var result = new List<byte>();

            for (var i = 0; i < digits.Count; i += 2)
            {
                var low = digits[i];
                var high = i + 1 < digits.Count ? digits[i + 1] : (byte)0x0F;
                result.Add((byte)((high << 4) | low));
            }

            return result.ToArray();
        }

        public static string Ascii(byte[] data, int offset, int count)
        {
            if (data == null || offset < 0 || count <= 0 || offset >= data.Length)
            {
                return string.Empty;
            }

            var length = Math.Min(count, data.Length - offset);
            var builder = new StringBuilder(length);

            for (var i = 0; i < length; i++)
            {
                var b = data[offset + i];
                builder.Append(b >= 32 && b < 127 ? (char)b : '.');
            }

            return builder.ToString();
        }
    }
}
