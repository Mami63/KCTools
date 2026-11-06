using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

var baseUrl = args.Length > 0 ? args[0] : "http://127.0.0.1:5090";
var failures = new List<string>();

void Check(bool condition, string name)
{
    Console.WriteLine((condition ? "PASS  " : "FAIL  ") + name);

    if (!condition)
    {
        failures.Add(name);
    }
}

var handler = new HttpClientHandler
{
    AllowAutoRedirect = false,
    CookieContainer = new CookieContainer()
};

using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };

async Task<(HttpStatusCode Code, string Body)> PostJson(
    string path, object payload, string bearer = null, string adminKey = null)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + path);
    request.Content = new StringContent(
        JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    if (!string.IsNullOrWhiteSpace(bearer))
    {
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
    }

    if (!string.IsNullOrWhiteSpace(adminKey))
    {
        request.Headers.Add("X-Admin-Key", adminKey);
    }

    using var response = await client.SendAsync(request);
    return (response.StatusCode, await response.Content.ReadAsStringAsync());
}

async Task<(HttpStatusCode Code, string Body)> PostForm(
    string path, Dictionary<string, string> values)
{
    using var response = await client.PostAsync(
        baseUrl + path, new FormUrlEncodedContent(values));

    return (response.StatusCode, await response.Content.ReadAsStringAsync());
}

// Sağlık
using (var health = await client.GetAsync(baseUrl + "/health"))
{
    Check(health.IsSuccessStatusCode, "Sunucu health 200");
}

// Kullanıcı + lisans
var email = "job-" + Guid.NewGuid().ToString("N").Substring(0, 8) + "@test.local";
var password = "Job!Pass12345";

var register = await PostJson("/api/v1/auth/register", new { username = email, password });
Check(register.Code == HttpStatusCode.Created, "Kullanıcı kaydı 201");

var userId = Regex.Match(register.Body, "\"id\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;
Check(Guid.TryParse(userId, out _), "Kullanıcı kimliği alındı");

var license = await PostJson(
    "/api/v1/admin/licenses",
    new { plan = "PRO", credits = 50, days = 365, userId },
    adminKey: "test-admin-key");

Check(license.Code == HttpStatusCode.Created, "Lisans oluşturuldu 201");
var licenseKey = Regex.Match(license.Body, "\"key\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;
Check(licenseKey.StartsWith("KC-"), "Lisans anahtarı alındı");

// Cihaz aktivasyonu (ECDSA)
using var deviceKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
var publicPem = deviceKey.ExportSubjectPublicKeyInfoPem();
var deviceId = Guid.NewGuid();
var hwid = "HWID-JOB-TEST-0001";

var challenge = await PostJson(
    "/api/v1/devices/challenge",
    new { licenseKey, deviceId, publicKey = publicPem });

Check(challenge.Code == HttpStatusCode.OK, "Cihaz challenge 200");
var nonce = Regex.Match(challenge.Body, "\"nonce\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;

var signature = Convert.ToBase64String(
    deviceKey.SignData(Encoding.UTF8.GetBytes(nonce), HashAlgorithmName.SHA256));

var activate = await PostJson(
    "/api/v1/devices/activate",
    new
    {
        licenseKey,
        deviceId,
        hwid,
        machineName = "JOB-TEST-PC",
        publicKey = publicPem,
        nonce,
        signature
    });

Check(activate.Code == HttpStatusCode.OK, "Cihaz aktivasyonu 200");

// OIDC PKCE
var loginPage = await client.GetStringAsync(baseUrl + "/Account/Login");
var antiForgery = Regex.Match(
    loginPage, "__RequestVerificationToken[^>]*value=\"([^\"]+)\"").Groups[1].Value;

var login = await PostForm("/Account/Login", new Dictionary<string, string>
{
    ["Email"] = email,
    ["Password"] = password,
    ["__RequestVerificationToken"] = antiForgery
});

Check((int)login.Code is 200 or 302, "Panel oturumu açıldı");

var verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
var challengeCode = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
var redirect = "http://127.0.0.1:45999/callback";
var scope = "openid profile offline_access kctools.license";

var authorizeUrl = baseUrl + "/connect/authorize?client_id=kctools-desktop" +
                   "&response_type=code&scope=" + Uri.EscapeDataString(scope) +
                   "&redirect_uri=" + Uri.EscapeDataString(redirect) +
                   "&state=jobstate&code_challenge=" + challengeCode +
                   "&code_challenge_method=S256";

using (var authorize = await client.GetAsync(authorizeUrl))
{
    Check(authorize.StatusCode == HttpStatusCode.Redirect, "OIDC authorize 302");

    var location = authorize.Headers.Location?.OriginalString ?? string.Empty;
    var code = Regex.Match(location, "code=([^&]+)").Groups[1].Value;

    Check(code.Length > 0, "OIDC yetkilendirme kodu alındı");

    var token = await PostForm("/connect/token", new Dictionary<string, string>
    {
        ["grant_type"] = "authorization_code",
        ["code"] = code,
        ["redirect_uri"] = redirect,
        ["client_id"] = "kctools-desktop",
        ["code_verifier"] = verifier
    });

    Check(token.Code == HttpStatusCode.OK, "OIDC token 200");

    var accessToken = Regex.Match(
        token.Body, "\"access_token\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;

    Check(accessToken.Length > 0, "Access token alındı");

    // Yetkisiz çağrı
    var unauthorized = await PostJson(
        "/api/v1/auth/authorize-job",
        new { hwid, serial = "SERIAL-1", chipset = "SM8650", operation = "flash" });

    Check(unauthorized.Code == HttpStatusCode.Unauthorized, "Job: yetkisiz 401");

    // Yetkili çağrı
    var job = await PostJson(
        "/api/v1/auth/authorize-job",
        new { hwid, serial = "SERIAL-1", chipset = "SM8650", operation = "flash" },
        bearer: accessToken);

    Check(job.Code == HttpStatusCode.OK, "Job: yetkili 200");

    var jobToken = Regex.Match(job.Body, "\"token\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;
    Check(jobToken.Split('.').Length == 3, "Job token JWT biçiminde");

    var payload = jobToken.Split('.')[1].Replace('-', '+').Replace('_', '/');
    while (payload.Length % 4 != 0) payload += "=";

    var claims = JsonDocument.Parse(
        Encoding.UTF8.GetString(Convert.FromBase64String(payload))).RootElement;

    Check(claims.GetProperty("typ").GetString() == "job", "Job token tipi job");
    Check(claims.GetProperty("chipset").GetString() == "SM8650", "Job token chipset");
    Check(claims.GetProperty("hwid").GetString() == hwid, "Job token HWID");
}

Console.WriteLine();
Console.WriteLine(failures.Count == 0
    ? "SONUC: TUM TESTLER GECTI"
    : "SONUC: " + failures.Count + " basarisiz -> " + string.Join(", ", failures));

return failures.Count == 0 ? 0 : 1;

static string Base64Url(byte[] value)
{
    return Convert.ToBase64String(value)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
}
