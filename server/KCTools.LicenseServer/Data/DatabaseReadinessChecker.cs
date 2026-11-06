using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KCTools.LicenseServer.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KCTools.LicenseServer.Data
{
    public sealed class ReadinessReport
    {
        public bool Ready { get; set; }

        public string Status { get; set; } = "not_ready";

        public string Database { get; set; } = "unknown";

        public int Attempts { get; set; }

        public int PendingMigrations { get; set; }

        public string? Error { get; set; }

        public DateTimeOffset CheckedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Gerçek veritabanı bağlantısını sınırlı deneme ve üstel geri çekilme ile
    /// doğrular. Hata metinleri SensitiveDataRedactor'dan geçirilir.
    /// </summary>
    public sealed class DatabaseReadinessChecker
    {
        private readonly LicenseDbContext _database;
        private readonly ILogger<DatabaseReadinessChecker> _logger;
        private readonly int _attempts;
        private readonly int _baseDelayMs;

        public DatabaseReadinessChecker(
            LicenseDbContext database,
            ILogger<DatabaseReadinessChecker> logger,
            int attempts,
            int baseDelayMs)
        {
            _database = database;
            _logger = logger;
            _attempts = Math.Clamp(attempts, 1, 10);
            _baseDelayMs = Math.Clamp(baseDelayMs, 100, 10000);
        }

        public async Task<ReadinessReport> CheckAsync(CancellationToken cancellationToken)
        {
            var provider = _database.Database.ProviderName ?? "unknown";
            var isPostgres = provider.IndexOf("Npgsql", StringComparison.OrdinalIgnoreCase) >= 0;
            var lastError = "Bağlantı kurulamadı.";

            for (var attempt = 1; attempt <= _attempts; attempt++)
            {
                using var attemptTimeout =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attemptTimeout.CancelAfter(TimeSpan.FromSeconds(4));

                try
                {
                    var connected = await _database.Database
                        .CanConnectAsync(attemptTimeout.Token);

                    if (!connected)
                    {
                        lastError = "Veritabanı bağlantısı kurulamadı.";
                    }
                    else
                    {
                        var pending = 0;

                        // SQLite geliştirmede EnsureCreated kullanır; migration geçmişi yoktur.
                        if (isPostgres)
                        {
                            var migrations = await _database.Database
                                .GetPendingMigrationsAsync(attemptTimeout.Token);

                            pending = migrations.Count();
                        }

                        return new ReadinessReport
                        {
                            Ready = pending == 0,
                            Status = pending == 0 ? "ready" : "pending_migrations",
                            Database = provider,
                            Attempts = attempt,
                            PendingMigrations = pending,
                            Error = pending == 0
                                ? null
                                : "Bekleyen migration sayısı: " + pending
                        };
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    lastError = "Veritabanı bağlantısı zaman aşımına uğradı.";
                }
                catch (Exception ex)
                {
                    lastError = SensitiveDataRedactor.Redact(ex.Message);
                }

                _logger.LogWarning(
                    "Veritabanı readiness denemesi {Attempt}/{Total} başarısız: {Error}",
                    attempt, _attempts, lastError);

                if (attempt < _attempts)
                {
                    var delay = _baseDelayMs * (1 << (attempt - 1));
                    await Task.Delay(delay, cancellationToken);
                }
            }

            return new ReadinessReport
            {
                Ready = false,
                Status = "not_ready",
                Database = provider,
                Attempts = _attempts,
                Error = SensitiveDataRedactor.Redact(lastError)
            };
        }
    }
}
