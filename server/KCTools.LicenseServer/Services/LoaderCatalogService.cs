using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace KCTools.LicenseServer.Services
{
    public sealed class LoaderInfo
    {
        public string Chipset { get; set; } = string.Empty;

        public string FileName { get; set; } = string.Empty;

        public string Platform { get; set; } = string.Empty;

        public long SizeBytes { get; set; }

        public string Sha256 { get; set; } = string.Empty;
    }

    /// <summary>
    /// Loader dosyalarını dosya sisteminden kataloglar. Dosyalar DB'ye yazılmaz;
    /// yalnızca metadata ve indirme yolu sunulur.
    /// </summary>
    public sealed class LoaderCatalogService
    {
        private static readonly Regex ChipsetPattern =
            new Regex(@"^[A-Za-z0-9._-]{2,64}$", RegexOptions.Compiled);

        private static readonly string[] Extensions = { ".mbn", ".elf", ".bin" };

        private readonly string _root;
        private readonly ILogger<LoaderCatalogService> _logger;

        public LoaderCatalogService(string root, ILogger<LoaderCatalogService> logger)
        {
            _root = Path.GetFullPath(root);
            _logger = logger;
        }

        public string RootPath => _root;

        public IReadOnlyList<LoaderInfo> List(string chipset)
        {
            var directory = ResolveDirectory(chipset);

            if (!Directory.Exists(directory))
            {
                return Array.Empty<LoaderInfo>();
            }

            var result = new List<LoaderInfo>();

            foreach (var path in Directory.EnumerateFiles(directory))
            {
                var extension = Path.GetExtension(path);

                if (!Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                result.Add(new LoaderInfo
                {
                    Chipset = chipset.ToUpperInvariant(),
                    FileName = Path.GetFileName(path),
                    Platform = extension.Equals(".bin", StringComparison.OrdinalIgnoreCase)
                        ? "mtk"
                        : "qualcomm",
                    SizeBytes = new FileInfo(path).Length,
                    Sha256 = ComputeSha256(path)
                });
            }

            return result.OrderBy(item => item.FileName).ToList();
        }

        public string? FindFile(string chipset, string fileName, out LoaderInfo? info)
        {
            info = null;

            if (string.IsNullOrWhiteSpace(fileName) ||
                fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                fileName.Contains("..", StringComparison.Ordinal))
            {
                return null;
            }

            var directory = ResolveDirectory(chipset);
            var path = Path.Combine(directory, fileName);

            if (!File.Exists(path))
            {
                return null;
            }

            var full = Path.GetFullPath(path);

            if (!full.StartsWith(directory + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            info = List(chipset).FirstOrDefault(item =>
                string.Equals(item.FileName, fileName, StringComparison.OrdinalIgnoreCase));

            return info == null ? null : full;
        }

        private string ResolveDirectory(string chipset)
        {
            if (string.IsNullOrWhiteSpace(chipset) || !ChipsetPattern.IsMatch(chipset))
            {
                throw new ArgumentException("Geçersiz chipset adı.", nameof(chipset));
            }

            var directory = Path.GetFullPath(Path.Combine(_root, chipset));

            if (!directory.StartsWith(_root + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(directory, _root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Loader yolu kök dizin dışında.");
            }

            return directory;
        }

        private static string ComputeSha256(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
    }
}
