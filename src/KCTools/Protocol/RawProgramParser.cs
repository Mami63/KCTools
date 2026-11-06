using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace KCTools.Protocol
{
    public sealed class RawProgramEntry
    {
        public string Label { get; set; } = string.Empty;

        public string Filename { get; set; } = string.Empty;

        public long StartSector { get; set; }

        public long NumSectors { get; set; }

        public int PhysicalPartition { get; set; }

        public string StartByteHex { get; set; }

        public bool IsSparse =>
            Filename.EndsWith(".sparse", StringComparison.OrdinalIgnoreCase) ||
            Filename.Contains("sparse", StringComparison.OrdinalIgnoreCase);

        public long? ResolveSize(string romDirectory)
        {
            if (string.IsNullOrWhiteSpace(Filename))
            {
                return null;
            }

            var path = Path.Combine(romDirectory, Filename);
            return File.Exists(path) ? new FileInfo(path).Length : null;
        }
    }

    public sealed class PatchEntry
    {
        public string What { get; set; } = string.Empty;

        public string Filename { get; set; } = string.Empty;

        public string Value { get; set; } = string.Empty;

        public long StartSector { get; set; }

        public int PhysicalPartition { get; set; }

        public int? SizeInBytes { get; set; }

        public int? ByteOffset { get; set; }

        public bool IsCrc => What.IndexOf("CRC", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public sealed class RawProgramPlan
    {
        public List<RawProgramEntry> Entries { get; } = new();

        public List<RawProgramFile> Files { get; } = new();

        public List<string> Skipped { get; } = new();
    }

    public sealed class RawProgramFile
    {
        public RawProgramEntry Entry { get; set; }

        public string FullPath { get; set; }

        public long Length { get; set; }
    }

    /// <summary>
    /// rawprogram*.xml ve patch*.xml ayrıştırıcısı. Firehose komutlarını
    /// üretmez; doğrulanmış planı döndürür.
    /// </summary>
    public static class RawProgramParser
    {
        public static IReadOnlyList<RawProgramEntry> ParsePrograms(string xml)
        {
            var document = XDocument.Parse(xml);
            var result = new List<RawProgramEntry>();

            foreach (var element in document.Descendants()
                         .Where(item => item.Name.LocalName.Equals("program", StringComparison.OrdinalIgnoreCase)))
            {
                result.Add(new RawProgramEntry
                {
                    Label = Attribute(element, "label") ?? string.Empty,
                    Filename = Attribute(element, "filename") ?? string.Empty,
                    StartSector = ToLong(Attribute(element, "start_sector")),
                    NumSectors = ToLong(Attribute(element, "num_partition_sectors")),
                    PhysicalPartition = (int)ToLong(Attribute(element, "physical_partition_number")),
                    StartByteHex = Attribute(element, "start_byte_hex")
                });
            }

            return result;
        }

        public static IReadOnlyList<PatchEntry> ParsePatches(string xml)
        {
            var document = XDocument.Parse(xml);
            var result = new List<PatchEntry>();

            foreach (var element in document.Descendants()
                         .Where(item => item.Name.LocalName.Equals("patch", StringComparison.OrdinalIgnoreCase)))
            {
                result.Add(new PatchEntry
                {
                    What = Attribute(element, "what") ?? string.Empty,
                    Filename = Attribute(element, "filename") ?? string.Empty,
                    Value = Attribute(element, "value") ?? string.Empty,
                    StartSector = ToLong(Attribute(element, "start_sector")),
                    PhysicalPartition = (int)ToLong(Attribute(element, "physical_partition_number")),
                    SizeInBytes = ToNullableInt(Attribute(element, "size_in_bytes")),
                    ByteOffset = ToNullableInt(Attribute(element, "byte_offset"))
                });
            }

            return result;
        }

        /// <summary>
        /// Eksik veya boş dosyaları atlayan Firehose planı. Boş filename
        /// (ör. GPT/boş bölüm) atlanır; sparse dosyalar Firehose'a olduğu gibi bırakılır.
        /// </summary>
        public static RawProgramPlan BuildPlan(string xml, string romDirectory)
        {
            if (string.IsNullOrWhiteSpace(romDirectory))
            {
                throw new ArgumentException("ROM klasörü gerekli.", nameof(romDirectory));
            }

            var plan = new RawProgramPlan();

            foreach (var entry in ParsePrograms(xml))
            {
                plan.Entries.Add(entry);

                if (string.IsNullOrWhiteSpace(entry.Filename))
                {
                    plan.Skipped.Add(entry.Label + " (dosya adı boş)");
                    continue;
                }

                var path = Path.Combine(romDirectory, entry.Filename);

                if (!File.Exists(path))
                {
                    plan.Skipped.Add(entry.Label + " (" + entry.Filename + " yok)");
                    continue;
                }

                var length = new FileInfo(path).Length;

                if (length == 0)
                {
                    plan.Skipped.Add(entry.Label + " (" + entry.Filename + " boş)");
                    continue;
                }

                plan.Files.Add(new RawProgramFile
                {
                    Entry = entry,
                    FullPath = path,
                    Length = length
                });
            }

            return plan;
        }

        private static string Attribute(XElement element, string name)
        {
            foreach (var attribute in element.Attributes())
            {
                if (string.Equals(attribute.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))
                {
                    return attribute.Value;
                }
            }

            return null;
        }

        private static long ToLong(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return 0;
            }

            value = value.Trim();

            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return long.TryParse(value.Substring(2), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out var hex) ? hex : 0;
            }

            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                ? number
                : 0;
        }

        private static int? ToNullableInt(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return (int)ToLong(value);
        }
    }
}
