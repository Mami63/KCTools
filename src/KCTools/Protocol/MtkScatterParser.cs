using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace KCTools.Protocol
{
    public sealed class MtkPartition
    {
        public int Index { get; set; }

        public string Name { get; set; } = string.Empty;

        public string FileName { get; set; } = string.Empty;

        public bool IsDownload { get; set; }

        public string Type { get; set; } = string.Empty;

        public long LinearStartAddress { get; set; }

        public long PhysicalStartAddress { get; set; }

        public long Size { get; set; }

        public string Region { get; set; } = string.Empty;

        public string Storage { get; set; } = string.Empty;

        public bool BoundaryCheck { get; set; }

        public bool IsReserved { get; set; }

        public string OperationType { get; set; } = string.Empty;

        public bool HasImage(string romDirectory)
        {
            return !string.IsNullOrWhiteSpace(FileName) &&
                   File.Exists(Path.Combine(romDirectory, FileName));
        }
    }

    public sealed class MtkScatterDocument
    {
        public string Platform { get; set; } = "MTK";

        public string SourceFile { get; set; } = string.Empty;

        public List<MtkPartition> Partitions { get; } = new();

        public IEnumerable<MtkPartition> DownloadablePartitions =>
            Partitions.FindAll(item => item.IsDownload && !string.IsNullOrWhiteSpace(item.FileName));

        public IEnumerable<MtkPartition> ResolveImages(string romDirectory)
        {
            foreach (var partition in DownloadablePartitions)
            {
                if (partition.HasImage(romDirectory))
                {
                    yield return partition;
                }
            }
        }
    }

    /// <summary>MTK scatter dosyası (MT67xx_Android_scatter.txt) ayrıştırıcısı.</summary>
    public static class MtkScatterParser
    {
        private static readonly Regex PlatformPattern =
            new Regex(@"MT\d{4}[A-Za-z0-9]*", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static MtkScatterDocument ParseFile(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Scatter dosyası bulunamadı.", path);
            }

            var document = Parse(File.ReadAllText(path));
            document.SourceFile = path;

            if (string.IsNullOrWhiteSpace(document.Platform) ||
                document.Platform == "MTK")
            {
                var match = PlatformPattern.Match(Path.GetFileName(path));

                if (match.Success)
                {
                    document.Platform = match.Value.ToUpperInvariant();
                }
            }

            return document;
        }

        public static MtkScatterDocument Parse(string text)
        {
            var document = new MtkScatterDocument();
            MtkPartition current = null;

            foreach (var rawLine in (text ?? string.Empty).Split('\n'))
            {
                var line = rawLine.Trim().TrimEnd('\r');

                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                if (line.StartsWith("-", StringComparison.Ordinal))
                {
                    line = line.Substring(1).Trim();
                    current = new MtkPartition();
                    document.Partitions.Add(current);
                }

                var separator = line.IndexOf(':');

                if (separator <= 0)
                {
                    continue;
                }

                var key = line.Substring(0, separator).Trim();
                var value = line.Substring(separator + 1).Trim();

                if (key.Equals("platform", StringComparison.OrdinalIgnoreCase))
                {
                    document.Platform = value.ToUpperInvariant();
                    continue;
                }

                if (current == null)
                {
                    continue;
                }

                Apply(current, key, value);
            }

            return document;
        }

        private static void Apply(MtkPartition partition, string key, string value)
        {
            switch (key.ToLowerInvariant())
            {
                case "partition_index":
                    partition.Index = (int)ToNumber(value);
                    break;
                case "partition_name":
                    partition.Name = value;
                    break;
                case "file_name":
                    partition.FileName = value;
                    break;
                case "is_download":
                    partition.IsDownload = ToBool(value);
                    break;
                case "type":
                    partition.Type = value;
                    break;
                case "linear_start_addr":
                    partition.LinearStartAddress = ToNumber(value);
                    break;
                case "physical_start_addr":
                    partition.PhysicalStartAddress = ToNumber(value);
                    break;
                case "partition_size":
                    partition.Size = ToNumber(value);
                    break;
                case "region":
                    partition.Region = value;
                    break;
                case "storage":
                    partition.Storage = value;
                    break;
                case "boundary_check":
                    partition.BoundaryCheck = ToBool(value);
                    break;
                case "is_reserved":
                    partition.IsReserved = ToBool(value);
                    break;
                case "operation_type":
                    partition.OperationType = value;
                    break;
            }
        }

        private static long ToNumber(string value)
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

        private static bool ToBool(string value)
        {
            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "1", StringComparison.Ordinal);
        }
    }
}
