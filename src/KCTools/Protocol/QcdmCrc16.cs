using System;

namespace KCTools.Protocol
{
    /// <summary>CRC-16/CCITT (Kermit): poly 0x1021 yansıtılmış (0x8408), init 0, LSB-first.</summary>
    public static class QcdmCrc16
    {
        public static ushort Compute(ReadOnlySpan<byte> data)
        {
            ushort crc = 0;

            foreach (var value in data)
            {
                crc ^= value;

                for (var bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 0x0001) != 0
                        ? (ushort)((crc >> 1) ^ 0x8408)
                        : (ushort)(crc >> 1);
                }
            }

            return crc;
        }

        public static bool Verify(ReadOnlySpan<byte> data, ushort expected)
        {
            return Compute(data) == expected;
        }
    }
}
