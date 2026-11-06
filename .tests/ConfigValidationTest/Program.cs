using System;
using System.Collections.Generic;
using System.IO;
using KCTools.LicenseServer.Configuration;
using KCTools.LicenseServer.Logging;
using Microsoft.Extensions.Configuration;

var failures = new List<string>();

void Check(bool condition, string name)
{
    Console.WriteLine((condition ? "PASS  " : "FAIL  ") + name);

    if (!condition)
    {
        failures.Add(name);
    }
}

static LicenseServerConfiguration Valid() => new LicenseServerConfiguration
{
    ConnectionString =
        "Host=db.example.com;Port=5432;Database=kctools;Username=app;Password=secret;SSL Mode=VerifyFull",
    DatabaseProvider = "postgres",
    AdminKey = new string('a', 32),
    JwtSecret = new string('b', 32),
    OfflineKeyPath = Path.Combine(Path.GetTempPath(), "kctools-secrets", "offline-key.pem"),
    ContentRootPath = Path.Combine(Path.GetTempPath(), "kctools-app"),
    Port = 5443,
    AllowHttp = false,
    OfflineHours = 72
};

var valid = Valid();
Check(valid.ValidateForProduction().Count == 0, "Geçerli production ayarları kabul edildi");

var noConnection = Valid();
noConnection = new LicenseServerConfiguration
{
    ConnectionString = string.Empty,
    DatabaseProvider = "postgres",
    AdminKey = noConnection.AdminKey,
    JwtSecret = noConnection.JwtSecret,
    OfflineKeyPath = noConnection.OfflineKeyPath,
    ContentRootPath = noConnection.ContentRootPath
};
Check(noConnection.ValidateForProduction().Count > 0, "Eksik DB bağlantısı reddedildi");

var sqlite = new LicenseServerConfiguration
{
    ConnectionString = "Data Source=/tmp/kctools.db",
    DatabaseProvider = "sqlite",
    AdminKey = new string('a', 32),
    JwtSecret = new string('b', 32),
    OfflineKeyPath = "/var/lib/kctools/offline-key.pem",
    ContentRootPath = "/srv/kctools"
};
Check(sqlite.ValidateForProduction().Count > 0, "Production'da SQLite reddedildi");

var noSsl = new LicenseServerConfiguration
{
    ConnectionString = "Host=db;Database=kctools;Username=app;Password=secret",
    DatabaseProvider = "postgres",
    AdminKey = new string('a', 32),
    JwtSecret = new string('b', 32),
    OfflineKeyPath = "/var/lib/kctools/offline-key.pem",
    ContentRootPath = "/srv/kctools"
};
Check(noSsl.ValidateForProduction().Count > 0, "Eksik SSL Mode reddedildi");

var weakAdmin = new LicenseServerConfiguration
{
    ConnectionString = valid.ConnectionString,
    DatabaseProvider = "postgres",
    AdminKey = "kisa",
    JwtSecret = new string('b', 32),
    OfflineKeyPath = valid.OfflineKeyPath,
    ContentRootPath = valid.ContentRootPath
};
Check(weakAdmin.ValidateForProduction().Count > 0, "Zayıf admin anahtarı reddedildi");

var http = new LicenseServerConfiguration
{
    ConnectionString = valid.ConnectionString,
    DatabaseProvider = "postgres",
    AdminKey = new string('a', 32),
    JwtSecret = new string('b', 32),
    OfflineKeyPath = valid.OfflineKeyPath,
    ContentRootPath = valid.ContentRootPath,
    AllowHttp = true
};
Check(http.ValidateForProduction().Count > 0, "Production'da HTTP reddedildi");

var noJwt = new LicenseServerConfiguration
{
    ConnectionString = valid.ConnectionString,
    DatabaseProvider = "postgres",
    AdminKey = new string('a', 32),
    JwtSecret = string.Empty,
    OfflineKeyPath = valid.OfflineKeyPath,
    ContentRootPath = valid.ContentRootPath
};
Check(noJwt.ValidateForProduction().Count > 0, "Eksik JWT sırrı reddedildi");

