using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using KCTools.LicenseServer.Data;
using Microsoft.AspNetCore.Identity;
using KCTools.LicenseServer.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var failures = new List<string>();

static string B64(string value)
{
    var text = value.Replace('-', '+').Replace('_', '/');
    return text + new string('=', (4 - text.Length % 4) % 4);
}


void Check(bool condition, string name)
{
    Console.WriteLine((condition ? "PASS  " : "FAIL  ") + name);

    if (!condition)
    {
        failures.Add(name);
    }
}

using var connection = new SqliteConnection("Filename=:memory:");
connection.Open();

var options = new DbContextOptionsBuilder<LicenseDbContext>()
    .UseSqlite(connection)
    .Options;

using var db = new LicenseDbContext(options);
db.Database.EnsureCreated();

var repository = new LicenseRepository(db);

// ---------------------------------------------------------------- şema
var tables = new List<string>();

using (var command = connection.CreateCommand())
{
    command.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
    using var reader = command.ExecuteReader();

    while (reader.Read())
    {
        tables.Add(reader.GetString(0));
    }
}

Check(new[] { "AspNetUsers", "AspNetRoles", "Licenses", "Devices", "RefreshTokens", "AuditLogs" }
    .All(tables.Contains), "Şema: Identity + lisans tabloları oluştu");

Check(db.Model.FindEntityType(typeof(AppUser)) != null, "Şema: AppUser modeli");
Check(db.Model.FindEntityType(typeof(Device)) != null, "Şema: Device modeli");

// ---------------------------------------------------------------- kullanıcı (Identity)
var user = new AppUser
{
    Id = Guid.NewGuid(),
    UserName = "demo@example.com",
    Email = "demo@example.com",
    DisplayName = "Demo"
};

db.Users.Add(user);
await db.SaveChangesAsync();

var hasher = new PasswordHasher<AppUser>();
var passwordHash = hasher.HashPassword(user, "Password123!");

Check(passwordHash != "Password123!", "Parola: düz metin saklanmıyor");
Check(hasher.VerifyHashedPassword(user, passwordHash, "Password123!")
        != PasswordVerificationResult.Failed, "Parola: Identity hash doğrulaması");
Check(hasher.VerifyHashedPassword(user, passwordHash, "yanlis")
        == PasswordVerificationResult.Failed, "Parola: yanlış parola reddedildi");
Check(await db.Users.AnyAsync(item => item.Email == "demo@example.com"),
    "Kullanıcı: veritabanında kayıtlı");
Check(AppRoles.Admin == "Admin" && AppRoles.Customer == "Customer",
    "Roller: Admin + Customer tanımlı");
// ---------------------------------------------------------------- lisans + cihaz
var (license, licenseError) = await repository.CreateLicenseAsync(
    null, "PRO", 10, 30, user!.Id, "test", null);

Check(license != null, "Lisans: oluşturuldu");
Check(license != null && license.EndsAt > license.StartsAt, "Lisans: başlangıç/bitiş tarihi");
Check(license != null && license.MaxDevices == 1, "Lisans: varsayılan tek bilgisayar");
Check(license != null && license.Key.StartsWith("KC-"), "Lisans: anahtar biçimi");

var first = await repository.ActivateAsync(license!.Key, "HWID-AAAA-1111", "PC-A", null);
Check(first.Success && first.Device != null, "Aktivasyon: ilk cihaz bağlandı");

var again = await repository.ActivateAsync(license.Key, "HWID-AAAA-1111", "PC-A", null);
Check(again.Success, "Aktivasyon: aynı cihaz tekrar bağlanabilir");

var second = await repository.ActivateAsync(license.Key, "HWID-BBBB-2222", "PC-B", null);
Check(!second.Success && second.StatusCode == 409,
    "Aktivasyon: ikinci cihaz reddedildi (tek bilgisayar)");

Check(await db.Devices.CountAsync(item => item.LicenseId == license.Id) == 1,
    "Cihaz: bağlantı veritabanında");

// ---------------------------------------------------------------- kredi
var consume = await repository.ConsumeAsync(
    license.Key, "HWID-AAAA-1111", 3, "test", null);

Check(consume.Success && consume.Credits == 7, "Kredi: 3 düşüldü (7 kaldı)");

var insufficient = await repository.ConsumeAsync(
    license.Key, "HWID-AAAA-1111", 100, "test", null);

Check(!insufficient.Success && insufficient.StatusCode == 409,
    "Kredi: yetersizse reddedildi");

var wrongDevice = await repository.ConsumeAsync(
    license.Key, "HWID-OTHER-9999", 1, "test", null);

Check(!wrongDevice.Success, "Kredi: bağlı olmayan cihaz reddedildi");

// ---------------------------------------------------------------- iptal
var revoke = await repository.RevokeLicenseAsync(license.Key, "test", "admin", null);
Check(revoke.Ok, "İptal: yapıldı");

