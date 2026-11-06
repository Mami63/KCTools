using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using KCTools.Common;

namespace KCTools.Protocol
{
    public enum FirehoseAction
    {
        Nop,
        Configure,
        Read,
        Write,
        Erase,
        Patch,
        Power,
        GetStorageInfo
    }

    /// <summary>Firehose komut parametreleri.</summary>
    public sealed class FirehoseRequest
    {
        public FirehoseAction Action { get; set; } = FirehoseAction.Nop;

        public string FileName { get; set; } = "partition.bin";

        public int PhysicalPartition { get; set; }

        public long StartSector { get; set; }

        /// <summary>read/erase için işlenecek sektör sayısı (num_partitions).</summary>
        public int SectorCount { get; set; } = 1;

        public int SectorSize { get; set; } = 512;

        public string StorageType { get; set; } = "ufs";

        public int MaxPayloadSize { get; set; } = 1048576;

        public bool SkipStorageInit { get; set; }

        public int PatchByteOffset { get; set; }

        public int PatchSizeInBytes { get; set; } = 4;

        public string PatchValue { get; set; } = "0x0";

        public string PowerValue { get; set; } = "reset";
    }

    /// <summary>
    /// Firehose XML paket üreticisi. Tüm paketler Qualcomm Firehose protokolünde
    /// beklenen öznitelik adları ve sırasıyla üretilir; sayılar invariant kültürdedir.
    /// </summary>
    public static class FirehoseXml
    {
        public static string Build(FirehoseRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            switch (request.Action)
            {
                case FirehoseAction.Configure:
                    return Configure(request.StorageType, request.MaxPayloadSize, request.SkipStorageInit);

                case FirehoseAction.Read:
                    return Read(
                        request.FileName,
                        request.PhysicalPartition,
                        request.StartSector,
                        request.SectorSize,
                        request.SectorCount);

                case FirehoseAction.Write:
                    return Write(
                        request.FileName,
                        request.PhysicalPartition,
                        request.StartSector,
                        request.SectorSize);

                case FirehoseAction.Erase:
                    return Erase(
                        request.PhysicalPartition,
                        request.StartSector,
                        request.SectorSize,
                        request.SectorCount);

                case FirehoseAction.Patch:
                    return Patch(
                        request.FileName,
                        request.PhysicalPartition,
                        request.StartSector,
                        request.SectorSize,
                        request.PatchByteOffset,
                        request.PatchSizeInBytes,
                        request.PatchValue);

                case FirehoseAction.Power:
                    return Power(request.PowerValue);

                case FirehoseAction.GetStorageInfo:
                    return "<getstorageinfo />";

                default:
                    return Nop();
            }
        }

        public static string Nop()
        {
            return "<nop TARGET_DISPLAY_DELAY_IN_SECONDS=\"0\" />";
        }

