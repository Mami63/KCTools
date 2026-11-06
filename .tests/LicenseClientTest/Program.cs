using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using KCTools.Security;
using KCTools.LicenseServer.Data;
using KCTools.LicenseServer.Services;

var failures = new List<string>();

void Check(bool condition, string name)
{
    Console.WriteLine((condition ? "PASS  " : "FAIL  ") + name);

    if (!condition)
    {
        failures.Add(name);
    }
}

// ---------------------------------------------------------------- cihaz anahtarı
var materialA = DeviceKeyStore.GetOrCreate();
var materialB = DeviceKeyStore.GetOrCreate();

Check(materialA.DeviceId != Guid.Empty, "Anahtar: cihaz kimliği üretildi");
Check(materialA.DeviceId == materialB.DeviceId, "Anahtar: DPAPI deposundan aynı kimlik");
Check(materialA.PublicKeyPem == materialB.PublicKeyPem, "Anahtar: açık anahtar kalıcı");

var signature = materialA.Sign("test-nonce");
using var verifyKey = ECDsa.Create();
verifyKey.ImportFromPem(materialA.PublicKeyPem);

Check(verifyKey.VerifyData(
        System.Text.Encoding.UTF8.GetBytes("test-nonce"),
        Convert.FromBase64String(signature), HashAlgorithmName.SHA256),
    "Anahtar: imza doğrulandı");

// ---------------------------------------------------------------- offline politika
var keyPath = Path.Combine(AppContext.BaseDirectory, "client-offline-key.pem");
if (File.Exists(keyPath)) File.Delete(keyPath);

var offline = new OfflineEntitlementService(keyPath);
var license = new License
{
    Key = "KC-CLNT-TEST-TEST",
    Plan = "PRO",
    Credits = 10,
    StartsAt = DateTimeOffset.UtcNow.AddDays(-1),
    EndsAt = DateTimeOffset.UtcNow.AddDays(30)
};

var device = new Device
{
    DeviceId = materialA.DeviceId,
    Hwid = "HWID-CLIENT-1",
    IsActive = true
};

var session = new OidcSession
{
    OfflineToken = offline.Issue(license, device, Guid.NewGuid(), 72),
    PublicKeyPem = offline.PublicKeyPem,
    LicenseEnd = license.EndsAt
};

var gate = new LicenseGate();
Check(gate.ValidateOffline(session), "Offline: geçerli imzalı yetki kabul edildi");

var tampered = session.OfflineToken.Split('.');
var tamperedPayload = tampered[1].Substring(0, tampered[1].Length - 2) + "AB";
var tamperedSession = new OidcSession
{
    OfflineToken = tampered[0] + "." + tamperedPayload + "." + tampered[2],
    PublicKeyPem = offline.PublicKeyPem
};

Check(!gate.ValidateOffline(tamperedSession), "Offline: kurcalanmış token reddedildi");

var expired = new License
{
    Key = "KC-CLNT-EXPD-EXPD",
    Plan = "PRO",
    StartsAt = DateTimeOffset.UtcNow.AddDays(-10),
    EndsAt = DateTimeOffset.UtcNow.AddHours(-1)
};

var expiredSession = new OidcSession
{
    OfflineToken = offline.Issue(expired, device, Guid.NewGuid(), 72),
    PublicKeyPem = offline.PublicKeyPem
};

Check(!gate.ValidateOffline(expiredSession), "Offline: süresi dolmuş lisans reddedildi");

var otherDevice = new Device
{
    DeviceId = Guid.NewGuid(),
    Hwid = "HWID-OTHER",
    IsActive = true
};

var mismatched = new OidcSession
{
    OfflineToken = offline.Issue(license, otherDevice, Guid.NewGuid(), 72),
    PublicKeyPem = offline.PublicKeyPem
};

Check(!gate.ValidateOffline(mismatched), "Offline: başka cihazın yetkisi reddedildi");

var empty = new OidcSession();
Check(!gate.ValidateOffline(empty), "Offline: yetki yoksa reddedildi");

gate.Dispose();

Console.WriteLine();
Console.WriteLine(failures.Count == 0
    ? "SONUC: TUM TESTLER GECTI"
    : "SONUC: " + failures.Count + " basarisiz -> " + string.Join(", ", failures));

return failures.Count == 0 ? 0 : 1;
