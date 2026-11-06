# KC Tools Lisans Veritabanı (PostgreSQL + EF Core)

## Amaç

Lisans verilerini süreç belleğinden çıkarıp kalıcı PostgreSQL'e taşır.
Bağlantı tanımlı değilse sunucu **eski bellek içi modda** çalışmaya devam eder
(geliştirme uyumluluğu).

## Tablolar

| Tablo | İçerik | Kural |
|---|---|---|
| `Users` | kullanıcı adı, PBKDF2-SHA256 parola hash'i (salt + iterasyon), rol | `NormalizedUsername` benzersiz |
| `Licenses` | anahtar, plan, kredi, `StartsAt`/`EndsAt`, `IsRevoked`, `MaxDevices` | `Key` benzersiz; iptal edilen tekrar kullanılamaz |
| `Devices` | HWID, makine adı, ilk/son görülme, `IsActive` | `(LicenseId, Hwid)` benzersiz; varsayılan tek cihaz |
| `RefreshTokens` | yalnızca SHA-256 hash, son kullanma, iptal, yerine geçen token | `TokenHash` benzersiz; rotasyon zorunlu |
| `AuditLogs` | aktör, eylem, varlık, ayrıntı, IP, zaman | yönetici işlemleri zorunlu |

## Yapılandırma

```powershell
$env:KCTOOLS_DB_CONNECTION = "Host=localhost;Port=5432;Database=kctools;Username=kctools;Password=GUCLU_PAROLA"
$env:KCTOOLS_JWT_SECRET   = "en-az-32-bayt-rastgele-anahtar"
$env:KCTOOLS_ADMIN_KEY    = "admin-icin-uzun-rastgele-anahtar"
$env:KCTOOLS_DB_AUTO_MIGRATE = "1"   # açılışta migration uygula
```

Alternatif: `appsettings.json` → `ConnectionStrings:LicenseDb`.

## Migration

```powershell
# yeni migration
dotnet ef migrations add <Ad> --project server/KCTools.LicenseServer --startup-project server/KCTools.LicenseServer --output-dir Data/Migrations

# SQL üret (çevrimdışı)
dotnet ef migrations script --project server/KCTools.LicenseServer --startup-project server/KCTools.LicenseServer -o Data/Migrations/InitialCreate.sql

# veritabanına uygula
dotnet ef database update --project server/KCTools.LicenseServer --startup-project server/KCTools.LicenseServer
```

Mevcut migration: `Data/Migrations/20261104230418_InitialCreate.cs` + `.sql`.

## API

Eski uç noktalar korunur; veritabanı bağlıysa aynı yollar DB üzerinden çalışır:

- `POST /api/v1/auth/activate` — anahtar + HWID → JWT (tek cihaz kuralı)
- `GET /api/v1/license/status` — token + HWID doğrulama
- `POST /api/v1/license/consume` — kredi düşme
- `POST /api/v1/auth/register` — kullanıcı oluştur (3-32 karakter, parola ≥ 8)
- `POST /api/v1/auth/login` — parola doğrula → refresh token (30 gün)
- `POST /api/v1/auth/refresh` — rotasyon; eski token tekrar kullanılamaz
- `POST /api/v1/admin/licenses` — `X-Admin-Key` ile lisans oluştur
- `POST /api/v1/admin/licenses/{key}/revoke` — iptal + audit
- `GET /api/v1/admin/audit?take=100` — son kayıtlar

### Örnekler

```bash
# yönetici lisans oluştur
curl -X POST http://localhost:5080/api/v1/admin/licenses \
  -H "X-Admin-Key: $KCTOOLS_ADMIN_KEY" -H "Content-Type: application/json" \
  -d '{"plan":"PRO","credits":100,"days":365}'

# kayıt
curl -X POST http://localhost:5080/api/v1/auth/register \
  -H "Content-Type: application/json" \
  -d '{"username":"demo","password":"Password123!"}'

# giriş
curl -X POST http://localhost:5080/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"demo","password":"Password123!"}'
```

## Güvenlik notları

- Parolalar **PBKDF2-SHA256, 100.000 iterasyon, 16 bayt salt** ile saklanır; düz metin yok.
- Refresh token'ın yalnızca **hash'i** veritabanına yazılır; ham token istemciye bir kez verilir.
- İptal edilen lisansın tüm cihazları pasifleştirilir; yeniden aktivasyon `409` döner.
- Yönetici uç noktaları `KCTOOLS_ADMIN_KEY` tanımlı değilse **kapalıdır**.
- `KCTOOLS_JWT_SECRET` üretimde zorunludur; tanımlı değilse geçici anahtar üretilir ve
  yeniden başlatmada token'lar geçersiz olur.

## Kapsam dışı (sonraki faz)

- Access JWT + `iss/aud` doğrulaması ve refresh ile birleşik oturum
- Kredi ledger'ı ve idempotency anahtarı
- Çoklu örnek (multi-instance) için dağıtık kilit / concurrency token
- Rate limiting, e-posta doğrulama, parola sıfırlama

## Identity (Faz 1)

- `AppUser : IdentityUser<Guid>`, `AppRole : IdentityRole<Guid>`; roller: **Admin**, **Customer**
- Parola: Identity `PasswordHasher` (PBKDF2 v3), min 10 karakter + karmaşıklık
- Kilitleme: **5 hatalı deneme → 15 dakika**; `IsLockedOutAsync` kontrolü
- Admin bootstrap: `KCTOOLS_BOOTSTRAP_ADMIN_EMAIL` + `KCTOOLS_BOOTSTRAP_ADMIN_PASSWORD`
  (kaynak kodda sabit hesap yok; tanımlı değilse admin oluşturulmaz)
