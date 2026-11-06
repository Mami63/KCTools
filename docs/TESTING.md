# KC Tools — Test ve Stres Prosedürü

Bu belge, donanım erişimi olan bir makinede yapılacak manuel testleri tanımlar.
Otomasyon için `build-release.ps1` ve günlük dosyaları kullanılır:
`%LOCALAPPDATA%\KCTools\Logs\kctools-YYYYMMDD.log`

## 0. Ön koşullar

```powershell
dotnet dev-certs https --trust
dotnet run --project server/KCTools.LicenseServer        # lisans testleri için
```

- Uygulama yönetici olarak başlar (`app.manifest` → `requireAdministrator`).
- `tools\platform-tools` içinde `adb.exe`, `fastboot.exe`, `AdbWinApi.dll`,
  `AdbWinUsbApi.dll` bulunmalı (build çıktısına otomatik kopyalanır).
- Cihaz sürücüleri: Qualcomm QDLoader 9008 / MediaTek Preloader.
- Ayarlar → **Ortam Kontrolü** kartında tüm satırlar yeşil olmalı.

## 1. Port dinleme stres testi

| Adım | Beklenen |
|---|---|
| Cihazı EDL modunda tak | Sol panelde `COMx · Qualcomm EDL`, konsolda `Connected` satırı, durum çubuğu `EDL` |
| Cihazı çıkar | `Disconnected` satırı, liste güncellenir, arayüz donmaz |
| 30–50 kez tak/çıkar (EDL / fastboot / diag) | UI 1 sn'den uzun kilitlenmez, cihaz satırı çoğalmaz, bellek artışı sınırlı |
| Aynı anda 2 cihaz | Her ikisi ayrı satır; seçim korunur |
| USB hub'ı sök | Tüm satırlar temizlenir, uygulama çökmez |

İzleme: `DeviceWatcher` 400 ms debounce + 15 sn güvenlik taraması kullanır.
Ardışık olaylarda tarama sayısı, günlükteki `CİHAZ` satırlarından izlenebilir.

## 2. Hata yakalama (crash) senaryoları

| Senaryo | Beklenen davranış |
|---|---|
| Fastboot yazma sırasında kabloyu çek | `ProcessRunner` çıkış kodu/hata satırı yazar, VM hatayı gösterir, uygulama açık kalır |
| ADB `install` sırasında cihaz ayrılır | Komut hata döner, `Failure`/`error` günlüğe yazılır |
| AT portu açıkken cihaz çıkarılır | `SerialTransport` hata olayı → uyarı; port kapanır, UI donmaz |
| Lisans sunucusu kapatılır | Durum `Çevrimdışı`ya düşer (token süresi dolmadıysa), aktivasyon denenirse anlaşılır hata |
| QCN dosyası başka programda açık | Kaydetme hatası loglanır, dosya bozulmaz |
| Disk dolu | Günlük yazımı sessizce durur, uygulama çalışmaya devam eder |
| Gözlenmeyen task hatası | `TaskScheduler.UnobservedTaskException` yakalar, günlüğe yazar |

Tüm kritik USB/serial/süreç yolları `try/catch` + `Guard` ile sarılıdır;
`App.xaml.cs` ayrıca `DispatcherUnhandledException` ve `AppDomain.UnhandledException`
yakalayıcılarını kurar.

## 3. Async / UI donma testi

- 500 MB'lık bir QCN dosyası yükleyin: yükleme sırasında pencere sürüklenebilmeli.
- rawprogram XML (binlerce `<program>`) açın: arayüz donmamalı.
- Cihaz bilgisi (`getprop`) ve `getvar all` sırasında durum kartı güncellenmeli.
- Lisans doğrulaması açılışta arka planda; sunucu kapalıyken açılış 15 sn beklememeli
  (kayıtlı oturum yoksa ağ isteği yapılmaz).

## 4. Lisans testleri

| Anahtar | Beklenen |
|---|---|
| `KC-DEMO-DEMO-DEMO` | Aktivasyon başarılı, `PRO · 100 kredi` |
| Aynı anahtar başka makinede | `409` → "Lisans başka bir makineye bağlı" |
| `KC-XXXX` (bozuk) | Yerel biçim hatası, ağ isteği yok |
| Sunucu kapalı + kayıtlı oturum | `Çevrimdışı · N kredi`, süre dolunca `Süre doldu` |
| Kredi tüketimi | Her işlemde sunucu tarafında düşer, UI güncellenir |

## 5. Paketleme testi

```powershell
.\build-release.ps1
```

- `dist\app\platform-tools\adb.exe` ve `fastboot.exe` var mı?
- `dist\KCTools-Setup-1.0.0.exe` oluştu mu?
- Temiz bir makinede (veya sanal makinede) kurulum → sürücü görevi → uygulama açılışı.
- Kurulumdan sonra Ortam Kontrolü kartı sürücüleri "hazır" göstermeli.

## 6. Bilinen sınırlar

- Gerçek Firehose/Sahara ve MTK BROM aktarımı henüz yok; bu yollar loopback/plan seviyesinde.
- WMI olayları bazı sanal USB hub'larda gecikebilir; 15 sn güvenlik taraması devreye girer.
- Karartma sonrası WPF uygulaması mutlaka açılıp tüm sayfalar gezilmeli (bkz. `docs/OBFUSCATION.md`).

## 7. Telefonsuz IMEI onarım testi

QCN onarım mantığı sentetik bir dosyayla, gerçek üretim kodu üzerinden test edilir:

```powershell
dotnet run --project .tests\QcnRepairTest\QcnRepairTest.csproj -c Release
```

Kapsam: Luhn doğrulama/öneri, ASCII + BCD eşleşmesi, tüm eşleşmelerin değiştirilmesi,
yedek oluşturma, diskte doğrulama, yedekte eski IMEI'nin korunması, yeniden yükleme.
Beklenen çıktı: `SONUC: TUM TESTLER GECTI`.

## 8. Canlı cihaz testi (model + sinyal)

```powershell
dotnet run --project .tests\LiveDeviceTest\LiveDeviceTest.csproj -c Release
```

Beklenen: model/codename/marketname okunur, katalog eşleşmesi KESİN, sinyal ADB ile
(seviye + RSRP + operatör) gelir ve SONUC: CANLI TEST GECTI yazar.

