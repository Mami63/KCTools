using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KCTools.Services
{
    public sealed class LoaderDescriptor
    {
        public string Chipset { get; set; } = string.Empty;

        public string FileName { get; set; } = string.Empty;

        public string Platform { get; set; } = string.Empty;

        public long SizeBytes { get; set; }

        public string Sha256 { get; set; } = string.Empty;
    }

    /// <summary>
    /// Sunucu loader kataloğundan chipsete uygun dosyayı indirir, SHA-256 doğrular
    /// ve yerel önbelleğe alır. Yerel dosya varsa ağa çıkmaz.
    /// </summary>
    public sealed class LoaderRepositoryService : IDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly HttpClient _http;
        private readonly string _apiBase;
        private readonly string _cacheRoot;

        public string LastCachedPath { get; private set; }

        public LoaderRepositoryService(
            string apiBase, string cacheRoot = null, HttpClient httpClient = null)
        {
            _apiBase = (apiBase ?? throw new ArgumentNullException(nameof(apiBase))).TrimEnd('/');
            _cacheRoot = cacheRoot ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "KCTools", "loaders");

            _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        }

        public async Task<IReadOnlyList<LoaderDescriptor>> ListAsync(
            string chipset, string accessToken = null, CancellationToken cancellationToken = default)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get, _apiBase + "/api/v1/loaders/" + Uri.EscapeDataString(chipset));

            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }

            using var response = await _http.SendAsync(request, cancellationToken)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            return JsonSerializer.Deserialize<List<LoaderDescriptor>>(json, JsonOptions)
                   ?? new List<LoaderDescriptor>();
        }

        public async Task<byte[]> GetLoaderAsync(
            string chipset,
            string localPath = null,
            string accessToken = null,
            CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrWhiteSpace(localPath) && File.Exists(localPath))
            {
                var local = await File.ReadAllBytesAsync(localPath, cancellationToken)
                    .ConfigureAwait(false);

                if (local.Length > 0)
                {
                    return local;
                }
            }

            var loaders = await ListAsync(chipset, accessToken, cancellationToken)
                .ConfigureAwait(false);

            if (loaders.Count == 0)
            {
                throw new FileNotFoundException(
                    "Sunucuda bu chipset için loader yok: " + chipset);
            }

            var chosen = loaders.FirstOrDefault(item =>
                             item.Platform.Equals("qualcomm", StringComparison.OrdinalIgnoreCase))
                         ?? loaders[0];

            var bytes = await DownloadAsync(chipset, chosen, accessToken, cancellationToken)
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(chosen.Sha256))
            {
                var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

                if (!string.Equals(actual, chosen.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("Loader SHA-256 doğrulaması başarısız.");
                }
            }

            await CacheAsync(chipset, chosen.FileName, bytes, cancellationToken)
                .ConfigureAwait(false);

            return bytes;
        }

        private async Task<byte[]> DownloadAsync(
            string chipset, LoaderDescriptor descriptor, string accessToken,
            CancellationToken cancellationToken)
        {
            var url = _apiBase + "/api/v1/loaders/" + Uri.EscapeDataString(chipset) + "/" +
                      Uri.EscapeDataString(descriptor.FileName);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }

            using var response = await _http.SendAsync(request, cancellationToken)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsByteArrayAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task CacheAsync(
            string chipset, string fileName, byte[] bytes, CancellationToken cancellationToken)
        {
            var directory = Path.Combine(_cacheRoot, chipset);
            Directory.CreateDirectory(directory);

            var target = Path.Combine(directory, Path.GetFileName(fileName));
            var temporary = target + ".part";

            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken)
                .ConfigureAwait(false);

            if (File.Exists(target))
            {
                File.Delete(target);
            }

            File.Move(temporary, target);
            LastCachedPath = target;
        }

        public void Dispose()
        {
            _http.Dispose();
        }
    }
}