var afterRevoke = await repository.ActivateAsync(license.Key, "HWID-AAAA-1111", "PC-A", null);
Check(!afterRevoke.Success && afterRevoke.StatusCode == 409,
    "İptal: lisans tekrar kullanılamıyor");

Check(await db.Devices.AllAsync(item => !item.IsActive),
    "İptal: cihaz bağlantıları pasif");

// ---------------------------------------------------------------- süre
var expired = new License
{
    Key = "KC-EXPD-EXPD-EXPD",
    Plan = "PRO",
    Credits = 5,
    StartsAt = DateTimeOffset.UtcNow.AddDays(-10),
    EndsAt = DateTimeOffset.UtcNow.AddDays(-1)
};

db.Licenses.Add(expired);
await db.SaveChangesAsync();

var expiredActivation = await repository.ActivateAsync(
    expired.Key, "HWID-CCCC-3333", "PC-C", null);

Check(!expiredActivation.Success && expiredActivation.StatusCode == 409,
    "Süre: dolmuş lisans reddedildi");

// ---------------------------------------------------------------- refresh token
var (raw, refreshExpires, refreshError) = await repository.CreateRefreshTokenAsync(user, null);
Check(raw != null, "Refresh: token üretildi");

var rotated = await repository.RotateRefreshTokenAsync(raw!, null);
Check(rotated.RawToken != null && rotated.User != null, "Refresh: rotate başarılı");

var reused = await repository.RotateRefreshTokenAsync(raw!, null);
Check(reused.User == null, "Refresh: eski token tekrar kullanılamaz");

var stored = await db.RefreshTokens.FirstAsync(item => item.UserId == user.Id);
Check(stored.TokenHash != raw, "Refresh: ham token saklanmıyor");

// ---------------------------------------------------------------- audit
var audit = await repository.GetAuditAsync(100);
Check(audit.Any(item => item.Action == "admin.license.create"), "Audit: lisans oluşturma");
Check(audit.Any(item => item.Action == "admin.license.revoke"), "Audit: lisans iptali");
Check(audit.Any(item => item.Action == "license.activate"), "Audit: aktivasyon");


// ---------------------------------------------------------------- cihaz anahtarı (ECDSA)
var (keyLicense, keyLicenseError) = await repository.CreateLicenseAsync(
    null, "PRO", 5, 30, user.Id, "test", null);

using var deviceKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
var publicPem = deviceKey.ExportSubjectPublicKeyInfoPem();
var deviceId = Guid.NewGuid();

var challenge = await repository.CreateChallengeAsync(
    keyLicense!.Key, deviceId, publicPem);

Check(challenge.Nonce != null, "Cihaz: challenge üretildi");

var signature = Convert.ToBase64String(deviceKey.SignData(
    Encoding.UTF8.GetBytes(challenge.Nonce!), HashAlgorithmName.SHA256));

var activated = await repository.ActivateDeviceAsync(
    keyLicense.Key, deviceId, "HWID-DEVICE-AAA", "PC-A",
    publicPem, challenge.Nonce!, signature, null);

Check(activated.Success && activated.Device?.KeyThumbprint != null,
    "Cihaz: ECDSA kanıtıyla aktive edildi");

var replay = await repository.ActivateDeviceAsync(
    keyLicense.Key, deviceId, "HWID-DEVICE-AAA", "PC-A",
    publicPem, challenge.Nonce!, signature, null);

Check(!replay.Success && replay.StatusCode == 401,
    "Cihaz: nonce tekrar kullanılamaz");

using var secondKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
var secondPem = secondKey.ExportSubjectPublicKeyInfoPem();
var secondDeviceId = Guid.NewGuid();

var secondChallenge = await repository.CreateChallengeAsync(
    keyLicense.Key, secondDeviceId, secondPem);

var secondSignature = Convert.ToBase64String(secondKey.SignData(
    Encoding.UTF8.GetBytes(secondChallenge.Nonce!), HashAlgorithmName.SHA256));

var secondActivation = await repository.ActivateDeviceAsync(
    keyLicense.Key, secondDeviceId, "HWID-DEVICE-BBB", "PC-B",
    secondPem, secondChallenge.Nonce!, secondSignature, null);

Check(!secondActivation.Success && secondActivation.StatusCode == 409,
    "Cihaz: ikinci bilgisayar reddedildi");

using var thirdKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
var thirdDeviceId = Guid.NewGuid();
var thirdChallenge = await repository.CreateChallengeAsync(
    keyLicense.Key, thirdDeviceId, publicPem);

var wrongSignature = Convert.ToBase64String(thirdKey.SignData(
    Encoding.UTF8.GetBytes(thirdChallenge.Nonce!), HashAlgorithmName.SHA256));

var invalid = await repository.ActivateDeviceAsync(
    keyLicense.Key, thirdDeviceId, "HWID-DEVICE-CCC", "PC-C",
    publicPem, thirdChallenge.Nonce!, wrongSignature, null);

