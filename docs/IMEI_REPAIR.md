# IMEI Onarımı ve Firmware — Kapsam ve Güvenli İş Akışı

> **Yasal uyarı:** IMEI değiştirmek birçok ülkede yasal sınırlamalara tabidir.
> Bu araç yalnızca **sahibi olunan cihazlarda**, kendi yedeğinizi geri yüklemek veya
> kendi cihazınızın kaydını onarmak için kullanılmalıdır. Cihaz kimliği klonlama,
> başkasının IMEI'sini kopyalama veya kimlik gizleme bu modülün kapsamı dışındadır.

## Kapsam (ne var / ne yok)

| Var | Yok |
|---|---|
| QCN dosyasından IMEI okuma, Luhn doğrulama | Cihazın NV belleğine doğrudan yazma |
| QCN içinde ASCII + BCD eşleşmelerini değiştirme | Root atma / Magisk kurulumu |
| Yedekli kaydetme (zaman damgalı `.qcn.bak`) | IMEI klonlama / başka cihazın kimliğini kopyalama |
| Yedek listesi, geri yükleme, güvenlik kopyası | Yetkisiz EFS/NV erişimi |
| SHA-256 bütünlük doğrulama | Exploit / ROM zafiyeti kullanımı |
| Fastboot ile imaj ve klasör flash | Bootloader kilidi zorla açma (yalnızca resmi `flashing unlock`) |

**Neden root yok:** QCN dosyası düzenleme dosya seviyesinde çalışır; cihazın NV
belleğine yazmak root/diag erişimi gerektirir ve geri dönüşü zordur. Bu proje
bilinçli olarak yalnızca **dosya + resmi fastboot** yolunu kullanır.

## Profesyonel iş akışı

1. **Yedek al** — QPST/üretici aracıyla cihazın QCN yedeğini al (veya mevcut yedeği kullan).
2. **Yükle** — IMEI Onarımı → `QCN Seç`. Uygulama SHA-256 ve boyutu gösterir.
3. **Doğrula** — `Doğrula` ile dosya bütünlüğünü kontrol et.
4. **Eşleşme bul** — Mevcut IMEI'yi oku (AT veya ADB) → `Eşleşmeleri Ara` (ASCII + BCD).
5. **Yeni IMEI** — 15 hane gir; `Luhn Düzelt` kontrol hanesini hesaplar.
6. **Onayla** — yasal onay kutusu → `IMEI Onar`.
7. **Yedekli kaydet** — otomatik `.qcn.bak` oluşur.
8. **Geri yükle (gerekirse)** — `Yedekler` listesinden seç → `Geri Yükle`;
   önce mevcut dosyanın güvenlik kopyası alınır.

## Firmware

| İşlem | Yöntem |
|---|---|
| Tek imaj flash | `İmaj Flash`: bölüm seç + `.img` seç |
| Klasörden flash | `Klasörden Flash`: `boot.img`, `recovery.img`, `vendor_boot.img`, `vbmeta.img`… bilinen bölümler sırayla yazılır |
| ZIP güncelleme | `ZIP Sideload` (recovery/sideload modu) |

Bilinmeyen dosya adları **atlanır**; yanlış bölüme yazma riski yoktur. Her adım onay ister
ve günlüğe yazılır.

## İncelenen referanslar (kod kopyalanmadı)

- [bkerler/edl](https://github.com/bkerler/edl) — Qualcomm EDL/Firehose akışı ve Sahara
- [Alephgsm/qualcomm-diag-protocol](https://github.com/Alephgsm/qualcomm-diag-protocol) — diag/EFS erişiminin nasıl çalıştığı (yalnızca bilgi)
- [Muhmmad-Almuhmmah/SimpleQcnParser](https://relatedrepos.com/gh/Muhmmad-Almuhmmah/SimpleQcnParser) — QCN ayrıştırma yaklaşımı
- [alltechdev/mtk-imei-switcheroo](https://github.com/alltechdev/mtk-imei-switcheroo) — MTK NVRAM yapısı (yalnızca bilgi; root gerektirir, bu projede kullanılmaz)

Bu projede yalnızca **QCN dosya biçimi ve fastboot komutları** kullanıldı; root/exploit
tabanlı yöntemler bilinçli olarak eklenmedi.

## Testler

```powershell
dotnet run --project .tests\QcnRepairTest\QcnRepairTest.csproj -c Release
dotnet run --project .tests\CatalogTest\CatalogTest.csproj -c Release
```

QCN testi: Luhn, ASCII/BCD eşleşme, yedek, kaydetme, geri yükleme, bütünlük.
