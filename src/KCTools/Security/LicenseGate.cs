using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KCTools.Services;

namespace KCTools.Security
{
    public enum GateState
    {
        Authorized,
        Offline,
        LoginRequired,
        Blocked
    }

    public sealed class GateResult
    {
        public GateState State { get; set; }

        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// Masaüstü lisans kapısı: OIDC oturumu, entitlement, cihaz anahtarı ve
    /// 72 saatlik imzalı offline tolerans. Lisans bitişi asla aşılmaz.
    /// </summary>
    public sealed class LicenseGate : IDisposable
    {
        private readonly OidcOptions _options;
        private readonly OidcPkceClient _oidc;
        private readonly DesktopLicenseClient _api;

        public LicenseGate(OidcOptions options = null)
        {
            _options = options ?? new OidcOptions();
            _oidc = new OidcPkceClient(_options);
            _api = new DesktopLicenseClient(_options.ApiBase);
        }

        public static bool Enabled =>
            string.Equals(
                Environment.GetEnvironmentVariable("KCTOOLS_OIDC_ENABLED"), "1",
                StringComparison.Ordinal);

        public OidcSession Session { get; private set; }

        public string Email { get; private set; } = "—";

        public string LicenseEndText { get; private set; } = "—";

        public string StatusText { get; private set; } = "Giriş gerekli";

        public bool IsOffline { get; private set; }

        public async Task<GateResult> RestoreAsync(CancellationToken cancellationToken = default)
        {
            Session = SecureSessionStore.Load();

            if (Session == null)
            {
                return new GateResult { State = GateState.LoginRequired, Message = "Oturum yok." };
            }

            if (!string.IsNullOrWhiteSpace(Session.AccessToken) &&
                Session.AccessExpiresAt > DateTimeOffset.UtcNow.AddSeconds(30))
            {
                var online = await TryOnlineAsync(Session.AccessToken, null, cancellationToken)
                    .ConfigureAwait(false);

                if (online != null)
                {
                    return online;
                }
            }

            if (!string.IsNullOrWhiteSpace(Session.RefreshToken))
            {
                try
                {
                    var tokens = await _oidc.RefreshAsync(Session.RefreshToken, cancellationToken)
                        .ConfigureAwait(false);

                    var refreshed = await TryOnlineAsync(tokens.AccessToken, tokens, cancellationToken)
                        .ConfigureAwait(false);

                    if (refreshed != null)
                    {
                        return refreshed;
                    }
                }
                catch (Exception)
                {
                    // çevrimdışı olabilir
                }
            }

            if (ValidateOffline(Session))
            {
                IsOffline = true;
                Email = Session.Email ?? "—";
                LicenseEndText = Session.LicenseEnd?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "—";
                StatusText = "Çevrimdışı (imzalı yetki)";

                return new GateResult
                {
                    State = GateState.Offline,
                    Message = "Sunucuya ulaşılamadı; imzalı offline yetki kullanılıyor."
                };
            }

            return new GateResult
            {
                State = GateState.LoginRequired,
                Message = "Oturum doğrulanamadı; yeniden giriş gerekli."
            };
        }

        public async Task<GateResult> SignInAsync(CancellationToken cancellationToken = default)
        {
            var tokens = await _oidc.SignInAsync(cancellationToken).ConfigureAwait(false);

            var result = await TryOnlineAsync(tokens.AccessToken, tokens, cancellationToken)
                .ConfigureAwait(false);

            return result ?? new GateResult
            {
                State = GateState.Blocked,
                Message = "Lisans doğrulanamadı."
            };
        }

