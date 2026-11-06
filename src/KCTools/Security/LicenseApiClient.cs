using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using KCTools.Services;

namespace KCTools.Security
{
    public sealed class ActivationRequest
    {
        [JsonPropertyName("hwid")]
        public string Hwid { get; set; }

        [JsonPropertyName("licenseKey")]
        public string LicenseKey { get; set; }

        [JsonPropertyName("appVersion")]
        public string AppVersion { get; set; }

        [JsonPropertyName("machineName")]
        public string MachineName { get; set; }
    }

    public sealed class ActivationResponse
    {
        [JsonPropertyName("token")]
        public string Token { get; set; }

        [JsonPropertyName("expiresAt")]
        public DateTimeOffset? ExpiresAt { get; set; }

        [JsonPropertyName("plan")]
        public string Plan { get; set; }

        [JsonPropertyName("credits")]
        public int Credits { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; }
    }

    public sealed class StatusResponse
    {
        [JsonPropertyName("valid")]
        public bool Valid { get; set; }

        [JsonPropertyName("plan")]
        public string Plan { get; set; }

        [JsonPropertyName("credits")]
        public int Credits { get; set; }

        [JsonPropertyName("expiresAt")]
        public DateTimeOffset? ExpiresAt { get; set; }

        [JsonPropertyName("hwid")]
        public string Hwid { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; }
    }

    public sealed class ConsumeRequest
    {
        [JsonPropertyName("amount")]
        public int Amount { get; set; }

        [JsonPropertyName("operation")]
        public string Operation { get; set; }
    }

    public sealed class ConsumeResponse
    {
        [JsonPropertyName("credits")]
        public int Credits { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; }
    }

    /// <summary>API çağrı sonucu: başarı, HTTP kodu, hata metni ve çevrimdışı bayrağı.</summary>
    public sealed class ApiResult<T>
    {
        public bool Success { get; set; }

        public bool Offline { get; set; }

        public int StatusCode { get; set; }

        public string Error { get; set; }

        public string RawBody { get; set; }

        public T Data { get; set; }

        public static ApiResult<T> Ok(T data)
        {
            return new ApiResult<T> { Success = true, StatusCode = 200, Data = data };
        }

        public static ApiResult<T> Fail(int statusCode, string error, string rawBody = null)
        {
            return new ApiResult<T> { StatusCode = statusCode, Error = error, RawBody = rawBody };
        }

        public static ApiResult<T> OfflineResult(string error)
        {
            return new ApiResult<T> { Offline = true, StatusCode = 0, Error = error };
        }
    }

    /// <summary>
    /// ASP.NET Core lisans API'siyle HTTPS üzerinden konuşan istemci.
    /// Sertifika doğrulaması kapatılmaz; JWT yalnızca Authorization başlığında taşınır.
    /// </summary>
    public sealed class LicenseApiClient : IDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly HttpClient _http;

        public LicenseApiClient(string baseUrl, TimeSpan? timeout = null)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                throw new ArgumentException("Sunucu adresi boş.", nameof(baseUrl));
            }

            BaseUrl = baseUrl.Trim().TrimEnd('/');

            var handler = new HttpClientHandler { AllowAutoRedirect = false };

            _http = new HttpClient(handler)
            {
                BaseAddress = new Uri(BaseUrl + "/"),
                Timeout = timeout ?? TimeSpan.FromSeconds(15)
            };

            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("KCTools/" + AppSettings.Version);
        }

        public string BaseUrl { get; }

        public Task<ApiResult<ActivationResponse>> ActivateAsync(
            ActivationRequest request,
            CancellationToken cancellationToken = default)
        {
            return SendAsync<ActivationResponse>(
                HttpMethod.Post, "api/v1/auth/activate", request, null, cancellationToken);
        }

        public Task<ApiResult<StatusResponse>> GetStatusAsync(
            string token,
            CancellationToken cancellationToken = default)
        {
            return SendAsync<StatusResponse>(
                HttpMethod.Get, "api/v1/license/status", null, token, cancellationToken);
        }

        public Task<ApiResult<ConsumeResponse>> ConsumeAsync(
            string token,
            ConsumeRequest request,
            CancellationToken cancellationToken = default)
        {
            return SendAsync<ConsumeResponse>(
                HttpMethod.Post, "api/v1/license/consume", request, token, cancellationToken);
        }

        public void Dispose()
        {
            _http.Dispose();
        }

        private async Task<ApiResult<T>> SendAsync<T>(
            HttpMethod method,
            string path,
            object body,
            string token,
            CancellationToken cancellationToken)
        {
            using (var request = new HttpRequestMessage(method, path))
            {
                if (body != null)
                {
                    request.Content = new StringContent(
                        JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
                }

                if (!string.IsNullOrWhiteSpace(token))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }

                try
                {
                    using (var response = await _http
                               .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                               .ConfigureAwait(false))
                    {
                        var text = await response.Content.ReadAsStringAsync(cancellationToken)
                            .ConfigureAwait(false);

                        if (!response.IsSuccessStatusCode)
                        {
                            return ApiResult<T>.Fail(
                                (int)response.StatusCode, ExtractError(text), text);
                        }

                        var data = JsonSerializer.Deserialize<T>(text, JsonOptions);

                        return data == null
                            ? ApiResult<T>.Fail((int)response.StatusCode, "Sunucu yanıtı boş.", text)
                            : ApiResult<T>.Ok(data);
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return ApiResult<T>.OfflineResult("Sunucu zaman aşımına uğradı.");
                }
                catch (HttpRequestException ex)
                {
                    return ApiResult<T>.OfflineResult("Sunucuya ulaşılamadı: " + ex.Message);
                }
                catch (JsonException ex)
                {
                    return ApiResult<T>.Fail(0, "Yanıt ayrıştırılamadı: " + ex.Message);
                }
                catch (UriFormatException)
                {
                    return ApiResult<T>.Fail(0, "Sunucu adresi geçersiz: " + BaseUrl);
                }
            }
        }

        private static string ExtractError(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return "Sunucu boş yanıt döndü.";
            }

            try
            {
                using (var document = JsonDocument.Parse(body))
                {
                    var root = document.RootElement;

                    foreach (var name in new[] { "error", "message", "detail" })
                    {
                        if (root.TryGetProperty(name, out var value) &&
                            value.ValueKind == JsonValueKind.String)
                        {
                            return value.GetString();
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // gövde düz metin olabilir
            }

            return body.Length > 200 ? body.Substring(0, 200) : body;
        }
    }
}
