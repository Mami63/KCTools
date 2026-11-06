# Üretim Yapılandırması (Aşama A)

Bu belge **yalnızca Aşama A** kapsamını açıklar: merkezi yapılandırma,
production doğrulaması, secrets yönetimi ve log gizleme.
Sertifikalar (B/C), yedekleme (B), admin sertleştirme (D) ve HTTPS sonlandırma (E)
ayrı aşamalarda ele alınacaktır.

## Merkezi yapılandırma

Tüm ayarlar `LicenseServerConfiguration` sınıfından okunur:

- `ConnectionStrings:LicenseDb` (appsettings) → yoksa `KCTOOLS_DB_CONNECTION`
- `KCTOOLS_DB_PROVIDER` (varsayılan `postgres`)
- `KCTOOLS_ADMIN_KEY`
- `KCTOOLS_JWT_SECRET`
- `KCTOOLS_OFFLINE_KEY_PATH` (yoksa içerik kökü/offline-key.pem)
- `KCTOOLS_LICENSE_PORT` (varsayılan 5443)
- `KCTOOLS_LICENSE_HTTP=1` (yalnızca geliştirme)
- `KCTOOLS_DB_AUTO_MIGRATE=1`
- `KCTOOLS_OFFLINE_HOURS` (1-168, varsayılan 72)
- `KCTOOLS_BOOTSTRAP_ADMIN_EMAIL` / `KCTOOLS_BOOTSTRAP_ADMIN_PASSWORD`

Mevcut isimler ve öncelik sırası **değişmedi**; Development davranışı aynıdır.

## Production doğrulaması (fail-fast)

`ASPNETCORE_ENVIRONMENT=Production` iken sunucu, aşağıdaki kurallardan biri
sağlanmazsa **başlamaz** ve yalnızca alan adı + kural içeren bir hata yazar:

| Kural | Açıklama |
|---|---|
| DB bağlantısı | Boş olamaz; SQLite kullanılamaz |
| Bağlantı içeriği | `Host=` ve `Database=` bulunmalı |
| TLS | `SSL Mode=Require`, `VerifyCA` veya `VerifyFull` |
| Sağlayıcı | `postgres` |
| HTTP | `KCTOOLS_LICENSE_HTTP` kapalı olmalı |
| Port | 1–65535 |
| Admin anahtarı | Zorunlu, ≥32 karakter, placeholder içermemeli |
| JWT sırrı | Zorunlu, ≥32 karakter, placeholder içermemeli |
| Bootstrap | E-posta ve parola birlikte; parola ≥12 karakter |
| Offline saat | 1–168 |
| Offline anahtar | Yol zorunlu ve uygulama içerik kökü **dışında** |

Hata mesajları hiçbir sır değeri içermez.

## Secrets yönetimi

- Değerler **ortam değişkeni** veya secret manager’dan gelir; repoya yazılmaz.
- `.env.example` yalnızca şablon içerir; gerçek `.env` dosyası `.gitignore`’dadır.
- `LicenseServerConfiguration.ToString()` yalnızca “tanımlı/tanımsız” özeti verir.
- Öneri: üretimde Azure Key Vault / HashiCorp Vault / AWS Secrets Manager;
  dosya tabanlı `.env` yalnızca kısıtlı erişimli sunucuda.

## Log gizleme

Production’da `RedactingConsoleFormatter` kullanılır. Aşağıdaki desenler maskelenir:

- `Password=...`, `pwd=...`
- `X-Admin-Key: ...`
- `Authorization: Bearer ...`
- `client_secret=...`
- JSON içindeki `password/secret/token/access_token/refresh_token`
- JWT benzeri `eyJ...` dizileri

Ayrıca `appsettings.Production.json` içinde EF Core SQL/komut logları `Warning`
seviyesine çekilmiştir; böylece parametreler loglanmaz.

## Development

`ASPNETCORE_ENVIRONMENT=Development` (veya tanımsız) iken doğrulama çalışmaz;
mevcut bellek içi/SQLite geliştirme davranışı korunur.

## Örnek (yalnızca placeholder)

```bash
export ASPNETCORE_ENVIRONMENT=Production
export KCTOOLS_DB_CONNECTION="Host=db.example.com;Database=kctools;Username=kctools_app;Password=CHANGE_ME;SSL Mode=VerifyFull"
export KCTOOLS_JWT_SECRET="CHANGE_ME_32_BYTES_MINIMUM_RANDOM"
export KCTOOLS_ADMIN_KEY="CHANGE_ME_32_BYTES_MINIMUM_RANDOM"
export KCTOOLS_OFFLINE_KEY_PATH="/var/lib/kctools/offline-key.pem"
export KCTOOLS_OFFLINE_HOURS="72"
export KCTOOLS_LICENSE_PORT="5443"
dotnet KCTools.LicenseServer.dll
```

## Kapsam dışı (sonraki aşamalar)

- PostgreSQL migration/backup otomasyonu (B)
- OpenIddict PFX sertifikaları, offline anahtar rotasyonu (C)
- Bootstrap tek seferlik akışı, admin key’in kaldırılması (D)
- HTTPS/HSTS/reverse proxy/rate limiting (E)
- Gerçek tarayıcı, ikinci bilgisayar, iptal/offline saha testleri (F)
