using System;
using System.Collections.Generic;
using System.Text;

namespace KCTools.Protocol
{
    /// <summary>MTK Download Agent (DA) dosya başlığı bilgisi.</summary>
    public sealed class MtkDaHeader
    {
        public bool IsValid { get; set; }

        public string Signature { get; set; } = "—";

        public int Version { get; set; }

        public int Size { get; set; }

        public ushort Checksum { get; set; }

        public string Note { get; set; } = string.Empty;

        public string Summary =>
            (IsValid ? "DA (MMM)" : "DA değil") + " · " + Size.ToString("N0") + " bayt · sürüm " +
            Version + " · checksum 0x" + Checksum.ToString("X4");
    }

    /// <summary>DA yükleme planındaki tek blok.</summary>
    public sealed class MtkDaBlock
    {
        public int Index { get; set; }

        public long Address { get; set; }

        public long Offset { get; set; }

        public int Length { get; set; }

        public ushort Checksum { get; set; }

        public override string ToString() =>
            "[" + Index.ToString("D3") + "] 0x" + Address.ToString("X8") + " · " +
            Length + " bayt · 0x" + Checksum.ToString("X4");
    }

    /// <summary>
    /// MTK BROM katmanının veri tarafı: DA dosya doğrulama, blok planı ve
    /// 16 bit checksum (toplam + ikiye tümleyen). USB/BROM aktarımı 6. parçada.
    /// </summary>
    public static class MtkBromTools
    {
        public const int DefaultBlockSize = 4096;

        /// <summary>MTK checksum: bayt toplamının ikiye tümleyeni (16 bit).</summary>
        public static ushort Checksum(byte[] data, int offset = 0, int count = -1)
        {
            if (data == null || data.Length == 0)
            {
                return 0;
            }

            var start = Math.Max(0, offset);
            var length = count < 0 ? data.Length - start : Math.Min(count, data.Length - start);
            uint sum = 0;

            for (var i = 0; i < length; i++)
            {
                sum += data[start + i];
            }

            return (ushort)((~sum + 1) & 0xFFFF);
        }

        /// <summary>DA dosyasını inceler: "MMM" imzası, sürüm, boyut ve checksum.</summary>
        public static MtkDaHeader InspectDa(byte[] data)
        {
            var header = new MtkDaHeader
            {
                Size = data?.Length ?? 0,
                Signature = "—"
            };

            if (data == null || data.Length < 8)
            {
                header.Note = "Dosya çok küçük veya boş.";
                return header;
            }

            header.Signature = Encoding.ASCII.GetString(data, 0, 3);
            header.IsValid = string.Equals(header.Signature, "MMM", StringComparison.Ordinal);
            header.Version = data[3];
            header.Checksum = Checksum(data);
            header.Note = header.IsValid
                ? "MTK Download Agent imzası doğrulandı."
                : "MMM imzası bulunamadı; bu dosya DA olmayabilir.";

            return header;
        }

        /// <summary>DA'yı BROM'a gönderilecek bloklara böler ve her bloğun checksum'ını hesaplar.</summary>
        public static List<MtkDaBlock> PlanUpload(
            byte[] data,
            int blockSize = DefaultBlockSize,
            long baseAddress = 0)
        {
            var blocks = new List<MtkDaBlock>();

            if (data == null || data.Length == 0)
            {
                return blocks;
            }

            var size = Math.Max(512, blockSize);

            for (var offset = 0; offset < data.Length; offset += size)
            {
                var length = Math.Min(size, data.Length - offset);
                var buffer = new byte[length];
                Buffer.BlockCopy(data, offset, buffer, 0, length);

                blocks.Add(new MtkDaBlock
                {
                    Index = blocks.Count,
                    Address = baseAddress + offset,
                    Offset = offset,
                    Length = length,
                    Checksum = Checksum(buffer)
                });
            }

            return blocks;
        }

        public static string DescribePlan(IReadOnlyList<MtkDaBlock> blocks, int maxLines = 4)
        {
            if (blocks == null || blocks.Count == 0)
            {
                return "Blok planı boş.";
            }

            var builder = new StringBuilder();
            builder.Append(blocks.Count).Append(" blok · toplam ")
                .Append(blocks[blocks.Count - 1].Address + blocks[blocks.Count - 1].Length)
                .Append(" bayt");

            var count = Math.Min(maxLines, blocks.Count);

            for (var i = 0; i < count; i++)
            {
                builder.AppendLine().Append(blocks[i]);
            }

            if (blocks.Count > count)
            {
                builder.AppendLine().Append("…");
            }

            return builder.ToString();
        }
    }
}