        private async Task<GateResult> TryOnlineAsync(
            string accessToken, OidcTokens tokens, CancellationToken cancellationToken)
        {
            EntitlementResponse entitlement;

            try
            {
                entitlement = await _api.GetEntitlementAsync(accessToken, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                return null;
            }
            catch (TaskCanceledException)
            {
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }

            if (entitlement == null)
            {
                return null;
            }

            if (!entitlement.Allowed)
            {
                StatusText = "Erişim kapalı: " + (entitlement.Reason ?? entitlement.Status);
                SecureSessionStore.Clear();

                return new GateResult
                {
                    State = GateState.Blocked,
                    Message = entitlement.Reason ?? "Lisans geçerli değil (" + entitlement.Status + ")."
                };
            }

            if (entitlement.Device == null && entitlement.License != null)
            {
                try
                {
                    await EnsureDeviceAsync(entitlement.License.Key, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    StatusText = "Cihaz aktivasyonu başarısız";

                    return new GateResult
                    {
                        State = GateState.Blocked,
                        Message = ex.Message
                    };
                }
            }

            string publicKey = null;

            try
            {
                publicKey = await _api.GetOfflinePublicKeyAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                publicKey = Session?.PublicKeyPem;
            }

            Session = new OidcSession
            {
                AccessToken = accessToken,
                RefreshToken = tokens?.RefreshToken ?? Session?.RefreshToken,
                AccessExpiresAt = tokens?.ExpiresAt ?? Session?.AccessExpiresAt ?? DateTimeOffset.UtcNow,
                OfflineToken = entitlement.OfflineToken,
                PublicKeyPem = publicKey,
                Email = entitlement.User?.Email,
                LicenseEnd = entitlement.License?.EndsAt
            };

            SecureSessionStore.Save(Session);

            IsOffline = false;
            Email = entitlement.User?.Email ?? "—";
            LicenseEndText = entitlement.License?.EndsAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "—";
            StatusText = "Aktif · " + entitlement.License?.Plan;

            return new GateResult { State = GateState.Authorized, Message = "Lisans aktif." };
        }

        private async Task EnsureDeviceAsync(string licenseKey, CancellationToken cancellationToken)
        {
            var material = DeviceKeyStore.GetOrCreate();
            var hwid = HwidProvider.GetHwid();

            var nonce = await _api.GetChallengeAsync(
                    licenseKey, material.DeviceId, material.PublicKeyPem, cancellationToken)
                .ConfigureAwait(false);

            var signature = material.Sign(nonce);

            await _api.ActivateDeviceAsync(
                    licenseKey, material.DeviceId, hwid, Environment.MachineName,
                    material.PublicKeyPem, nonce, signature, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>İmzalı offline token'ı yerel olarak doğrular (RS256).</summary>
        public bool ValidateOffline(OidcSession session)
        {
            if (session == null ||
                string.IsNullOrWhiteSpace(session.OfflineToken) ||
                string.IsNullOrWhiteSpace(session.PublicKeyPem))
            {
                return false;
            }

            try
            {
                var parts = session.OfflineToken.Split('.');

                if (parts.Length != 3)
                {
                    return false;
                }

                using var rsa = RSA.Create();
                rsa.ImportFromPem(session.PublicKeyPem);

                var signature = Convert.FromBase64String(Pad(parts[2]));

                if (!rsa.VerifyData(
                        Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]),
                        signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                {
                    return false;
                }

                var payload = JsonDocument.Parse(
                    Encoding.UTF8.GetString(Convert.FromBase64String(Pad(parts[1]))));

                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                var exp = payload.RootElement.GetProperty("exp").GetInt64();
                var offlineUntil = payload.RootElement.GetProperty("offline_until").GetInt64();
                var licenseEnd = payload.RootElement.GetProperty("license_end").GetInt64();

                if (now >= exp || now >= offlineUntil || now >= licenseEnd)
                {
                    return false;
                }

                var deviceId = payload.RootElement.TryGetProperty("device_id", out var device)
                    ? device.GetString()
                    : null;

                var material = DeviceKeyStore.GetOrCreate();

                return string.IsNullOrWhiteSpace(deviceId) ||
                       string.Equals(deviceId, material.DeviceId.ToString(),
                           StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string Pad(string value)
        {
            var text = value.Replace('-', '+').Replace('_', '/');
            return text + new string('=', (4 - text.Length % 4) % 4);
        }

        public void Dispose()
        {
            _oidc.Dispose();
            _api.Dispose();
        }
    }
}