        public static string Configure(string storageType = "ufs", int maxPayloadSize = 1048576, bool skipStorageInit = false)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "<configure MemoryName=\"{0}\" Verbose=\"0\" AlwaysValidate=\"0\" " +
                "MaxDigestTableSizeInBytes=\"8192\" MaxPayloadSizeToTargetInBytes=\"{1}\" " +
                "ZlpAwareHost=\"1\" SkipStorageInit=\"{2}\" />",
                Escape(storageType ?? "ufs"),
                Math.Max(4096, maxPayloadSize),
                skipStorageInit ? 1 : 0);
        }

        /// <summary>num_partitions, okunacak sektör sayısıdır.</summary>
        public static string Read(
            string fileName,
            int physicalPartition = 0,
            long startSector = 0,
            int sectorSize = 512,
            int sectorCount = 1)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "<read SECTOR_SIZE_IN_BYTES=\"{0}\" filename=\"{1}\" physical_partition_number=\"{2}\" " +
                "start_sector=\"{3}\" num_partitions=\"{4}\" />",
                NormalizeSectorSize(sectorSize),
                Escape(fileName ?? "partition.bin"),
                Math.Max(0, physicalPartition),
                Math.Max(0, startSector),
                Math.Max(1, sectorCount));
        }

        public static string Write(
            string fileName,
            int physicalPartition = 0,
            long startSector = 0,
            int sectorSize = 512)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "<write SECTOR_SIZE_IN_BYTES=\"{0}\" filename=\"{1}\" physical_partition_number=\"{2}\" " +
                "start_sector=\"{3}\" num_partitions=\"0\" />",
                NormalizeSectorSize(sectorSize),
                Escape(fileName ?? "partition.bin"),
                Math.Max(0, physicalPartition),
                Math.Max(0, startSector));
        }

        public static string Erase(
            int physicalPartition,
            long startSector,
            int sectorSize = 512,
            int sectorCount = 1)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "<erase SECTOR_SIZE_IN_BYTES=\"{0}\" physical_partition_number=\"{1}\" " +
                "start_sector=\"{2}\" num_partitions=\"{3}\" />",
                NormalizeSectorSize(sectorSize),
                Math.Max(0, physicalPartition),
                Math.Max(0, startSector),
                Math.Max(1, sectorCount));
        }

        public static string Patch(
            string fileName,
            int physicalPartition,
            long startSector,
            int sectorSize,
            int byteOffset,
            int sizeInBytes,
            string value)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "<patch SECTOR_SIZE_IN_BYTES=\"{0}\" byte_offset=\"{1}\" filename=\"{2}\" " +
                "physical_partition_number=\"{3}\" size_in_bytes=\"{4}\" start_sector=\"{5}\" value=\"{6}\" />",
                NormalizeSectorSize(sectorSize),
                Math.Max(0, byteOffset),
                Escape(fileName ?? "patch.bin"),
                Math.Max(0, physicalPartition),
                Math.Max(1, sizeInBytes),
                Math.Max(0, startSector),
                Escape(string.IsNullOrWhiteSpace(value) ? "0x0" : value));
        }

        public static string Power(string value = "reset")
        {
            return "<power value=\"" + Escape(string.IsNullOrWhiteSpace(value) ? "reset" : value) + "\" />";
        }

        /// <summary>Write akışında gönderilen hex veri paketi.</summary>
        public static string DataPacket(byte[] data)
        {
            return "<data>" + Hex.ToHex(data, string.Empty) + "</data>";
        }

        public static string Response(string value, bool rawMode = false, string error = null)
        {
            var text = string.Format(
                CultureInfo.InvariantCulture,
                "<response value=\"{0}\" rawmode=\"{1}\"",
                Escape(value ?? "NAK"),
                rawMode ? "true" : "false");

            if (!string.IsNullOrWhiteSpace(error))
            {
                text += " error=\"" + Escape(error) + "\"";
            }

            return text + " />";
        }

        public static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
        }

        public static int NormalizeSectorSize(int sectorSize)
        {
            return sectorSize == 4096 ? 4096 : 512;
        }
    }

    /// <summary>Firehose adresleme ve hex veri işleme yardımcıları.</summary>
    public static class FirehoseSectors
    {
        public static long BytesToSectors(long bytes, int sectorSize)
        {
            if (bytes <= 0)
            {
                return 0;
            }

            var size = FirehoseXml.NormalizeSectorSize(sectorSize);
            return (bytes + size - 1) / size;
        }

        public static long SectorsToBytes(long sectors, int sectorSize)
        {
            return Math.Max(0, sectors) * FirehoseXml.NormalizeSectorSize(sectorSize);
        }

        public static int AlignedLength(int length, int sectorSize)
        {
            if (length <= 0)
            {
                return 0;
            }

            var size = FirehoseXml.NormalizeSectorSize(sectorSize);
            return ((length + size - 1) / size) * size;
        }

        /// <summary>Son sektörü sıfırla doldurur (Firehose sektör hizalı veri bekler).</summary>
        public static byte[] PadToSector(byte[] data, int sectorSize)
        {
            if (data == null || data.Length == 0)
            {
                return Array.Empty<byte>();
            }

            var target = AlignedLength(data.Length, sectorSize);

            if (target == data.Length)
            {
                return data;
            }

            var padded = new byte[target];
            Buffer.BlockCopy(data, 0, padded, 0, data.Length);
            return padded;
        }

        /// <summary>Veriyi sektör hizalı, en fazla maxPayload baytlık parçalara böler.</summary>
        public static List<FirehoseChunk> Chunk(
            byte[] data,
            int sectorSize,
            int maxPayloadBytes,
            long startSector = 0)
        {
            var chunks = new List<FirehoseChunk>();

            if (data == null || data.Length == 0)
            {
                return chunks;
            }

            var size = FirehoseXml.NormalizeSectorSize(sectorSize);
            var payload = Math.Max(size, (maxPayloadBytes / size) * size);
            var offset = 0;

            while (offset < data.Length)
            {
                var length = Math.Min(payload, data.Length - offset);
                var buffer = new byte[length];
                Buffer.BlockCopy(data, offset, buffer, 0, length);

                var padded = PadToSector(buffer, size);
                var sector = startSector + (offset / size);

                chunks.Add(new FirehoseChunk
                {
                    StartSector = sector,
                    OriginalLength = length,
                    Data = padded
                });

                offset += length;
            }

            return chunks;
        }

        public static string ToHex(byte[] data)
        {
            return Hex.ToHex(data ?? Array.Empty<byte>(), string.Empty);
        }

        public static byte[] FromHex(string hex)
        {
            return Hex.Parse(hex);
        }

        /// <summary>rawprogram_unsparse.xml dosyasındaki &lt;program&gt; kayıtlarını okur.</summary>
        public static List<FirehoseProgramEntry> ParseRawProgram(string xmlText)
        {
            var entries = new List<FirehoseProgramEntry>();

            if (string.IsNullOrWhiteSpace(xmlText))
            {
                return entries;
            }

            XDocument document;

            try
            {
                document = XDocument.Parse(xmlText);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("rawprogram XML ayrıştırılamadı: " + ex.Message, ex);
            }

            foreach (var element in document.Descendants().Where(e => e.Name.LocalName == "program"))
            {
                var entry = new FirehoseProgramEntry
                {
                    Label = Attribute(element, "label") ?? "—",
                    FileName = Attribute(element, "filename") ?? string.Empty,
                    PhysicalPartition = ToInt(Attribute(element, "physical_partition_number"), 0),
                    StartSector = ToLong(Attribute(element, "start_sector"), 0),
                    SectorSize = FirehoseXml.NormalizeSectorSize(ToInt(Attribute(element, "SECTOR_SIZE_IN_BYTES"), 512)),
                    SectorCount = ToLong(Attribute(element, "num_partitions"), 0)
                };

                if (!string.IsNullOrWhiteSpace(entry.FileName))
                {
                    entries.Add(entry);
                }
            }

            return entries;
        }

        private static string Attribute(XElement element, string name)
        {
            var attribute = element.Attributes()
                .FirstOrDefault(a => string.Equals(a.Name.LocalName, name, StringComparison.OrdinalIgnoreCase));

            return attribute?.Value;
        }

        private static int ToInt(string value, int fallback)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
                ? result
                : fallback;
        }

        private static long ToLong(string value, long fallback)
        {
            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
                ? result
                : fallback;
        }
    }

    public sealed class FirehoseChunk
    {
        public long StartSector { get; set; }

        public int OriginalLength { get; set; }

        public byte[] Data { get; set; } = Array.Empty<byte>();

        public int PaddedLength => Data?.Length ?? 0;
    }

    public sealed class FirehoseProgramEntry
    {
        public string Label { get; set; } = "—";

        public string FileName { get; set; } = string.Empty;

        public int PhysicalPartition { get; set; }

        public long StartSector { get; set; }

        public long SectorCount { get; set; }

        public int SectorSize { get; set; } = 512;

        public long ByteOffset => StartSector * SectorSize;

        public override string ToString() =>
            Label + " · " + FileName + " · sektör " + StartSector + " · bölüm " + PhysicalPartition;
    }
}
