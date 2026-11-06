using System;
using System.Collections.Generic;
using System.IO;

namespace KCTools.Protocol
{
    public sealed class QcdmFrame
    {
        public byte Command { get; set; }

        public byte[] Payload { get; set; } = Array.Empty<byte>();

        public ushort Crc { get; set; }

        public bool CrcValid { get; set; }
    }

    /// <summary>
    /// QCDM/HDLC çerçeveleme: 0x7E bayrak, 0x7D kaçış (XOR 0x20),
    /// CRC-16/Kermit (LSB-first, CRC alçak bayt önce).
    /// </summary>
    public static class QcdmFramer
    {
        public const byte Flag = 0x7E;
        public const byte Escape = 0x7D;
        public const byte EscapeXor = 0x20;

        /// <summary>Encode için kısa ad; çerçeveleme API'si.</summary>
        public static byte[] Frame(byte command, byte[] payload, bool escapeControl = true)
        {
            return Encode(command, payload, escapeControl);
        }

        public static byte[] Encode(byte command, byte[] payload, bool escapeControl = true)
        {
            payload ??= Array.Empty<byte>();

            var body = new byte[1 + payload.Length + 2];
            body[0] = command;
            Buffer.BlockCopy(payload, 0, body, 1, payload.Length);

            var crc = QcdmCrc16.Compute(body.AsSpan(0, 1 + payload.Length));
            body[body.Length - 2] = (byte)(crc & 0xFF);
            body[body.Length - 1] = (byte)(crc >> 8);

            var output = new List<byte>(body.Length + 4) { Flag };

            foreach (var value in body)
            {
                if (value == Flag || value == Escape || (escapeControl && value < 0x20))
                {
                    output.Add(Escape);
                    output.Add((byte)(value ^ EscapeXor));
                }
                else
                {
                    output.Add(value);
                }
            }

            output.Add(Flag);

            return output.ToArray();
        }

        /// <summary>Ham çerçeveden (bayraklar dahil) komut/yük çıkarır; CRC doğrular.</summary>
        public static bool TryDecode(byte[] frame, out QcdmFrame decoded, out string error)
        {
            decoded = null;
            error = null;

            if (frame == null || frame.Length < 6)
            {
                error = "Çerçeve çok kısa.";
                return false;
            }

            var start = Array.IndexOf(frame, Flag);
            var end = Array.LastIndexOf(frame, Flag);

            if (start < 0 || end <= start + 1)
            {
                error = "0x7E bayrakları bulunamadı.";
                return false;
            }

            var buffer = new List<byte>(end - start);
            var escaped = false;

            for (var i = start + 1; i < end; i++)
            {
                var value = frame[i];

                if (escaped)
                {
                    buffer.Add((byte)(value ^ EscapeXor));
                    escaped = false;
                }
                else if (value == Escape)
                {
                    escaped = true;
                }
                else if (value == Flag)
                {
                    error = "Çerçeve içinde beklenmeyen bayrak.";
                    return false;
                }
                else
                {
                    buffer.Add(value);
                }
            }

            if (escaped || buffer.Count < 3)
            {
                error = "Kaçış dizisi veya çerçeve uzunluğu geçersiz.";
                return false;
            }

            var body = buffer.ToArray();
            var crcExpected = (ushort)(body[body.Length - 2] | (body[body.Length - 1] << 8));
            var crcActual = QcdmCrc16.Compute(body.AsSpan(0, body.Length - 2));

            if (crcExpected != crcActual)
            {
                error = "CRC uyuşmuyor.";
                return false;
            }

            var payload = new byte[body.Length - 3];
            Buffer.BlockCopy(body, 1, payload, 0, payload.Length);

            decoded = new QcdmFrame
            {
                Command = body[0],
                Payload = payload,
                Crc = crcActual,
                CrcValid = true
            };

            return true;
        }

        public static bool TryDecode(Stream stream, out QcdmFrame decoded, out string error)
        {
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return TryDecode(memory.ToArray(), out decoded, out error);
        }
    }
}
