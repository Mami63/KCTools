using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KCTools.Security
{
    public sealed class EntitlementResponse
    {
        public bool Allowed { get; set; }

        public string Status { get; set; }

        public string Reason { get; set; }

        public string OfflineToken { get; set; }

        public int OfflineHours { get; set; }

        public LicenseInfo License { get; set; }

        public DeviceInfo Device { get; set; }

        public UserInfo User { get; set; }
    }

    public sealed class LicenseInfo
    {
        public string Key { get; set; }

        public string Plan { get; set; }

        public int Credits { get; set; }

        public DateTimeOffset StartsAt { get; set; }

        public DateTimeOffset EndsAt { get; set; }

        public int MaxDevices { get; set; }

        public bool IsRevoked { get; set; }

        public bool IsFrozen { get; set; }
    }

    public sealed class DeviceInfo
    {
        public Guid DeviceId { get; set; }

        public string Hwid { get; set; }

        public string MachineName { get; set; }

        public DateTimeOffset? ActivatedAt { get; set; }

        public string Thumbprint { get; set; }
    }

    public sealed class UserInfo
    {
        public Guid Id { get; set; }

        public string Email { get; set; }

        public string Name { get; set; }
    }

    /// <summary>Lisans API'si: entitlement, cihaz challenge/activate, offline açık anahtar.</summary>
    public sealed class DesktopLicenseClient : IDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly HttpClient _http;
        private readonly string _apiBase;

        public DesktopLicenseClient(string apiBase)
        {
            _apiBase = apiBase.TrimEnd('/');
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        }

        public async Task<EntitlementResponse> GetEntitlementAsync(
            string accessToken, CancellationToken cancellationToken = default)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get, _apiBase + "/api/v1/me/license");

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _http.SendAsync(request, cancellationToken)
                .ConfigureAwait(false);

            var body = await response.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException("Lisans durumu alınamadı: " + Truncate(body));
            }

            return JsonSerializer.Deserialize<EntitlementResponse>(body, JsonOptions);
        }

        public async Task<string> GetChallengeAsync(
            string licenseKey, Guid deviceId, string publicKeyPem,
            CancellationToken cancellationToken = default)
        {
            var json = JsonSerializer.Serialize(new
            {
                licenseKey,
                deviceId,
                publicKey = publicKeyPem
            });

            using var response = await _http.PostAsync(
                    _apiBase + "/api/v1/devices/challenge",
                    new StringContent(json, Encoding.UTF8, "application/json"),
                    cancellationToken)
                .ConfigureAwait(false);

            var body = await response.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException("Challenge alınamadı: " + Truncate(body));
            }

            using var document = JsonDocument.Parse(body);
            return document.RootElement.GetProperty("nonce").GetString();
        }

        public async Task ActivateDeviceAsync(
            string licenseKey, Guid deviceId, string hwid, string machineName,
            string publicKeyPem, string nonce, string signature,
            CancellationToken cancellationToken = default)
        {
            var json = JsonSerializer.Serialize(new
            {
                licenseKey,
                deviceId,
                hwid,
                machineName,
                publicKey = publicKeyPem,
                nonce,
                signature
            });

            using var response = await _http.PostAsync(
                    _apiBase + "/api/v1/devices/activate",
                    new StringContent(json, Encoding.UTF8, "application/json"),
                    cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);

                throw new InvalidOperationException("Cihaz aktive edilemedi: " + Truncate(body));
            }
        }

        public async Task<string> GetOfflinePublicKeyAsync(
            CancellationToken cancellationToken = default)
        {
            return await _http.GetStringAsync(
                    _apiBase + "/api/v1/license/offline-public-key", cancellationToken)
                .ConfigureAwait(false);
        }

        private static string Truncate(string value)
        {
            return value != null && value.Length > 240 ? value.Substring(0, 240) : value;
        }

        public void Dispose()
        {
            _http.Dispose();
        }
    }
}