var weakJwt = new LicenseServerConfiguration
{
    ConnectionString = valid.ConnectionString,
    DatabaseProvider = "postgres",
    AdminKey = new string('a', 32),
    JwtSecret = "CHANGE_ME",
    OfflineKeyPath = valid.OfflineKeyPath,
    ContentRootPath = valid.ContentRootPath
};
Check(weakJwt.ValidateForProduction().Count > 0, "Placeholder JWT sırrı reddedildi");

var bootstrapMismatch = new LicenseServerConfiguration
{
    ConnectionString = valid.ConnectionString,
    DatabaseProvider = "postgres",
    AdminKey = new string('a', 32),
    JwtSecret = new string('b', 32),
    OfflineKeyPath = valid.OfflineKeyPath,
    ContentRootPath = valid.ContentRootPath,
    BootstrapAdminEmail = "admin@example.com"
};
Check(bootstrapMismatch.ValidateForProduction().Count > 0, "Eksik bootstrap parolası reddedildi");

var offlineInside = new LicenseServerConfiguration
{
    ConnectionString = valid.ConnectionString,
    DatabaseProvider = "postgres",
    AdminKey = new string('a', 32),
    JwtSecret = new string('b', 32),
    OfflineKeyPath = Path.Combine(valid.ContentRootPath, "offline-key.pem"),
    ContentRootPath = valid.ContentRootPath
};
Check(offlineInside.ValidateForProduction().Count > 0, "İçerik kökündeki offline anahtarı reddedildi");

// ---------------------------------------------------------------- yükleme önceliği
var oldConnection = Environment.GetEnvironmentVariable("KCTOOLS_DB_CONNECTION");

try
{
    Environment.SetEnvironmentVariable("KCTOOLS_DB_CONNECTION", "Host=env;Database=env;SSL Mode=Require");

    var settings = new Dictionary<string, string>
    {
        ["ConnectionStrings:LicenseDb"] = "Host=config;Database=config;SSL Mode=VerifyFull"
    };

    var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    var loaded = LicenseServerConfiguration.Load(configuration, "/srv/kctools");

    Check(loaded.ConnectionString.Contains("Host=config"),
        "appsettings bağlantısı ortam değişkeninden önce gelir");

    var emptyConfiguration = new ConfigurationBuilder().Build();
    var fromEnvironment = LicenseServerConfiguration.Load(emptyConfiguration, "/srv/kctools");

    Check(fromEnvironment.ConnectionString.Contains("Host=env"),
        "Boş appsettings'te ortam değişkeni kullanılır");
}
finally
{
    Environment.SetEnvironmentVariable("KCTOOLS_DB_CONNECTION", oldConnection);
}

// ---------------------------------------------------------------- log gizleme
var redacted = SensitiveDataRedactor.Redact(
    "Host=db;Password=SuperSecret;Username=app X-Admin-Key: abcdef123456 " +
    "Authorization: Bearer eyJhbGciOiJSUzI1NiJ9.payload.signature " +
    "client_secret=topsecret");

Check(!redacted.Contains("SuperSecret"), "Log: DB parolası maskelendi");
Check(!redacted.Contains("abcdef123456"), "Log: admin anahtarı maskelendi");
Check(!redacted.Contains("eyJhbGciOiJSUzI1NiJ9"), "Log: Bearer/JWT maskelendi");
Check(!redacted.Contains("topsecret"), "Log: client_secret maskelendi");

var jsonRedacted = SensitiveDataRedactor.Redact("{\"password\":\"abc123\",\"ok\":true}");
Check(!jsonRedacted.Contains("abc123"), "Log: JSON parola alanı maskelendi");

var normal = "Lisans KC-TEST-TEST-TEST etkinleştirildi.";
Check(SensitiveDataRedactor.Redact(normal) == normal, "Log: normal metin değişmedi");

Console.WriteLine();
Console.WriteLine(failures.Count == 0
    ? "SONUC: TUM TESTLER GECTI"
    : "SONUC: " + failures.Count + " basarisiz -> " + string.Join(", ", failures));

return failures.Count == 0 ? 0 : 1;