- Çerez: HttpOnly, SameSite=Lax, üretimde Secure; 30 dk kayan oturum
- Migration: `20261105052106_Identity` (Users → AspNet* tabloları)

## Cihaz anahtarı (Faz 2)

- İstemci **ECDSA P-256** anahtar çifti üretir; özel anahtar istemcide (CNG/DPAPI) kalır.
- `POST /api/v1/devices/challenge` → 5 dk geçerli tek kullanımlık nonce
- `POST /api/v1/devices/activate` → nonce imzası + açık anahtar; sunucu SPKI parmak izini saklar
- Aynı lisans **tek aktif cihaz**; ikinci bilgisayar `409`
- Nonce tekrarı ve geçersiz imza `401`
- Admin sıfırlama: `POST /api/v1/admin/devices/{deviceId}/reset` → cihaz pasif, yeni aktivasyon mümkün
- Geçmiş: `ActivationHistory`; admin sorgu: `GET /api/v1/admin/activation-history`
- Migration: `20261105052338_DeviceKeys`

## Kapsam dışı (sıradaki fazlar)

- OpenIddict OIDC + PKCE ve WPF giriş ekranı
- MVC yönetim paneli (8 sayfa) + CSRF + cookie/rol koruması
- 72 saatlik imzalı offline entitlement politikası

## Web yönetim paneli (Faz 3)

- MVC + Razor, Identity cookie oturumu, koyu responsive tema (`wwwroot/site.css`)
- Sayfalar: `/Admin` (dashboard), `/Admin/Users`, `/Admin/Licenses`, `/Admin/Devices`,
  `/Admin/Duration`, `/Admin/Audit`, `/Admin/Settings`, `/Account/Login`
- Müşteri: `/Portal` — kendi hesabı, lisansları ve cihazları
- Yetki: `[Authorize(Roles="Admin")]`; Customer → `/account/denied`
- CSRF: tüm POST formlarında antiforgery token (token'sız istek **400**)
- Kilitleme: 5 hatalı giriş → 15 dk; panelde "hesap kilitlendi" mesajı
- Cookie: HttpOnly, SameSite=Lax, üretimde Secure
- Admin işlemleri: kullanıcı oluştur/devre dışı, lisans oluştur/uzat/dondur/aktifleştir/iptal,
  cihaz sıfırla; hepsi `AuditLogs`'a yazılır

## SQLite ile yerel test

PostgreSQL kurulu olmayan geliştirme ortamında:

```powershell
$env:KCTOOLS_DB_PROVIDER = "sqlite"
$env:KCTOOLS_DB_CONNECTION = "Data Source=C:\temp\kctools.db"
$env:KCTOOLS_DB_AUTO_MIGRATE = "1"
```

SQLite modunda `EnsureCreated()` kullanılır; PostgreSQL üretimde migration ile yönetilir.

## OIDC + PKCE (Faz 4)

- Sağlayıcı: **OpenIddict 5.8**; uçlar `/connect/authorize`, `/connect/token`, discovery (`/.well-known/openid-configuration`)
- İstemci: `kctools-desktop` (public), **PKCE S256 zorunlu**, redirect `http://127.0.0.1:45999/callback`
- Scope: `openid profile offline_access kctools.license`
- Access token: kısa ömürlü, **şifreli JWE**; refresh token destekli
- API: `GET /api/v1/me`, `GET /api/v1/me/license` (Bearer)
- Yanlış `code_verifier` → **400**
- `offlineToken`: **RS256** imzalı JWT; `offline_until = min(şimdi+72s, lisans bitişi)`;
  süresi dolmuş lisansta tolerans **yok**
- Açık anahtar: `GET /api/v1/license/offline-public-key`
- Geliştirme HTTP modu: `KCTOOLS_LICENSE_HTTP=1` (yalnızca yerel)

Örnek akış (özet): tarayıcı `/connect/authorize?...&code_challenge=...&code_challenge_method=S256`
→ login → `code` → `/connect/token` (`code_verifier`) → access/refresh → `/api/v1/me/license`.

> WPF masaüstü OIDC istemcisi ve 72 saatlik yerel offline uygulaması bir sonraki fazda.

## WPF masaüstü girişi ve offline politika (Faz 5)

- `KCTOOLS_OIDC_ENABLED=1` ile lisans kapısı etkinleşir; aksi halde uygulama eski davranışla açılır.
- İlk açılışta **LoginWindow**: sistem tarayıcısı + loopback (port 45999) + PKCE S256.
- Cihaz anahtarı: **Windows CNG** (dışa aktarılamayan ECDSA P-256); CNG yoksa DPAPI dosyası.
  Cihaz kimliği açık anahtardan türetilir.
- Giriş sonrası `/api/v1/me/license` çağrılır; cihaz yoksa challenge/activate otomatik yapılır.
- İptal / donmuş / süre dolmuş → erişim kapalı (uygulama açılmaz).
- Sunucuya ulaşılamazsa **imzalı offline token** RS256 ile doğrulanır:
  `exp`, `offline_until` ve `license_end` ayrı ayrı kontrol edilir; lisans bitişi aşılamaz.
- Bağlantı dönünce oturum yeniden doğrulanır (access → refresh → offline sırası).

Ortam değişkenleri:

```powershell
$env:KCTOOLS_OIDC_ENABLED = "1"
$env:KCTOOLS_OIDC_AUTHORITY = "https://license.example.com"
$env:KCTOOLS_LICENSE_API = "https://license.example.com"
$env:KCTOOLS_OIDC_CLIENT_ID = "kctools-desktop"
$env:KCTOOLS_OIDC_PORT = "45999"
```