Check(!invalid.Success && invalid.StatusCode == 401,
    "Cihaz: geçersiz imza reddedildi");

var reset = await repository.ResetDeviceAsync(deviceId, "admin", null);
Check(reset.Ok, "Cihaz: yönetici sıfırladı");

var newDeviceId = Guid.NewGuid();
var newChallenge = await repository.CreateChallengeAsync(
    keyLicense.Key, newDeviceId, publicPem);

var newSignature = Convert.ToBase64String(deviceKey.SignData(
    Encoding.UTF8.GetBytes(newChallenge.Nonce!), HashAlgorithmName.SHA256));

var reactivated = await repository.ActivateDeviceAsync(
    keyLicense.Key, newDeviceId, "HWID-DEVICE-NEW", "PC-NEW",
    publicPem, newChallenge.Nonce!, newSignature, null);

Check(reactivated.Success, "Cihaz: sıfırlama sonrası yeni aktivasyon");

var history = await repository.GetActivationHistoryAsync(keyLicense.Key);
Check(history.Any(item => item.Action == "admin.reset"),
    "Cihaz: aktivasyon geçmişi kaydı");

// ---------------------------------------------------------------- süre / dondurma
var (termLicense, termError) = await repository.CreateLicenseAsync(
    null, "PRO", 10, 30, user.Id, "test", null);

var beforeEnd = termLicense!.EndsAt;
var (extended, extendError) = await repository.ExtendLicenseAsync(
    termLicense.Key, 30, "admin", null);

Check(extended != null && extended.EndsAt == beforeEnd.AddDays(30),
    "Lisans: süre uzatma");
Check(extended != null && extended.ExtendedCount == 1,
    "Lisans: uzatma sayacı");

var (frozen, freezeError) = await repository.FreezeLicenseAsync(
    termLicense.Key, "test", "admin", null);

Check(frozen != null && frozen.IsFrozen, "Lisans: dondurma");

var frozenActivation = await repository.ActivateAsync(
    termLicense.Key, "HWID-FROZEN-1", "PC-F", null);

Check(!frozenActivation.Success && frozenActivation.StatusCode == 409,
    "Lisans: donmuş lisans aktive edilemez");

var (unfrozen, unfreezeError) = await repository.UnfreezeLicenseAsync(
    termLicense.Key, "admin", null);

Check(unfrozen != null && !unfrozen.IsFrozen, "Lisans: aktifleştirme");

// ---------------------------------------------------------------- offline entitlement (RS256)
var offlineKeyPath = Path.Combine(AppContext.BaseDirectory, "offline-test-key.pem");
if (File.Exists(offlineKeyPath)) File.Delete(offlineKeyPath);

var offline = new OfflineEntitlementService(offlineKeyPath);
var activeDevice = await db.Devices.FirstAsync(item => item.IsActive);
var offlineToken = offline.Issue(license, activeDevice, user.Id, 72);

var parts = offlineToken.Split('.');
Check(parts.Length == 3, "Offline: JWT üç parçalı");

var header = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
    Encoding.UTF8.GetString(Convert.FromBase64String(B64(parts[0]))));
Check(header != null && header["alg"].GetString() == "RS256", "Offline: RS256 algoritması");

var payloadJson = Encoding.UTF8.GetString(Convert.FromBase64String(B64(parts[1])));
var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(payloadJson);

var offlineUntil = payload!["offline_until"].GetInt64();
var licenseEnd = payload["license_end"].GetInt64();
var nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

Check(offlineUntil <= licenseEnd, "Offline: tolerans lisans bitişini aşmıyor");
Check(offlineUntil <= nowSeconds + 72 * 3600 + 120, "Offline: en fazla 72 saat");

using var verifyRsa = RSA.Create();
verifyRsa.ImportFromPem(offline.PublicKeyPem);
var offlineSignature = Convert.FromBase64String(B64(parts[2]));

Check(verifyRsa.VerifyData(
        Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]),
        offlineSignature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
    "Offline: imza doğrulandı");

var expiredLicense = new License
{
    Key = "KC-OFFL-EXPD-EXPD",
    Plan = "PRO",
    Credits = 1,
    StartsAt = DateTimeOffset.UtcNow.AddDays(-30),
    EndsAt = DateTimeOffset.UtcNow.AddHours(-1)
};

var expiredToken = offline.Issue(expiredLicense, null, user.Id, 72);
var expiredPayload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
    Encoding.UTF8.GetString(Convert.FromBase64String(
        B64(expiredToken.Split('.')[1]))));

Check(expiredPayload!["exp"].GetInt64() <=
      DateTimeOffset.UtcNow.ToUnixTimeSeconds(), "Offline: süresi dolmuş lisans toleranssız");
Console.WriteLine();
Console.WriteLine(failures.Count == 0
    ? "SONUC: TUM TESTLER GECTI"
    : "SONUC: " + failures.Count + " test basarisiz -> " + string.Join(", ", failures));

return failures.Count == 0 ? 0 : 1;
