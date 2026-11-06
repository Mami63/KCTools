using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KCTools.Security
{
    public sealed class OidcOptions
    {
        public string Authority { get; set; } =
            Environment.GetEnvironmentVariable("KCTOOLS_OIDC_AUTHORITY") ?? "https://localhost:5443";

        public string ApiBase { get; set; } =
            Environment.GetEnvironmentVariable("KCTOOLS_LICENSE_API") ?? "https://localhost:5443";

        public string ClientId { get; set; } =
            Environment.GetEnvironmentVariable("KCTOOLS_OIDC_CLIENT_ID") ?? "kctools-desktop";

        public int RedirectPort { get; set; } =
            int.TryParse(Environment.GetEnvironmentVariable("KCTOOLS_OIDC_PORT"), out var port)
                ? port
                : 45999;

        public string Scope => "openid profile offline_access kctools.license";
    }

    public sealed class OidcTokens
    {
        public string AccessToken { get; set; }

        public string RefreshToken { get; set; }

        public DateTimeOffset ExpiresAt { get; set; }
    }

    /// <summary>
    /// Masaüstü OAuth 2.0 Authorization Code + PKCE (S256) istemcisi.
    /// Sistem tarayıcısı kullanılır; loopback TcpListener ile kod yakalanır.
    /// </summary>
    public sealed class OidcPkceClient : IDisposable
    {
        private readonly OidcOptions _options;
        private readonly HttpClient _http;

        public OidcPkceClient(OidcOptions options)
        {
            _options = options;
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        }

        public async Task<OidcTokens> SignInAsync(CancellationToken cancellationToken = default)
        {
            var verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
            var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            var state = Base64Url(RandomNumberGenerator.GetBytes(16));
            var redirect = $"http://127.0.0.1:{_options.RedirectPort}/callback";

            var authorizeUrl =
                _options.Authority.TrimEnd('/') + "/connect/authorize" +
                "?client_id=" + Uri.EscapeDataString(_options.ClientId) +
                "&response_type=code" +
                "&scope=" + Uri.EscapeDataString(_options.Scope) +
                "&redirect_uri=" + Uri.EscapeDataString(redirect) +
                "&state=" + state +
                "&code_challenge=" + challenge +
                "&code_challenge_method=S256";

            var listener = new TcpListener(IPAddress.Loopback, _options.RedirectPort);
            listener.Start();

            try
            {
                Process.Start(new ProcessStartInfo(authorizeUrl) { UseShellExecute = true });

                var code = await WaitForCodeAsync(listener, state, cancellationToken)
                    .ConfigureAwait(false);

                return await ExchangeAsync(code, verifier, redirect, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                listener.Stop();
            }
        }

        public async Task<OidcTokens> RefreshAsync(
            string refreshToken, CancellationToken cancellationToken = default)
        {
            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = _options.ClientId
            };

            using var response = await _http.PostAsync(
                    _options.Authority.TrimEnd('/') + "/connect/token",
                    new FormUrlEncodedContent(form), cancellationToken)
                .ConfigureAwait(false);

            return await ParseTokenResponseAsync(response, cancellationToken).ConfigureAwait(false);
        }

        private async Task<OidcTokens> ExchangeAsync(
            string code, string verifier, string redirect, CancellationToken cancellationToken)
        {
            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirect,
                ["client_id"] = _options.ClientId,
                ["code_verifier"] = verifier
            };

            using var response = await _http.PostAsync(
                    _options.Authority.TrimEnd('/') + "/connect/token",
                    new FormUrlEncodedContent(form), cancellationToken)
                .ConfigureAwait(false);

            return await ParseTokenResponseAsync(response, cancellationToken).ConfigureAwait(false);
        }

        private static async Task<OidcTokens> ParseTokenResponseAsync(
            HttpResponseMessage response, CancellationToken cancellationToken)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException("Token alınamadı: " + ExtractError(body));
            }

            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            var expiresIn = root.TryGetProperty("expires_in", out var expires)
                ? expires.GetInt32()
                : 600;

            return new OidcTokens
            {
                AccessToken = root.GetProperty("access_token").GetString(),
                RefreshToken = root.TryGetProperty("refresh_token", out var refresh)
                    ? refresh.GetString()
                    : null,
                ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, expiresIn - 30))
            };
        }

        private static async Task<string> WaitForCodeAsync(
            TcpListener listener, string expectedState, CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));

            TcpClient client;

            try
            {
                client = await listener.AcceptTcpClientAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("Tarayıcı girişi zaman aşımına uğradı.");
            }

            using (client)
            {
                var stream = client.GetStream();
                var buffer = new byte[16384];
                var read = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);
                var request = Encoding.ASCII.GetString(buffer, 0, read);

                var path = request.Split(' ')[0].Length > 0 && request.Contains(' ')
                    ? request.Split(' ')[1]
                    : "/";

                var uri = new Uri("http://127.0.0.1" + path);
                var query = ParseQuery(uri.Query);

                var html = "<html><body style='font-family:Segoe UI;background:#0b0f14;color:#e6edf3;" +
                           "display:grid;place-items:center;height:100vh'>" +
                           "<div><h2>KC Tools</h2><p>Giriş tamamlandı. Bu pencereyi kapatabilirsiniz.</p>" +
                           "</div></body></html>";

                var responseBytes = Encoding.UTF8.GetBytes(html);
                var header = "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\n" +
                             "Content-Length: " + responseBytes.Length + "\r\nConnection: close\r\n\r\n";

                await stream.WriteAsync(Encoding.ASCII.GetBytes(header), timeout.Token)
                    .ConfigureAwait(false);
                await stream.WriteAsync(responseBytes, timeout.Token).ConfigureAwait(false);

                if (query.TryGetValue("error", out var error))
                {
                    throw new InvalidOperationException("Giriş reddedildi: " + error);
                }

                if (!query.TryGetValue("code", out var code))
                {
                    throw new InvalidOperationException("Yetkilendirme kodu alınamadı.");
                }

                if (!query.TryGetValue("state", out var state) ||
                    !string.Equals(state, expectedState, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("State doğrulanamadı (CSRF koruması).");
                }

                return code;
            }
        }

        private static Dictionary<string, string> ParseQuery(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('=', 2);
                var key = Uri.UnescapeDataString(parts[0]);
                var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
                result[key] = value;
            }

            return result;
        }

        private static string ExtractError(string body)
        {
            try
            {
                using var document = JsonDocument.Parse(body);

                foreach (var name in new[] { "error_description", "error", "message" })
                {
                    if (document.RootElement.TryGetProperty(name, out var value))
                    {
                        return value.GetString();
                    }
                }
            }
            catch (JsonException)
            {
                // düz metin
            }

            return body.Length > 200 ? body.Substring(0, 200) : body;
        }

        private static string Base64Url(byte[] data)
        {
            return Convert.ToBase64String(data)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        public void Dispose()
        {
            _http.Dispose();
        }
    }
}
