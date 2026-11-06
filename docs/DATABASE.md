# Veritabanı Mimarisi ve Operasyon (Aşama B)

## Sağlayıcılar

| Ortam | Sağlayıcı | Not |
|---|---|---|
| Development (DB yok) | bellek içi | mevcut davranış korunur |
| Development | SQLite / PostgreSQL | SQLite `EnsureCreated`, PG migration |
| Production | PostgreSQL 16 | migration ayrı çalıştırılır; `AUTO_MIGRATE` yasak |

EF Core 8 / Npgsql 8 kullanılır. PostgreSQL 16 ile uyumludur; bu makinede
PostgreSQL kurulu olmadığı için **canlı PG testi yapılmamıştır**.

## Bağlantı ve retry

- `ConnectionStrings:LicenseDb` veya `KCTOOLS_DB_CONNECTION`
- Npgsql: `EnableRetryOnFailure(5, 10 sn)` — geçici bağlantı hatalarında otomatik retry
- Readiness: `KCTOOLS_DB_RETRY_COUNT` (varsayılan 3), `KCTOOLS_DB_RETRY_BASE_DELAY_MS`
  (varsayılan 1000); üstel geri çekilme, deneme başına 4 sn zaman aşımı

## Health / readiness

| Uç | Amaç | DB |
|---|---|---|
| `GET /health`, `/api/v1/health` | liveness | dokunmaz |
| `GET /health/ready`, `/api/v1/health/ready` | readiness | `CanConnect` + pending migration |

- Hazır: `200 { "ready": true, "status": "ready" }`
- DB yok/erişilemez: `503`
- PostgreSQL’de bekleyen migration varsa: `503 "pending_migrations"`
- SQLite’da migration geçmişi olmadığından yalnızca bağlantı kontrol edilir.

Hata metinleri `SensitiveDataRedactor` ile maskelenir.

## Migration (uygulamadan bağımsız)

Production’da uygulama başlangıcında migration **çalışmaz**
(`KCTOOLS_DB_AUTO_MIGRATE` production doğrulamasında reddedilir).
Yalnızca Development’ta açıkça istenirse çalışır.

```powershell
# 1) Bundle üret (bir kez / her sürümde)
# dotnet PATH'te değilse -EfPath ile dotnet-ef yolu verilebilir.
./scripts/migrate-bundle.ps1 -DotnetPath .\.dotnet\dotnet.exe `
    -EfPath .\.tools\dotnet-ef\dotnet-ef.exe

# 2) Migration öncesi yedek + uygula
$env:KCTOOLS_DB_MIGRATION_CONNECTION = 'Host=...;Database=kctools;Username=kctools_migrator;Password=...;SSL Mode=VerifyFull'
./scripts/migrate.ps1 -UseBundle -DotnetPath .\.dotnet\dotnet.exe

# Alternatif: doğrudan EF ile
./scripts/migrate.ps1 -DotnetPath .\.dotnet\dotnet.exe
```

`migrate.ps1` varsayılan olarak önce `backup-db.ps1` çalıştırır; yedek başarısızsa durur.

## Roller

| Rol | Yetki |
|---|---|
| `kctools_migrator` | DB sahibi, DDL/migration |
| `kctools_app` | yalnızca SELECT/INSERT/UPDATE/DELETE + sequence |

```powershell
$env:KCTOOLS_DB_ADMIN_CONNECTION = 'Host=...;Database=postgres;Username=postgres;Password=...'
$env:KCTOOLS_DB_NAME = 'kctools'
$env:KCTOOLS_DB_APP_PASSWORD = '...'
$env:KCTOOLS_DB_MIGRATOR_PASSWORD = '...'
./scripts/setup-roles.ps1
```

Parolalar kaynak kodda tutulmaz; yalnızca ortam değişkenlerinden okunur ve geçici
SQL dosyası `icacls` ile kısıtlanıp silinir.

## Yedekleme / geri yükleme

```powershell
# Yedek (pg_dump -Fc, SHA256, retention)
$env:KCTOOLS_DB_MIGRATION_CONNECTION = '...'
./scripts/backup-db.ps1 -Label manual -RetentionDays 14

# Geri yükleme: varsayılan olarak YENİ veritabanına
./scripts/restore-db.ps1 -BackupFile .\backups\kctools-manual-....dump
```

- `restore-db.ps1` mevcut veritabanının üzerine yazmaz; `kctools_restore_<zaman>` oluşturur.
- `.sha256` varsa doğrulanır.
- `pg_dump`/`pg_restore` bu makinede kurulu değil; betikler **çalıştırılmadı**,
  yalnızca sözdizimi ve `-DryRun` kontrolü yapıldı.
- Üretimde gecelik yedek + offsite kopya + periyodik restore tatbikatı önerilir.

## Migration geri alma

- Her migration öncesi `backup-db.ps1` çalışır.
- Geri alma: yeni migration ile ileri düzeltme veya yedekten geri yükleme.
- `dotnet ef database update <OncekiMigration>` yalnızca geliştirmede kullanılmalı;
  üretimde veri kaybı riski nedeniyle yedek + onay gerekir.

## Bu aşamada doğrulanmayanlar

- Gerçek PostgreSQL 16 sunucusuna migration/readiness testi
- `pg_dump` / `pg_restore` gerçek çalıştırma
- Rol oluşturma (`setup-roles.ps1`) gerçek çalıştırma
- Migration bundle’ın gerçek DB’ye uygulanması
## Betik çalıştırma notları

- PowerShell execution policy kısıtlıysa:
  `pwsh -ExecutionPolicy Bypass -File .\scripts\migrate.ps1`
- `dotnet` PATH'te değilse `-DotnetPath`; dotnet-ef ayrı kuruluysa `-EfPath` kullanın.
- Bundle, migration sırasında `KCTOOLS_DB_CONNECTION` ortam değişkenini okur;
  bağlantı dizesi komut satırına yazılmaz.
- `pg_dump`/`pg_restore` için PostgreSQL istemci araçları PATH'te olmalı veya
  `KCTOOLS_PG_DUMP` / `KCTOOLS_PG_RESTORE` ile tam yol verilmeli.
