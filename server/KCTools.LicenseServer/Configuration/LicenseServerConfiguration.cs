using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace KCTools.LicenseServer.Configuration
{
    /// <summary>
    /// Sunucunun tüm yapılandırmasının tek okuma noktası. Mevcut ortam değişkeni
    /// adları ve öncelik sırası korunur; sırlar hiçbir zaman ToString/log'a yazılmaz.
    /// </summary>
    public sealed class LicenseServerConfiguration
    {
        private static readonly Regex SslModePattern = new Regex(
            @"SSL\s*Mode\s*=\s*(Require|VerifyCA|VerifyFull)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly string[] Placeholders =
        {
            "CHANGE_ME", "CHANGEME", "PLACEHOLDER", "<", ">"
        };

        public string ConnectionString { get; init; } = string.Empty;

        public string DatabaseProvider { get; init; } = "postgres";

        public string AdminKey { get; init; } = string.Empty;

        public string JwtSecret { get; init; } = string.Empty;

        public string OfflineKeyPath { get; init; } = string.Empty;

        public string ContentRootPath { get; init; } = string.Empty;

        public string BootstrapAdminEmail { get; init; } = string.Empty;

        public string BootstrapAdminPassword { get; init; } = string.Empty;

        public int Port { get; init; } = 5443;

        public bool AllowHttp { get; init; }

        public bool AutoMigrate { get; init; }

        public int OfflineHours { get; init; } = 72;

        public int DatabaseRetryCount { get; init; } = 3;

        public int DatabaseRetryBaseDelayMs { get; init; } = 1000;

        public bool IsSqlite =>
            string.Equals(DatabaseProvider, "sqlite", StringComparison.OrdinalIgnoreCase) ||
            ConnectionString.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase);

        public bool HasDatabase => !string.IsNullOrWhiteSpace(ConnectionString);

        public static LicenseServerConfiguration Load(
            IConfiguration configuration, string contentRootPath)
        {
            var configuredConnection = configuration.GetConnectionString("LicenseDb");

            var connectionString = string.IsNullOrWhiteSpace(configuredConnection)
                ? Read("KCTOOLS_DB_CONNECTION")
                : configuredConnection;

            var rawProvider = Read("KCTOOLS_DB_PROVIDER");
            var provider = string.IsNullOrWhiteSpace(rawProvider)
                ? "postgres"
                : rawProvider.Trim().ToLowerInvariant();

            var offlineKeyPath = Read("KCTOOLS_OFFLINE_KEY_PATH");

            if (string.IsNullOrWhiteSpace(offlineKeyPath))
            {
                offlineKeyPath = Path.Combine(contentRootPath, "offline-key.pem");
            }

            var port = int.TryParse(Read("KCTOOLS_LICENSE_PORT"), out var configuredPort)
                ? configuredPort
                : 5443;

            var offlineHours = int.TryParse(Read("KCTOOLS_OFFLINE_HOURS"), out var configuredHours)
                ? configuredHours
                : 72;

            var retryCount = int.TryParse(Read("KCTOOLS_DB_RETRY_COUNT"), out var configuredRetry)
                ? configuredRetry
                : 3;

            var retryDelay = int.TryParse(
                Read("KCTOOLS_DB_RETRY_BASE_DELAY_MS"), out var configuredDelay)
                ? configuredDelay
                : 1000;

            return new LicenseServerConfiguration
            {
                ConnectionString = connectionString ?? string.Empty,
                DatabaseProvider = provider,
                AdminKey = Read("KCTOOLS_ADMIN_KEY") ?? string.Empty,
                JwtSecret = Read("KCTOOLS_JWT_SECRET") ?? string.Empty,
                OfflineKeyPath = offlineKeyPath ?? string.Empty,
                ContentRootPath = contentRootPath,
                BootstrapAdminEmail = Read("KCTOOLS_BOOTSTRAP_ADMIN_EMAIL") ?? string.Empty,
                BootstrapAdminPassword = Read("KCTOOLS_BOOTSTRAP_ADMIN_PASSWORD") ?? string.Empty,
                Port = port,
                AllowHttp = string.Equals(
                    Read("KCTOOLS_LICENSE_HTTP"), "1", StringComparison.Ordinal),
                AutoMigrate = string.Equals(
                    Read("KCTOOLS_DB_AUTO_MIGRATE"), "1", StringComparison.Ordinal),
                OfflineHours = offlineHours,
                DatabaseRetryCount = retryCount,
                DatabaseRetryBaseDelayMs = retryDelay
            };
        }

        /// <summary>
        /// Yalnızca Production ortamında çağrılır. Dönen metinler yalnızca alan
        /// adlarını ve kuralı içerir; hiçbir sır değeri içermez.
        /// </summary>
        public IReadOnlyList<string> ValidateForProduction()
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(ConnectionString))
            {
                errors.Add(
                    "ConnectionStrings:LicenseDb veya KCTOOLS_DB_CONNECTION zorunlu.");
            }
            else
            {
                if (IsSqlite)
                {
                    errors.Add("Üretimde SQLite kullanılamaz; PostgreSQL bağlantısı gerekli.");
                }

                if (ConnectionString.IndexOf("Host=", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    errors.Add("Bağlantı dizesinde Host= eksik.");
                }

                if (ConnectionString.IndexOf("Database=", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    errors.Add("Bağlantı dizesinde Database= eksik.");
                }

                if (!SslModePattern.IsMatch(ConnectionString))
                {
                    errors.Add(
                        "Bağlantı dizesinde SSL Mode=Require, VerifyCA veya VerifyFull zorunlu.");
                }
            }

            if (!IsSqlite &&
                !string.Equals(DatabaseProvider, "postgres", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("KCTOOLS_DB_PROVIDER üretimde 'postgres' olmalı.");
            }

            if (AllowHttp)
            {
                errors.Add("KCTOOLS_LICENSE_HTTP üretimde kapalı olmalı (HTTPS zorunlu).");
            }

            if (Port < 1 || Port > 65535)
            {
                errors.Add("KCTOOLS_LICENSE_PORT 1-65535 aralığında olmalı.");
            }

            if (string.IsNullOrWhiteSpace(AdminKey))
            {
                errors.Add("KCTOOLS_ADMIN_KEY zorunlu.");
            }
            else if (IsWeakSecret(AdminKey, 32))
            {
                errors.Add("KCTOOLS_ADMIN_KEY en az 32 karakter olmalı ve placeholder içermemeli.");
            }

            if (string.IsNullOrWhiteSpace(JwtSecret))
            {
                errors.Add("KCTOOLS_JWT_SECRET zorunlu.");
            }
            else if (IsWeakSecret(JwtSecret, 32))
            {
                errors.Add("KCTOOLS_JWT_SECRET en az 32 karakter olmalı ve placeholder içermemeli.");
            }

            var hasBootstrapEmail = !string.IsNullOrWhiteSpace(BootstrapAdminEmail);
            var hasBootstrapPassword = !string.IsNullOrWhiteSpace(BootstrapAdminPassword);

            if (hasBootstrapEmail != hasBootstrapPassword)
            {
                errors.Add(
                    "KCTOOLS_BOOTSTRAP_ADMIN_EMAIL ve KCTOOLS_BOOTSTRAP_ADMIN_PASSWORD birlikte tanımlanmalı.");
            }
            else if (hasBootstrapEmail && BootstrapAdminPassword.Length < 12)
            {
                errors.Add("Bootstrap admin parolası en az 12 karakter olmalı.");
            }

            if (OfflineHours < 1 || OfflineHours > 168)
            {
                errors.Add("KCTOOLS_OFFLINE_HOURS 1-168 aralığında olmalı.");
            }

            if (DatabaseRetryCount < 1 || DatabaseRetryCount > 10)
            {
                errors.Add("KCTOOLS_DB_RETRY_COUNT 1-10 aralığında olmalı.");
            }

            if (DatabaseRetryBaseDelayMs < 100 || DatabaseRetryBaseDelayMs > 10000)
            {
                errors.Add("KCTOOLS_DB_RETRY_BASE_DELAY_MS 100-10000 aralığında olmalı.");
            }

            if (AutoMigrate)
            {
                errors.Add(
                    "KCTOOLS_DB_AUTO_MIGRATE üretimde kapalı olmalı; migration ayrı betikle çalıştırılır.");
            }

            if (string.IsNullOrWhiteSpace(OfflineKeyPath))
            {
                errors.Add("KCTOOLS_OFFLINE_KEY_PATH üretimde zorunlu.");
            }
            else if (IsInsideContentRoot(OfflineKeyPath))
            {
                errors.Add(
                    "KCTOOLS_OFFLINE_KEY_PATH uygulama içerik kökü dışında olmalı.");
            }

            return errors;
        }

        private bool IsInsideContentRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(ContentRootPath))
            {
                return false;
            }

            try
            {
                var root = Path.GetFullPath(ContentRootPath)
                    .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

                var target = Path.GetFullPath(path);

                return target.StartsWith(root, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsWeakSecret(string value, int minimumLength)
        {
            if (value.Length < minimumLength)
            {
                return true;
            }

            foreach (var placeholder in Placeholders)
            {
                if (value.IndexOf(placeholder, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static string? Read(string name)
        {
            return Environment.GetEnvironmentVariable(name);
        }

        public override string ToString()
        {
            return "LicenseServerConfiguration { Database=" + DatabaseProvider +
                   ", HasConnection=" + HasDatabase +
                   ", AllowHttp=" + AllowHttp +
                   ", Port=" + Port +
                   ", BootstrapAdmin=" + !string.IsNullOrWhiteSpace(BootstrapAdminEmail) +
                   " }";
        }
    }
}
