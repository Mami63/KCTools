using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KCTools.Services
{
    public sealed class MagiskRelease
    {
        public string Tag { get; set; } = "—";

        public string ApkName { get; set; } = string.Empty;

        public string ApkUrl { get; set; } = string.Empty;

        public long Size { get; set; }
    }

    /// <summary>
    /// Magisk APK'sını resmi GitHub sürümünden indirir. Kullanıcı ayrıca APK
    /// indirmez; uygulama sürümü bulur, önbelleğe alır ve telefona kurar.
    /// </summary>
    public static class MagiskInstaller
    {
        private const string ApiUrl =
            "https://api.github.com/repos/topjohnwu/Magisk/releases/latest";

        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("KCTools/1.0");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        }

        public static async Task<MagiskRelease> GetLatestAsync(
            CancellationToken cancellationToken = default)
        {
            try
            {
                var json = await Http.GetStringAsync(ApiUrl, cancellationToken)
                    .ConfigureAwait(false);

                return Parse(json);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>GitHub sürüm JSON'undan APK varlığını seçer (test edilebilir).</summary>
        public static MagiskRelease Parse(string json)
        {
            try
            {
                using (var document = JsonDocument.Parse(json))
                {
                    var root = document.RootElement;
                    var tag = root.TryGetProperty("tag_name", out var tagElement)
                        ? tagElement.GetString()
                        : "—";

                    if (!root.TryGetProperty("assets", out var assets))
                    {
                        return null;
                    }

                    var apkAssets = assets.EnumerateArray()
                        .Select(asset => new
                        {
                            Name = asset.TryGetProperty("name", out var name) ? name.GetString() : null,
                            Url = asset.TryGetProperty("browser_download_url", out var url)
                                ? url.GetString()
                                : null,
                            Size = asset.TryGetProperty("size", out var size) ? size.GetInt64() : 0
                        })
                        .Where(asset =>
                            asset.Name != null &&
                            asset.Url != null &&
                            asset.Name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    var chosen = apkAssets.FirstOrDefault(
                                     asset => asset.Name.StartsWith(
                                         "Magisk-", StringComparison.OrdinalIgnoreCase))
                                 ?? apkAssets.FirstOrDefault();

                    if (chosen == null)
                    {
                        return null;
                    }

                    return new MagiskRelease
                    {
                        Tag = tag ?? "—",
                        ApkName = chosen.Name,
                        ApkUrl = chosen.Url,
                        Size = chosen.Size
                    };
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static async Task<string> DownloadAsync(
            MagiskRelease release,
            CancellationToken cancellationToken = default)
        {
            if (release == null || string.IsNullOrWhiteSpace(release.ApkUrl))
            {
                return null;
            }

            try
            {
                var folder = ResolveCacheFolder();
                Directory.CreateDirectory(folder);

                var fileName = (release.ApkName ?? "magisk.apk")
                    .Replace('/', '_')
                    .Replace('\\', '_');

                var target = Path.Combine(folder, fileName);

                if (File.Exists(target) && new FileInfo(target).Length == release.Size)
                {
                    return target;
                }

                var temp = target + ".part";

                using (var response = await Http.GetAsync(
                           release.ApkUrl,
                           HttpCompletionOption.ResponseHeadersRead,
                           cancellationToken).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();

                    using (var input = await response.Content.ReadAsStreamAsync(cancellationToken)
                               .ConfigureAwait(false))
                    using (var output = new FileStream(
                               temp, FileMode.Create, FileAccess.Write, FileShare.None,
                               81920, true))
                    {
                        await input.CopyToAsync(output, 81920, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }

                if (File.Exists(target))
                {
                    File.Delete(target);
                }

                File.Move(temp, target);

                LogHub.Instance.Ok(
                    "Magisk " + release.Tag + " indirildi (" +
                    (new FileInfo(target).Length / 1024 / 1024) + " MB)", "ROOT");

                return target;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                LogHub.Instance.Warn("Magisk indirilemedi: " + ex.Message, "ROOT");
                return null;
            }
        }

        public static string ResolveCacheFolder()
        {
            var primary = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "KCTools", "cache");

            try
            {
                Directory.CreateDirectory(primary);

                var probe = Path.Combine(primary, ".write-test");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);

                return primary;
            }
            catch (Exception)
            {
                return Path.Combine(AppContext.BaseDirectory, "cache");
            }
        }
    }
}
