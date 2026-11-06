# Cihaz Kataloğu (Marka / Model Veritabanı)

KC Tools'un model kataloğu tek bir JSON dosyasında tutulur:

```
src/KCTools/Assets/DeviceCatalog.json   → build/publish çıktısına kopyalanır
AppContext.BaseDirectory/Assets/DeviceCatalog.json
```

Yeni model eklemek için yalnızca bu dosyayı düzenleyip uygulamayı yeniden başlatmak yeterlidir
(kod değişikliği gerekmez).

## Şema

```json
{
  "schemaVersion": 1,
  "updated": "2026-11-04",
  "brands": [
    {
      "id": "samsung",
      "name": "Samsung",
      "vidPids": ["04E8"],
      "models": [
        {
          "code": "SM-S918B",
          "name": "Galaxy S23 Ultra",
          "codename": "dm3q",
          "platform": "Snapdragon 8 Gen 2",
          "modes": ["adb", "fastboot", "download", "odin"],
          "tested": false,
          "notes": ""
        }
      ]
    }
  ]
}
```

| Alan | Zorunlu | Açıklama |
|---|---|---|
| `schemaVersion` | evet | Şu an `1` |
| `brands[].id` | evet | Benzersiz kimlik (`samsung`, `xiaomi`…) |
| `brands[].name` | evet | Görünen ad |
| `brands[].vidPids` | hayır | USB Vendor ID ipuçları (`04E8`, `2717`…) |
| `models[].code` | evet | `ro.product.model` ile eşleşen pazarlama kodu |
| `models[].name` | hayır | Görünen model adı |
| `models[].codename` | hayır | `ro.product.device` / fastboot `product` (en güvenilir sinyal) |
| `models[].platform` | hayır | SoC / platform |
| `models[].modes` | hayır | `adb`, `fastboot`, `recovery`, `download`, `odin`, `edl` |
| `models[].tested` | evet | **Gerçek cihazda doğrulandı mı?** Arayüzde yeşil/kırmızı rozet |
| `models[].notes` | hayır | Serbest not |

## Doğrulama

`DeviceCatalogService.Validate` yükleme sırasında:

- `schemaVersion != 1` → uyarı
- kimliksiz marka / kodu boş model → atlanır
- tekrar eden marka `id` / model `code` → uyarı
- bozuk JSON → boş katalog + uyarı (uygulama çökmez)

Uyarılar günlüğe `KATALOG` kaynağıyla yazılır.

## Eşleştirme sırası

| Sıra | SinyaI | Güven |
|---|---|---|
| 1 | `ro.product.model` == `code` | **KESİN** |
| 2 | `ro.product.device` / fastboot `product` == `codename` | **YÜKSEK** |
| 3 | model adı benzerliği | ORTA |
| 4 | VID/PID → yalnızca marka | TAHMİNÎ |
| 5 | üretici adı → marka | TAHMİNÎ |
| 6 | kullanıcı ataması | **MANUEL** |

> Kod veya codename doğrulanmadan hiçbir kayıt "KESİN" sayılmaz.

## Manuel atama (override)

Tanınmayan cihazlar için Katalog sayfasında model seçilip **"Bu Cihaza Ata"** denir.
Kayıt seri numarasına bağlı olarak saklanır:

```
%LOCALAPPDATA%\KCTools\device-overrides.json
```

Yazma izni yoksa uygulama klasörüne düşer. Dosya silinirse atamalar sıfırlanır.

## "Test edilmedi" kuralı

- `tested: false` → arayüzde **TEST EDİLMEDİ** (sarı) rozet
- `tested: true` → **TEST EDİLDİ** (yeşil) rozet — yalnızca gerçek cihazda model **ve** codename
  doğrulandıktan sonra işaretlenir

Katalogda 960 model vardır; yalnızca `Redmi Note 14 (2508CRN2BE / tornado)` gerçek cihazda doğrulanmıştır.
Diğer kayıtlar topluluk verisidir; codename'i boş olanlar yalnızca model koduyla eşleşir.

## Sınırlar

- VID/PID tablosu üretici standardı değildir; yalnızca marka tahmini içindir.
- Katalog yalnızca **tanımlama** yapar; cihaz üzerinde işlem (flash/format) mevcut Cihaz
  sayfasındaki ayrı akışlarda kalır.
- IMEI değiştirme / klonlama / kimlik manipülasyonu bu modülün kapsamı dışındadır.

